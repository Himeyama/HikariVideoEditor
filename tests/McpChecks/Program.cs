using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VideoEditor;

void Check(bool value, string label) { if (!value) throw new Exception(label); }
var blocker = new TcpListener(IPAddress.Loopback, Bridge.DefaultPort);
try { blocker.Start(); }
catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
{
    // An already-running editor also occupies the base port for this test.
}
int calls = 0;
using var bridge = new Bridge(request =>
{
    calls++;
    if (request.GetProperty("method").GetString() == "clip.delete") throw new ArgumentException("missing clip");
    return Task.FromResult<object>(new { received = request.GetProperty("method").GetString() });
});
Check(bridge.Port > Bridge.DefaultPort, "port fallback");
using var second = new Bridge(_ => Task.FromResult<object>(new { }));
Check(second.Port > bridge.Port, "second instance fallback");
using var client = new HttpClient();
using (var docs = await client.GetAsync($"http://127.0.0.1:{bridge.Port}/docs"))
{
    var guide = await docs.Content.ReadAsStringAsync();
    Check(docs.IsSuccessStatusCode && guide.Contains($"POST {bridge.Endpoint}") &&
        guide.Contains("tools/list") && guide.Contains("clip_update") && guide.Contains("preview_refresh"), "documentation URL and actual port");
}
using (var wrongMethod = await client.PostAsync($"http://127.0.0.1:{bridge.Port}/docs", null))
    Check(wrongMethod.StatusCode == HttpStatusCode.MethodNotAllowed, "documentation method");
async Task<HttpResponseMessage> Send(string body, string? origin = null, string version = "2025-11-25")
{
    var request = new HttpRequestMessage(HttpMethod.Post, bridge.Endpoint);
    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
    request.Headers.Add("MCP-Protocol-Version", version);
    if (origin != null) request.Headers.Add("Origin", origin);
    return await client.SendAsync(request);
}
async Task<JsonElement> Rpc(string body)
{
    using var response = await Send(body);
    Check(response.IsSuccessStatusCode, "HTTP status");
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
var init = await Rpc("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
Check(init.GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25", "initialize");
using (var notification = await Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""))
    Check(notification.StatusCode == HttpStatusCode.Accepted && (await notification.Content.ReadAsByteArrayAsync()).Length == 0, "notification");
var list = await Rpc("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
Check(list.GetProperty("result").GetProperty("tools").GetArrayLength() == 21, "tools list");
var result = await Rpc("""{"jsonrpc":"2.0","id":"call","method":"tools/call","params":{"name":"project_get","arguments":{}}}""");
Check(!result.GetProperty("result").GetProperty("isError").GetBoolean() && calls == 1, "tool dispatch");
result = await Rpc("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"clip_delete","arguments":{"id":"missing"}}}""");
Check(result.GetProperty("result").GetProperty("isError").GetBoolean(), "tool failure");
result = await Rpc("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"clip_update","arguments":{"id":"x","layer":"bad"}}}""");
Check(result.GetProperty("error").GetProperty("code").GetInt32() == -32602 && calls == 2, "schema validation");
result = await Rpc("{");
Check(result.GetProperty("error").GetProperty("code").GetInt32() == -32700, "parse error");
result = await Rpc("""{"jsonrpc":"2.0","id":5,"method":"unknown"}""");
Check(result.GetProperty("error").GetProperty("code").GetInt32() == -32601, "unknown method");
using (var forbidden = await Send("{}", "https://evil.example")) Check(forbidden.StatusCode == HttpStatusCode.Forbidden, "Origin");
using (var unsupported = await Send("{}", version: "bad")) Check(unsupported.StatusCode == HttpStatusCode.BadRequest, "version");
using (var get = await client.GetAsync(bridge.Endpoint)) Check(get.StatusCode == HttpStatusCode.MethodNotAllowed, "GET without SSE");
using (var oversize = await Send(new string(' ', 262145))) Check(oversize.StatusCode == HttpStatusCode.RequestEntityTooLarge, "body limit");
var names = list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToArray();
Check(new[] { "speech_voices", "speech_create", "image_create" }.All(names.Contains), "generation tools exposed");
foreach (var call in new[] {
    """{"jsonrpc":"2.0","id":20,"method":"tools/call","params":{"name":"speech_voices","arguments":{}}}""",
    """{"jsonrpc":"2.0","id":21,"method":"tools/call","params":{"name":"speech_create","arguments":{"text":"test","rate":0,"addToTimeline":true}}}""",
    """{"jsonrpc":"2.0","id":22,"method":"tools/call","params":{"name":"image_create","arguments":{"title":"test","width":1920,"duration":6}}}"""
}) {
    var generated = await Rpc(call);
    Check(!generated.GetProperty("result").GetProperty("isError").GetBoolean(), "generation tool dispatch");
}
var invalid = await Rpc("""{"jsonrpc":"2.0","id":23,"method":"tools/call","params":{"name":"speech_create","arguments":{"rate":0}}}""");
Check(invalid.GetProperty("error").GetProperty("code").GetInt32() == -32602, "speech text required");
blocker.Stop();
Console.WriteLine("PASS: port fallback, multiple instances, initialization, notification, 21 tools, dispatch, errors, schema, Origin, protocol version, GET, body limit");
