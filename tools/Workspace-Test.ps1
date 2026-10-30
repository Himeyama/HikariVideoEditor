param([int]$EditorProcessId, [int]$Port = 53100)
$ErrorActionPreference = 'Stop'

function Request([string]$method, $parameters = @{}) {
    $response = & "$PSScriptRoot\Invoke-Editor.ps1" -EditorProcessId $EditorProcessId -Port $Port -Method $method -ParamsJson ($parameters | ConvertTo-Json -Compress) | ConvertFrom-Json
    if ($response.error) { throw $response.error.message }
    return $response.result
}

$project = Request 'project.get'
if ($project.Clips.Count -ne 0) { throw '空のテスト用アプリで実行してください。' }

$directory = (Resolve-Path (Join-Path $PSScriptRoot '..\artifacts\playback')).Path
Request 'workspace.set' @{ path = $directory } | Out-Null
$clip = Request 'clip.add' @{ path = (Join-Path $directory 'motion-test.mp4') }
Request 'clip.update' @{ id = $clip.Id; duration = 1; offsetX = 120; offsetY = -80; scale = 0.5 } | Out-Null
$duplicate = Request 'clip.duplicate' @{ id = $clip.Id }
if ($duplicate.Id -eq $clip.Id -or $duplicate.Start -ne 1 -or $duplicate.Scale -ne 0.5 -or $duplicate.OffsetX -ne 120 -or $duplicate.OffsetY -ne -80) {
    throw '複製で値が保持されませんでした。'
}

$file = Join-Path $directory 'workspace-project.hve'
Request 'project.save' @{ path = $file } | Out-Null
Request 'clip.delete' @{ id = $duplicate.Id } | Out-Null
Request 'project.open' @{ path = $file } | Out-Null
$restored = Request 'project.get'
if ($restored.Clips.Count -ne 2 -or $restored.WorkspaceDirectory -ne $directory -or $restored.Clips[1].Scale -ne 0.5) {
    throw 'ワークスペース・配置の復元が一致しません。'
}
if ($restored.Video.Width -ne 1920 -or $restored.Video.AudioSampleRate -ne 44100) {
    throw '出力設定が一致しません。'
}

$invalid = & "$PSScriptRoot\Invoke-Editor.ps1" -EditorProcessId $EditorProcessId -Port $Port -Method clip.update -ParamsJson (@{ id=$clip.Id; scale=0 } | ConvertTo-Json -Compress) | ConvertFrom-Json
if (!$invalid.error) { throw '不正な拡大率が拒否されませんでした。' }
Request 'preview.refresh' | Out-Null
Request 'export.mp4' @{ path = (Join-Path $directory ("workspace-export-" + [guid]::NewGuid().ToString('N') + '.mp4')) } | Out-Null
'PASS: workspace selection, duplicate identity and transforms, save/open, output settings, invalid scale rejection, MP4 export'
