$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) { throw 'Visual Studio 2022 の .NET デスクトップ開発と Windows App SDK 開発ツールをインストールしてください。' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild が見つかりません。' }
& $msbuild "$PSScriptRoot\VideoEditor.csproj" /restore /p:Platform=x64 /p:Configuration=Debug /p:NuGetAudit=false /verbosity:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
