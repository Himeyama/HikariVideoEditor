[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HsisSource,
    [string]$SevenZipPath = "$env:ProgramFiles\7-Zip\7z.exe"
)

$ErrorActionPreference = 'Stop'
$sevenZip = (Get-Command $SevenZipPath -CommandType Application -ErrorAction Stop).Source
$sourcePath = Join-Path $HsisSource 'src/Hsis.Core/SevenZip.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
# Compatibility patch for the pinned v0.6.1: Windows tar cannot encode LZMA2
# or decode LZMA2 on all runners. Use 7-Zip for both build-time operations.
$original = 'ProcessRunner.RunAsync(Executable, ["-c", "--format=7zip", "--options=7zip:compression=lzma2,7zip:compression-level=9", "-f", Path.GetFullPath(archive), "."], source, log, cancellationToken);'
$replacement = 'ProcessRunner.RunAsync(Environment.GetEnvironmentVariable("HSIS_7ZIP_PATH") ?? throw new InvalidOperationException("HSIS_7ZIP_PATH is required."), ["a", "-t7z", "-m0=lzma2", "-mx=9", Path.GetFullPath(archive), ".\\*"], source, log, cancellationToken);'
if ($source.Split($original, [StringSplitOptions]::None).Count -ne 2) {
    throw 'Expected the original HSIS v0.6.1 archive creation code exactly once. Review the compatibility patch before updating HSIS.'
}
$originalExtract = 'ProcessRunner.RunAsync(Executable, ["-x", "-f", archive, "-C", destination, "-T", selection], destination, log, cancellationToken);'
$replacementExtract = 'ProcessRunner.RunAsync(Environment.GetEnvironmentVariable("HSIS_7ZIP_PATH") ?? throw new InvalidOperationException("HSIS_7ZIP_PATH is required."), ["x", "-y", "-scsUTF-8", $"-o{Path.GetFullPath(destination)}", Path.GetFullPath(archive), $"@{Path.GetFullPath(selection)}"], destination, log, cancellationToken);'
if ($source.Split($originalExtract, [StringSplitOptions]::None).Count -ne 2) {
    throw 'Expected the original HSIS v0.6.1 archive extraction code exactly once.'
}

# Verify both build-time operations without depending on Windows tar codecs.
$probe = Join-Path ([IO.Path]::GetTempPath()) ("hsis-lzma2-probe-" + [guid]::NewGuid().ToString('N'))
$inputPath = Join-Path $probe 'input'
$outputPath = Join-Path $probe 'output'
New-Item -ItemType Directory -Path $inputPath, $outputPath -Force | Out-Null
$content = 'HSIS LZMA2 round trip 日本語'
Set-Content -LiteralPath (Join-Path $inputPath 'probe.txt') -Value $content -Encoding utf8 -NoNewline
$archive = Join-Path $probe 'probe.7z'
& $sevenZip a -t7z -m0=lzma2 -mx=9 $archive "$inputPath\*"
if ($LASTEXITCODE -ne 0) { throw '7-Zip LZMA2 creation failed.' }
& $sevenZip x -y "-o$outputPath" $archive
if ($LASTEXITCODE -ne 0) { throw '7-Zip LZMA2 extraction failed.' }
if ((Get-Content -LiteralPath (Join-Path $outputPath 'probe.txt') -Raw) -cne $content) {
    throw 'LZMA2 round-trip content mismatch.'
}
Set-Content -LiteralPath $sourcePath -Value $source.Replace($original, $replacement).Replace($originalExtract, $replacementExtract) -Encoding utf8 -NoNewline
$env:HSIS_7ZIP_PATH = $sevenZip
if ($env:GITHUB_ENV) { "HSIS_7ZIP_PATH=$sevenZip" >> $env:GITHUB_ENV }
Write-Host 'HSIS v0.6.1 uses 7-Zip for LZMA2 creation and extraction; round trip passed.'
