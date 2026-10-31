using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace VideoEditor;

// Stateless Streamable HTTP: JSON responses, no server-initiated SSE stream.
public sealed class Bridge : IDisposable
{
    public const int DefaultPort = 53100;
    private static readonly string[] _versions = ["2025-11-25", "2025-06-18", "2025-03-26"];
    private readonly WebApplication _server;
    private readonly Func<JsonElement, Task<object>> _dispatch;
    public int Port { get; }
    public string Endpoint => $"http://127.0.0.1:{Port}/mcp";

    public Bridge(Func<JsonElement, Task<object>> dispatch)
    {
        this._dispatch = dispatch;
        for (int port = DefaultPort; port <= 65535; port++)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port);
                options.Limits.MaxRequestBodySize = 262144;
            });
            WebApplication candidate = builder.Build();
            candidate.Run(Handle);
            try
            {
                Task.Run(() => candidate.StartAsync()).GetAwaiter().GetResult();
                _server = candidate;
                Port = port;
                return;
            }
            catch (IOException exception) when (AddressInUse(exception))
            {
                candidate.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        throw new IOException("MCP接続に使用できるポートがありません。");
    }

    private static bool AddressInUse(Exception exception) =>
        exception is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } ||
        exception.InnerException != null && AddressInUse(exception.InnerException);

    private async Task Handle(HttpContext context)
    {
        HttpRequest request = context.Request;
        if (request.Path != "/mcp" && request.Path != "/docs") { context.Response.StatusCode = 404; return; }
        if (request.Host.Host != "127.0.0.1" && request.Host.Host != "localhost")
        { context.Response.StatusCode = 403; return; }
        if (request.Headers.TryGetValue("Origin", out Microsoft.Extensions.Primitives.StringValues origin) &&
            (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out Uri? uri) ||
             uri.Scheme != "http" || (uri.Host != "127.0.0.1" && uri.Host != "localhost") || uri.Port != Port))
        { context.Response.StatusCode = 403; return; }
        if (request.Path == "/docs")
        {
            if (request.Method != "GET")
            {
                context.Response.Headers.Allow = "GET";
                context.Response.StatusCode = 405;
                return;
            }
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(McpDocumentation.Create(Port));
            return;
        }
        if (request.Headers.TryGetValue("MCP-Protocol-Version", out Microsoft.Extensions.Primitives.StringValues version) && !_versions.Contains(version.ToString()))
        { context.Response.StatusCode = 400; return; }
        if (request.Method != "POST")
        {
            context.Response.Headers.Allow = "POST";
            context.Response.StatusCode = 405;
            return;
        }
        if (!request.HasJsonContentType()) { context.Response.StatusCode = 415; return; }
        IList<Microsoft.Net.Http.Headers.MediaTypeHeaderValue> accept = request.GetTypedHeaders().Accept;
        if (accept == null || !accept.Any(value => value.MediaType == "application/json") ||
            !accept.Any(value => value.MediaType == "text/event-stream"))
        { context.Response.StatusCode = 406; return; }
        object? id = null;
        object response;
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("jsonrpc", out JsonElement rpc) || rpc.GetString() != "2.0" ||
                !root.TryGetProperty("method", out JsonElement method) || method.ValueKind != JsonValueKind.String)
            {
                await WriteError(context, null, -32600, "Invalid Request");
                return;
            }
            if (!root.TryGetProperty("id", out JsonElement requestId))
            {
                // Notifications never execute editor tools and have no JSON-RPC response.
                context.Response.StatusCode = 202;
                return;
            }
            if (requestId.ValueKind != JsonValueKind.String && requestId.ValueKind != JsonValueKind.Number)
            { await WriteError(context, null, -32600, "Invalid request id"); return; }
            id = requestId.Clone();
            JsonElement parameters = root.TryGetProperty("params", out JsonElement p) ? p : default;
            object result;
            switch (method.GetString())
            {
                case "initialize":
                    string? requested = parameters.GetProperty("protocolVersion").GetString();
                    _ = parameters.GetProperty("capabilities");
                    _ = parameters.GetProperty("clientInfo");
                    result = new
                    {
                        protocolVersion = _versions.Contains(requested) ? requested : _versions[0],
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "hikari-video-editor", version = "1.0.0" },
                        instructions = "Use project_get to inspect clips. After editing call preview_refresh. Times are seconds; layers start at 1. Paths refer to this Windows PC."
                    };
                    break;
                case "ping": result = new { }; break;
                case "tools/list": result = new { tools = McpTools.All }; break;
                case "tools/call":
                    string? name = parameters.GetProperty("name").GetString();
                    McpTools.Tool? tool = McpTools.All.SingleOrDefault(tool => tool.name == name);
                    if (tool == null) { await WriteError(context, id, -32602, "Unknown tool"); return; }
                    JsonElement arguments = parameters.TryGetProperty("arguments", out JsonElement a) ? a : JsonSerializer.SerializeToElement(new { });
                    if (!tool.ValidArguments(arguments))
                    { await WriteError(context, id, -32602, "Arguments do not match inputSchema"); return; }
                    try
                    {
                        JsonElement command = JsonSerializer.SerializeToElement(new { method = tool.Method, @params = arguments });
                        object value = await _dispatch(command);
                        result = new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(value) } }, isError = false };
                    }
                    catch (Exception exception)
                    {
                        result = new { content = new[] { new { type = "text", text = exception.Message } }, isError = true };
                    }
                    break;
                default: await WriteError(context, id, -32601, "Method not found"); return;
            }
            response = new { jsonrpc = "2.0", id, result };
        }
        catch (JsonException) { await WriteError(context, id, -32700, "Parse error"); return; }
        catch (BadHttpRequestException exception) { context.Response.StatusCode = exception.StatusCode; return; }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        { await WriteError(context, id, -32602, "Invalid params"); return; }
        await context.Response.WriteAsJsonAsync(response);
    }

    private static Task WriteError(HttpContext context, object? id, int code, string message) =>
        context.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, error = new { code, message } });

    public void Dispose()
    {
        // Stop asynchronously so an in-flight UI command cannot deadlock window shutdown.
        _ = Stop();
    }

    private async Task Stop()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        try { await _server.StopAsync(timeout.Token); }
        catch (OperationCanceledException) { }
        finally { await _server.DisposeAsync(); }
    }
}
