param([int]$EditorProcessId, [int]$Port = 53100)
$ErrorActionPreference = 'Stop'
$testDir = Join-Path $PSScriptRoot '..\artifacts\smoke'
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$testDir = (Resolve-Path $testDir).Path
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(640, 360)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::CornflowerBlue)
$font = [System.Drawing.Font]::new('Arial', 32)
$graphics.DrawString('Hikari Video Editor', $font, [System.Drawing.Brushes]::White, 90, 145)
$imagePath = Join-Path $testDir 'sample.png'
$bitmap.Save($imagePath, [System.Drawing.Imaging.ImageFormat]::Png)
$font.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
function Invoke-CommandJson([string]$method, $parameters = @{}) {
    $json = $parameters | ConvertTo-Json -Compress
    $response = & "$PSScriptRoot\Invoke-Editor.ps1" -EditorProcessId $EditorProcessId -Port $Port -Method $method -ParamsJson $json | ConvertFrom-Json
    if ($response.error) { throw $response.error.message }
    return $response.result
}
$original = Invoke-CommandJson 'project.get'
if ($original.Clips.Count -ne 0) { throw '空のテスト用アプリで実行してください。' }
$clip = Invoke-CommandJson 'clip.add' @{path=$imagePath}
Invoke-CommandJson 'clip.update' @{id=$clip.Id; start=0; duration=1; layer=2; volume=0.5} | Out-Null
Invoke-CommandJson 'preview.refresh' | Out-Null
Invoke-CommandJson 'playback.play' | Out-Null
Invoke-CommandJson 'playback.pause' | Out-Null
Invoke-CommandJson 'playback.seek' @{seconds=0.5} | Out-Null
$projectPath = Join-Path $testDir 'project.hve'
Invoke-CommandJson 'project.save' @{path=$projectPath} | Out-Null
Invoke-CommandJson 'clip.delete' @{id=$clip.Id} | Out-Null
Invoke-CommandJson 'project.open' @{path=$projectPath} | Out-Null
$loaded = Invoke-CommandJson 'project.get'
if ($loaded.Clips.Count -ne 1 -or $loaded.Clips[0].Duration -ne 1 -or $loaded.Clips[0].Layer -ne 2) { throw 'プロジェクトの復元が一致しません。' }
$bad = & "$PSScriptRoot\Invoke-Editor.ps1" -EditorProcessId $EditorProcessId -Port $Port -Method clip.update -ParamsJson (@{id=$clip.Id;duration=-1}|ConvertTo-Json -Compress) | ConvertFrom-Json
if (!$bad.error) { throw '不正な長さが拒否されませんでした。' }
$unchanged = Invoke-CommandJson 'project.get'
if ($unchanged.Clips[0].Duration -ne 1) { throw '不正な更新でプロジェクトが変わりました。' }
$outputPath = Join-Path $testDir ("export-" + [guid]::NewGuid().ToString('N') + '.mp4')
Invoke-CommandJson 'export.mp4' @{path=$outputPath} | Out-Null
if ((Get-Item $outputPath).Length -lt 1000) { throw 'MP4 が生成されませんでした。' }
$overwrite = & "$PSScriptRoot\Invoke-Editor.ps1" -EditorProcessId $EditorProcessId -Port $Port -Method export.mp4 -ParamsJson (@{path=$outputPath}|ConvertTo-Json -Compress) | ConvertFrom-Json
if (!$overwrite.error) { throw 'API が既存の出力を上書きしました。' }
"PASS: import, update, preview, playback, save/open, delete, invalid update rollback, MP4 export, overwrite rejection"
$outputPath
