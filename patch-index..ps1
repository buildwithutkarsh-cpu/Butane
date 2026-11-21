# Run from the repo folder:  .\patch-index.ps1
# Optional version labels:   .\patch-index.ps1 -Version 1.0.1
param(
    [string]$Path = "index.html",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $Path)) { throw "Cannot find $Path. Run this from the repo folder." }

$utf8 = New-Object System.Text.UTF8Encoding($false)
$text = [System.IO.File]::ReadAllText((Resolve-Path $Path), $utf8)
$original = $text

# 1) Point every download button at the newest release asset.
$pattern = 'https://github\.com/buildwithutkarsh-cpu/Butane/releases/download/[^'']+\.exe'
$newUrl  = 'https://github.com/buildwithutkarsh-cpu/Butane/releases/latest/download/Butane-Setup.exe'
if ($text -notmatch $pattern) {
    Write-Warning "Hardcoded release URL not found. It may already be patched."
} else {
    $text = [regex]::Replace($text, $pattern, $newUrl)
    Write-Host "Download URL now: $newUrl"
}

# 2) Optionally update the visible version labels.
if ($Version) {
    $text = $text.Replace('<span>1.0.0<\u002Fspan>', "<span>$Version<\u002Fspan>")
    $text = $text.Replace('Version 1.0.0 for Windows', "Version $Version for Windows")
    $text = $text.Replace('DOWNLOAD BUTANE 1.0.0', "DOWNLOAD BUTANE $Version")
    Write-Host "Version labels set to $Version (only replaces labels still reading 1.0.0)."
}

if ($text -ne $original) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $text, $utf8)
    Write-Host "Saved $Path"
} else {
    Write-Host "No changes made."
}
