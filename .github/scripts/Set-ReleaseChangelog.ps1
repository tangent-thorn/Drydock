# Takes the section for one version out of CHANGELOG.md, writes it to a notes file for the
# GitHub Release, and sets it as Changelog in the given manifests (the plugin's Drydock.json
# before the build packages it, repo.json after). Fails when the version has no section, so
# a release cannot go out without one.
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $ChangelogPath,
    [string] $NotesFile,
    [string[]] $Manifest = @()
)
$ErrorActionPreference = 'Stop'

$text = (Get-Content -LiteralPath $ChangelogPath -Raw) -replace "`r`n", "`n"
$pattern = '(?ms)^## ' + [regex]::Escape($Version) + '[ \t]*\n(.*?)(?=^## |\z)'
$match = [regex]::Match($text, $pattern)
$notes = if ($match.Success) { $match.Groups[1].Value.Trim() } else { '' }
if (-not $notes) { throw "CHANGELOG.md has no section '## $Version'" }

$utf8 = New-Object System.Text.UTF8Encoding($false)
if ($NotesFile) { [System.IO.File]::WriteAllText($NotesFile, $notes + "`n", $utf8) }

# pwsh -File passes -Manifest 'a','b' as one string.
foreach ($path in ($Manifest -split ',' | Where-Object { $_ })) {
    $full = (Resolve-Path -LiteralPath $path).Path
    # -NoEnumerate keeps repo.json's one-entry array an array.
    $json = Get-Content -LiteralPath $full -Raw | ConvertFrom-Json -NoEnumerate
    $isArray = $json -is [array]
    $entries = if ($isArray) { $json | Where-Object { $_.InternalName -eq 'Drydock' } } else { @($json) }
    if (-not $entries) { throw "No Drydock entry in $path" }
    foreach ($entry in $entries) {
        $entry | Add-Member -NotePropertyName Changelog -NotePropertyValue $notes -Force
    }
    $out = if ($isArray) { ConvertTo-Json -InputObject @($json) -Depth 10 } else { ConvertTo-Json -InputObject $json -Depth 10 }
    [System.IO.File]::WriteAllText($full, $out + "`n", $utf8)
}

$notes
