param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\installer",
    [string]$HsisPath = 'hsis.exe'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Version は 0.1.0 の形式で指定してください。'
}
if (($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) {
    throw 'バージョンの各数値は 65535 以下にしてください。'
}
$hsis = (Get-Command $HsisPath -CommandType Application -ErrorAction Stop).Source
$templatePath = Join-Path (Split-Path -Parent $hsis) 'Template'
[xml]$hsisProperties = Get-Content -LiteralPath (Join-Path $templatePath 'Directory.Build.props') -Raw
if ($hsisProperties.Project.PropertyGroup.Version -ne '0.5.1') { throw 'HSIS v0.5.1 を使用してください。' }
if (!(Test-Path -LiteralPath (Join-Path $templatePath 'src/Hsis.App/Hsis.ico') -PathType Leaf)) {
    throw 'HSIS テンプレートに Hsis.ico がありません。HSIS v0.5.1 の src/Hsis.App/Hsis.ico を同じ場所へコピーしてください。'
}
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
# Each run publishes into an empty directory so old files cannot enter a release.
$publishPath = Join-Path $outputPath ("payload-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
& "$PSScriptRoot\..\build.ps1" -Configuration Release -Publish -Version $Version -OutputDirectory $publishPath -SkipLaunch
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
if (!(Test-Path "$publishPath\VideoEditor.exe")) { throw 'VideoEditor.exe がありません。' }
# Reject framework-dependent or incomplete output before packaging it.
$runtime = Get-Content -LiteralPath (Join-Path $publishPath 'VideoEditor.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or !$runtime.runtimeOptions.includedFrameworks) {
    throw 'VideoEditor must be published with SelfContained=true. External .NET runtime requirements are not allowed in the installer.'
}
foreach ($runtimeFile in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $publishPath $runtimeFile) -PathType Leaf)) {
        throw "Missing bundled .NET runtime file: $runtimeFile"
    }
}
# A successful .NET publish can still omit WinUI's generated resources.
$requiredResources = @('VideoEditor.pri') + @(Get-ChildItem -LiteralPath "$PSScriptRoot\.." -Filter '*.xaml' -File | ForEach-Object { $_.BaseName + '.xbf' })
foreach ($resource in $requiredResources) {
    $resourcePath = Join-Path $publishPath $resource
    if (!(Test-Path -LiteralPath $resourcePath -PathType Leaf) -or (Get-Item -LiteralPath $resourcePath).Length -eq 0) {
        throw "起動に必要な WinUI リソースがありません: $resource"
    }
}
$installerName = "VideoEditor-$Version-win-x64-Setup.exe"
$installer = Join-Path $outputPath $installerName
$hsisOutput = Join-Path $outputPath ("hsis-" + [guid]::NewGuid().ToString('N'))
& $hsis build "$PSScriptRoot\VideoEditor.nsi" --output $hsisOutput "-DAPP_VERSION=$Version" "-DSOURCE_DIR=$publishPath" "-DOUTPUT_FILE=$installerName"
if ($LASTEXITCODE -ne 0) { throw "HSIS failed: $LASTEXITCODE" }
$generated = @(Get-ChildItem -LiteralPath $hsisOutput -Directory -Filter 'Setup-*' |
    ForEach-Object { Join-Path $_.FullName $installerName } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
if ($generated.Count -ne 1) { throw 'HSIS が生成したインストーラーを特定できません。' }
# Preserve the compiled filename: renaming a WinUI executable breaks resource lookup.
Copy-Item -LiteralPath $generated[0] -Destination $installer -Force
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
Write-Host "Installer: $installer"
