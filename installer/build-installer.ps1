param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\installer",
    [string]$MakeNsis
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Version は 0.1.0 の形式で指定してください。'
}
if (($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) {
    throw 'バージョンの各数値は 65535 以下にしてください。'
}
if (!$MakeNsis) {
    $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($command) { $MakeNsis = $command.Source }
    else { $MakeNsis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe" }
}
if (!(Test-Path -LiteralPath $MakeNsis)) { throw 'NSIS 3 をインストールしてください (choco install nsis -y)。' }
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
# Each run publishes into an empty directory so old files cannot enter a release.
$publishPath = Join-Path $outputPath ("payload-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
& "$PSScriptRoot\..\build.ps1" -Configuration Release -Publish -Version $Version -OutputDirectory $publishPath -SkipLaunch
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
if (!(Test-Path "$publishPath\VideoEditor.exe")) { throw 'VideoEditor.exe がありません。' }
# A successful .NET publish can still omit WinUI's generated resources.
$requiredResources = @('VideoEditor.pri') + @(Get-ChildItem -LiteralPath "$PSScriptRoot\.." -Filter '*.xaml' -File | ForEach-Object { $_.BaseName + '.xbf' })
foreach ($resource in $requiredResources) {
    $resourcePath = Join-Path $publishPath $resource
    if (!(Test-Path -LiteralPath $resourcePath -PathType Leaf) -or (Get-Item -LiteralPath $resourcePath).Length -eq 0) {
        throw "起動に必要な WinUI リソースがありません: $resource"
    }
}
$installer = Join-Path $outputPath "VideoEditor-$Version-win-x64-Setup.exe"
& $MakeNsis /V3 "/DAPP_VERSION=$Version" "/DSOURCE_DIR=$publishPath" "/DOUTPUT_FILE=$installer" "$PSScriptRoot\VideoEditor.nsi"
if ($LASTEXITCODE -ne 0) { throw "NSIS failed: $LASTEXITCODE" }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
Write-Host "Installer: $installer"
