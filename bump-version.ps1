<#
.SYNOPSIS
ステージ済みの変更にバージョン更新を加えてコミットし、同じ番号のタグを作成します。
.EXAMPLE
./bump-version.ps1 -Part Minor -Message 'feat: 新機能'
#>
[CmdletBinding()]
param(
    [ValidateSet('Patch', 'Minor', 'Major')]
    [string]$Part = 'Patch',
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Message
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string[]]$Arguments)
    $result = & git -C $PSScriptRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed ($LASTEXITCODE)." }
    return $result
}

$projectPath = Join-Path $PSScriptRoot 'VideoEditor.csproj'
$projectText = [System.IO.File]::ReadAllText($projectPath)
$versionPattern = '<Version>(\d+\.\d+\.\d+)</Version>'
$matchesFound = [regex]::Matches($projectText, $versionPattern)
if ($matchesFound.Count -ne 1) { throw 'Expected exactly one X.Y.Z Version in VideoEditor.csproj.' }

# Reject an unfinished operation or detached HEAD before changing files.
$null = Invoke-Git -Arguments @('symbolic-ref', '--quiet', 'HEAD')
foreach ($marker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply')) {
    $markerPath = Invoke-Git -Arguments @('rev-parse', '--git-path', $marker)
    if (![System.IO.Path]::IsPathRooted($markerPath)) { $markerPath = Join-Path $PSScriptRoot $markerPath }
    if (Test-Path -LiteralPath $markerPath) { throw 'Finish the current Git operation before bumping the version.' }
}
$stagedFiles = @(Invoke-Git -Arguments @('diff', '--cached', '--name-only'))
if ($stagedFiles.Count -eq 0) { throw 'Stage the changes to commit with git add first.' }
$unstagedProject = @(Invoke-Git -Arguments @('diff', '--name-only', '--', 'VideoEditor.csproj'))
if ($unstagedProject.Count -gt 0) { throw 'Stage all VideoEditor.csproj changes first to avoid including unstaged edits.' }

[version]$currentVersion = $matchesFound[0].Groups[1].Value
$tags = @(Invoke-Git -Arguments @('tag', '--list', 'v*'))
foreach ($existingTag in $tags) {
    if ($existingTag -match '^v(\d+\.\d+\.\d+)$') {
        [version]$tagVersion = $Matches[1]
        if ($tagVersion -gt $currentVersion) { $currentVersion = $tagVersion }
    }
}
$nextVersion = switch ($Part) {
    'Major' { '{0}.0.0' -f ($currentVersion.Major + 1) }
    'Minor' { '{0}.{1}.0' -f $currentVersion.Major, ($currentVersion.Minor + 1) }
    'Patch' { '{0}.{1}.{2}' -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1) }
}
$tag = "v$nextVersion"
$updatedText = [regex]::Replace($projectText, $versionPattern, "<Version>$nextVersion</Version>")
[System.IO.File]::WriteAllText($projectPath, $updatedText, [System.Text.UTF8Encoding]::new($false))
$null = Invoke-Git -Arguments @('add', '--', 'VideoEditor.csproj')
Invoke-Git -Arguments @('commit', '-m', $Message)

# If tagging fails, leave the successful commit intact and show the recovery command.
try {
    $committedProject = (Invoke-Git -Arguments @('show', 'HEAD:VideoEditor.csproj')) -join "`n"
    if ($committedProject -notmatch "<Version>$([regex]::Escape($nextVersion))</Version>") {
        throw 'The committed project version differs from the requested version.'
    }
    $null = Invoke-Git -Arguments @('tag', $tag, 'HEAD')
}
catch {
    throw "Commit created, but tagging failed. Check HEAD and run: git tag $tag HEAD. $($_.Exception.Message)"
}
Write-Host "Created commit and tag $tag (VideoEditor.csproj: $nextVersion)."
