param([int]$EditorProcessId, [int]$Port = 53100)
$ErrorActionPreference = 'Stop'

function Request([string]$method, $parameters = @{}) {
    $response = & "$PSScriptRoot\Invoke-Editor.ps1" -EditorProcessId $EditorProcessId -Port $Port -Method $method -ParamsJson ($parameters | ConvertTo-Json -Compress) | ConvertFrom-Json
    if ($response.error) { throw $response.error.message }
    return $response.result
}

if ((Request 'project.get').Clips.Count -ne 0) { throw '空のテスト用アプリで実行してください。' }
$path = (Resolve-Path (Join-Path $PSScriptRoot '..\artifacts\playback\motion-test.mp4')).Path
$first = Request 'clip.add' @{ path=$path; atPlayhead=$true }
$second = Request 'clip.add' @{ path=$path; atPlayhead=$true }
$third = Request 'clip.add' @{ path=$path; atPlayhead=$true }
if ($first.Start -ne 0 -or $second.Start -ne 0 -or $third.Start -ne 0 -or $first.Layer -ne 1 -or $second.Layer -ne 2 -or $third.Layer -ne 3) {
    throw '再生位置での最小レイヤー配置が一致しません。'
}

Request 'selection.set' @{ ids=@($first.Id,$second.Id) } | Out-Null
Request 'selection.move' @{ seconds=2; layers=2 } | Out-Null
$project = Request 'project.get'
$movedFirst = $project.Clips | Where-Object Id -eq $first.Id
$movedSecond = $project.Clips | Where-Object Id -eq $second.Id
$unchanged = $project.Clips | Where-Object Id -eq $third.Id
if ($movedFirst.Start -ne 2 -or $movedSecond.Start -ne 2 -or $movedFirst.Layer -ne 3 -or $movedSecond.Layer -ne 4) {
    throw '複数移動で相対位置が維持されませんでした。'
}
if ($unchanged.Start -ne 0 -or $unchanged.Layer -ne 3) { throw '選択していない素材が移動しました。' }
if ((Request 'selection.get').ids.Count -ne 2) { throw '移動後に複数選択が解除されました。' }

Request 'selection.move' @{ seconds=-100; layers=100 } | Out-Null
$clamped = Request 'project.get'
$firstClamped = $clamped.Clips | Where-Object Id -eq $first.Id
$secondClamped = $clamped.Clips | Where-Object Id -eq $second.Id
if ($firstClamped.Start -ne 0 -or $secondClamped.Start -ne 0 -or $firstClamped.Layer -ne 7 -or $secondClamped.Layer -ne 8) {
    throw '境界でグループ全体が制限されませんでした。'
}
'PASS: playhead insertion into layers 1/2/3, multiple selection, relative time/layer movement, unselected clips unchanged, group boundary limits'
