param([switch]$SkipLaunch, [string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) { throw 'Visual Studio 2022 の .NET デスクトップ開発と Windows App SDK 開発ツールをインストールしてください。' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild が見つかりません。' }
$outputArguments = @()
if ($OutputDirectory) {
    $outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
    $outputArguments = @("/p:OutDir=$outputPath\")
}
& $msbuild "$PSScriptRoot\VideoEditor.csproj" /restore /p:Platform=x64 /p:Configuration=Debug /p:NuGetAudit=false /verbosity:minimal @outputArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (!$SkipLaunch) {
    $executable = Join-Path $PSScriptRoot 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\VideoEditor.exe'
    if ($OutputDirectory) { $executable = Join-Path $outputPath 'VideoEditor.exe' }
    $app = Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -PassThru
    Start-Sleep -Seconds 2
    $app.Refresh()
    if ($app.HasExited) { throw "アプリが起動直後に終了しました（終了コード: $($app.ExitCode)）。" }
    Write-Host "アプリを起動しました（PID: $($app.Id)）。"
}
