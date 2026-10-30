param(
    [int]$EditorProcessId,
    [int]$Port = 53100,
    [string]$Method = 'ping',
    [string]$ParamsJson = '{}'
)
$ErrorActionPreference = 'Stop'
if ($EditorProcessId) {
    $connection = Get-NetTCPConnection -State Listen -OwningProcess $EditorProcessId |
        Where-Object { $_.LocalAddress -eq '127.0.0.1' -and $_.LocalPort -ge 53100 } |
        Sort-Object LocalPort | Select-Object -First 1
    if (!$connection) { throw 'Editor MCP endpoint not found.' }
    $Port = $connection.LocalPort
}
$endpoint = "http://127.0.0.1:$Port/mcp"
$headers = @{ Accept = 'application/json, text/event-stream'; 'MCP-Protocol-Version' = '2025-11-25' }
function Send-Mcp($payload) {
    $body = $payload | ConvertTo-Json -Depth 30 -Compress
    Invoke-RestMethod -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 3600
}
$null = Send-Mcp @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'Invoke-Editor'; version = '1.0' } } }
$null = Send-Mcp @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
if ($Method -eq 'ping') {
    $response = Send-Mcp @{ jsonrpc = '2.0'; id = 2; method = 'ping' }
    $response | ConvertTo-Json -Depth 30 -Compress
} else {
    $response = Send-Mcp @{ jsonrpc = '2.0'; id = 2; method = 'tools/call'; params = @{ name = $Method.Replace('.', '_'); arguments = ($ParamsJson | ConvertFrom-Json) } }
    # Keep existing test/client output shape while using MCP on the wire.
    if ($response.error) {
        @{ id = 2; error = $response.error } | ConvertTo-Json -Depth 30 -Compress
    } elseif ($response.result.isError) {
        @{ id = 2; error = @{ message = $response.result.content[0].text } } | ConvertTo-Json -Depth 30 -Compress
    } else {
        @{ id = 2; result = ($response.result.content[0].text | ConvertFrom-Json) } | ConvertTo-Json -Depth 30 -Compress
    }
}
