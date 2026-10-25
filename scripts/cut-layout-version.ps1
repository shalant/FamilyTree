# cut-layout-version.ps1  --  Cuts a version of the Timeline layout engine (added 2026-10-25).
#
# Sets TimelineLayoutEngine.Version to -Version. With -Best, also copies TimelineLayoutEngine.cs
# over BestTimelineLayoutEngine.cs (renamed class, FROZEN header), so the picker's
# "Best so far" option draws exactly this version until the next -Best cut.
#
#   ./scripts/cut-layout-version.ps1 -Version v2          # a new version, not better than best
#   ./scripts/cut-layout-version.ps1 -Version v2 -Best    # a new version that is the new best
#
# Then, on the same branch: add the version's row to docs/Testing/layout-versions.md (the row
# is printed by LayoutVersionHistoryTests), run the tests, open the PR. After it is merged,
# tag the merge commit: git tag layout-v2 <merge sha>; git push origin layout-v2.
# Full workflow: docs/Testing/layout-versions.md.
#
# Reads and writes the files as UTF-8 without a BOM through .NET, never Get-Content/Set-Content,
# which corrupt the box-drawing characters in the engine's comments.

param(
    [Parameter(Mandatory = $true)][ValidatePattern('^v[0-9]+$')][string]$Version,
    [switch]$Best
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$services = Join-Path $PSScriptRoot '..\src\FamilyTree.Web\Services'
$newestPath = Join-Path $services 'TimelineLayoutEngine.cs'
$bestPath = Join-Path $services 'BestTimelineLayoutEngine.cs'

$pattern = 'public const string Version = "v[0-9]+";'
$text = [IO.File]::ReadAllText($newestPath, $utf8)
if (-not [regex]::IsMatch($text, $pattern)) { throw "No Version constant found in $newestPath" }
$text = [regex]::Replace($text, $pattern, "public const string Version = `"$Version`";")
[IO.File]::WriteAllText($newestPath, $text, $utf8)
Write-Host "TimelineLayoutEngine.Version = $Version"

if ($Best) {
    $classLine = 'public class TimelineLayoutEngine : IFamilyTreeLayoutEngine'
    if (-not $text.Contains($classLine)) { throw "Class declaration not found in $newestPath" }
    $header = @"
// FROZEN: TimelineLayoutEngine as cut at $Version (git tag layout-$Version), drawn by the picker's
// "Best so far" option. Never edit it by hand: it is replaced whole by
// scripts/cut-layout-version.ps1 -Best. Shared code it calls (FamilyTreeLayoutEngine's connector
// builder and records) is not frozen; change that only through new parameters, so this keeps
// drawing as it did. docs/Testing/layout-versions.md.
public class BestTimelineLayoutEngine : IFamilyTreeLayoutEngine
"@
    $snapshot = $text.Replace($classLine, $header.Replace("`r`n", "`n"))
    [IO.File]::WriteAllText($bestPath, $snapshot, $utf8)
    Write-Host "BestTimelineLayoutEngine = $Version"
}
