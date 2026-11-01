param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$AppVersion = "1.0.0"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Butane.csproj"
$publishDir = Join-Path $PSScriptRoot "tmp\installer-publish\$RuntimeIdentifier"
$outputDir = Join-Path $PSScriptRoot "dist"
$script = Join-Path $PSScriptRoot "Butane.iss"

$compiler = (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue).Source
if (-not $compiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )
    $compiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $compiler) {
    throw "Inno Setup 6 is required. Install it from https://jrsoftware.org/isdl.php, then run this script again."
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& dotnet publish $project `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDir `
    -p:UseAppHost=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

& $compiler "/DPublishDir=$publishDir" "/DOutputDir=$outputDir" "/DAppVersion=$AppVersion" $script
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Installer created: $(Join-Path $outputDir "Butane-Setup-$AppVersion.exe")"
