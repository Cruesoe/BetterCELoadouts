param([string]$RimWorldRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld')

# Checks run by Deploy-Mod before deploying: translation keys (ours and the CE keys we reuse), textures, no tiny fonts, metadata.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$failures = New-Object System.Collections.Generic.List[string]

$sourceText = (Get-ChildItem (Join-Path $root 'Source') -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"

$used = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in [regex]::Matches($sourceText, '"(BetterCELoadouts_[A-Za-z0-9_]+)"')) { [void]$used.Add($m.Groups[1].Value) }

$keyed = New-Object System.Collections.Generic.HashSet[string]
foreach ($file in Get-ChildItem (Join-Path $root 'Languages\English\Keyed') -Filter *.xml) {
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($text -match '[ \t]+\r?\n') { $failures.Add("Trailing whitespace in $($file.Name)") }
    [xml]$xml = $text
    foreach ($node in $xml.LanguageData.ChildNodes) {
        if ($node.NodeType -eq 'Element' -and -not $keyed.Add($node.Name)) { $failures.Add("Duplicate key $($node.Name)") }
    }
}
foreach ($key in $used) { if (-not $keyed.Contains($key)) { $failures.Add("Missing translation: $key") } }
foreach ($key in $keyed) { if (-not $used.Contains($key)) { $failures.Add("Unused translation: $key") } }

# CE's own keys the code reuses must exist in the official Combat Extended (Workshop 3495749827), when it's installed.
$ceKeyed = Join-Path $RimWorldRoot '..\..\workshop\content\294100\3495749827\Languages\English\Keyed'
if (Test-Path $ceKeyed) {
    $ceKeys = New-Object System.Collections.Generic.HashSet[string]
    foreach ($file in Get-ChildItem $ceKeyed -Filter *.xml) {
        foreach ($m in [regex]::Matches([IO.File]::ReadAllText($file.FullName), '<(CE_[A-Za-z0-9_]+)>')) { [void]$ceKeys.Add($m.Groups[1].Value) }
    }
    foreach ($m in [regex]::Matches($sourceText, '"(CE_[A-Za-z0-9_]+)"\.Translate')) {
        if (-not $ceKeys.Contains($m.Groups[1].Value)) { $failures.Add("Combat Extended has no key $($m.Groups[1].Value)") }
    }
} else {
    'Combat Extended not installed; skipping its keys.'
}

foreach ($m in [regex]::Matches($sourceText, 'ContentFinder<Texture2D>\.Get\("([^"]+)"\s*[,)]')) {
    $texture = Join-Path $root ('Textures\' + $m.Groups[1].Value.Replace('/', '\') + '.png')
    if (-not (Test-Path $texture)) { $failures.Add("Missing texture: $($m.Groups[1].Value)") }
}

if ($sourceText -match 'GameFont\.Tiny') { $failures.Add('GameFont.Tiny is used') }

[xml]$about = [IO.File]::ReadAllText((Join-Path $root 'About\About.xml'))
if ($about.ModMetaData.packageId -ne 'cruesoe.betterceloadouts') { $failures.Add('Wrong package ID') }
if (@($about.ModMetaData.modDependencies.li.packageId) -notcontains 'CETeam.CombatExtended') { $failures.Add('Combat Extended is not a dependency') }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { "FAIL: $_" }
    exit 1
}
'All checks passed.'
