param([int]$EditorProcessId, [int]$Port = 53100)
$ErrorActionPreference = 'Stop'
function Request([string]$method, $parameters = @{}) {
    $response = & "$PSScriptRoot\Invoke-Editor.ps1" -EditorProcessId $EditorProcessId -Port $Port -Method $method -ParamsJson ($parameters | ConvertTo-Json -Compress) | ConvertFrom-Json
    if ($response.error) { throw $response.error.message }
    return $response.result
}
$project = Request 'project.get'
if ($project.Clips.Count -ne 0) { throw '空のテスト用アプリで実行してください。' }
$path = (Resolve-Path (Join-Path $PSScriptRoot '..\artifacts\playback\motion-test.mp4')).Path
Request 'clip.add' @{ path = $path } | Out-Null
Request 'preview.refresh' | Out-Null
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $state = Request 'playback.get'
    if ($state.error) { throw $state.error }
    if (!$state.opening -and $state.hasSource) { break }
    Start-Sleep -Milliseconds 250
}
if ($state.opening) { throw 'プレビューの読み込みが完了しません。' }
Request 'playback.seek' @{seconds=0} | Out-Null
Request 'playback.play' | Out-Null
Start-Sleep -Milliseconds 800
$playing = Request 'playback.get'
if ($playing.state -ne 'Playing' -or $playing.seconds -lt 0.2) { throw "再生時間が進みません: $($playing|ConvertTo-Json -Compress)" }
Request 'playback.pause' | Out-Null
Start-Sleep -Milliseconds 100
$paused = Request 'playback.get'
Start-Sleep -Milliseconds 300
$stillPaused = Request 'playback.get'
if ($stillPaused.state -ne 'Paused' -or [Math]::Abs($stillPaused.seconds - $paused.seconds) -gt 0.07) { throw '一時停止中も再生位置が進みます。' }
Request 'playback.seek' @{seconds=2.5} | Out-Null
Start-Sleep -Milliseconds 200
$seek = Request 'playback.get'
if ([Math]::Abs($seek.seconds - 2.5) -gt 0.1) { throw "シーク位置が違います: $($seek.seconds)" }
"PASS: video opens, playback clock advances ($($playing.seconds)s), pause holds, seek reaches 2.5s"
