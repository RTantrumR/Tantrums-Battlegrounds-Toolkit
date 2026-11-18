<#
.SYNOPSIS
  Build Release and package the Setup wizard + the drop-in zip for Tantrum's Battlegrounds Toolkit.

.DESCRIPTION
  Produces dist\TantrumsBattlegroundsToolkit-Setup-<Version>.exe (the Inno Setup wizard from
  installer\HsbgCardLookup.iss, built by installer\build.ps1 — the download the plugin's update
  notice points at) and dist\HsbgCardLookup-v<Version>.zip. The zip root contains:
    HsbgCardLookup\   (the DLL + bundled WebP-decoder deps + data\ — the plugin itself)
    install.bat       (one-click: closes HDT, copies the folder into the Plugins dir)
    README.txt        (EN install/usage)
    UA_Readme.txt     (UA install/usage)
  The zip stays attached for one more release so the Updates page's old text ("run install.bat
  from the zip") stays true for users still on it; users extract and double-click install.bat
  (or drag the HsbgCardLookup folder into %APPDATA%\HearthstoneDeckTracker\Plugins\ manually).
  Release excludes the dev-only GameStateProbe (it's #if DEBUG).
#>
# -SignPfx / -SignPasswordFile: sign our own binaries (the zip's DLL here, the Setup's set in
# installer\build.ps1) with that certificate. A release is built with them; without, it is unsigned.
param([string]$Version, [string]$SignPfx, [string]$SignPasswordFile)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Default the version to whatever Plugin.cs reports (the `Version` property the GitHub updater compares
# against the release tag), so the zip name can never drift from the shipped build. Pass -Version only
# to override.
if (-not $Version) {
    $pluginCs = Join-Path $root "HsbgCardLookup\Plugin.cs"
    $m = [regex]::Match((Get-Content -Raw $pluginCs), 'Version\s*=>\s*new\s+Version\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)')
    if (-not $m.Success) { throw "Could not read plugin Version from $pluginCs" }
    $Version = "{0}.{1}.{2}" -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value
    Write-Host "Version (from Plugin.cs): $Version" -ForegroundColor Cyan
}
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at $msbuild" }

Write-Host "Building Release..." -ForegroundColor Cyan
& $msbuild "$root\HsbgCardLookup.sln" /t:Rebuild /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
# The standalone exe is not in the solution, and installer\build.ps1 is called with -SkipBuild below:
# build it here, or the Setup ships whatever standalone build was left in its bin\Release.
& $msbuild "$root\Standalone\Standalone.csproj" /t:Rebuild /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Standalone build failed (exit $LASTEXITCODE)." }

$bin = "$root\HsbgCardLookup\bin\Release"
$dll = "$bin\HsbgCardLookup.dll"
if (-not (Test-Path $dll)) { throw "DLL not found at $dll" }
if (-not (Test-Path "$bin\data\cards.json")) { throw "data\cards.json missing from build output" }

# Package layout:
#   dist\pkg\                  <- zipped contents (the extraction root the user sees)
#   dist\pkg\HsbgCardLookup\   <- runtime files only (what lands in the Plugins folder)
$dist  = "$root\dist"
$pkg   = "$dist\pkg"
$stage = "$pkg\HsbgCardLookup"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# Our DLL + bundled runtime deps (ImageSharp + System.* closure). HDT's own assemblies are
# Private=False so they aren't in bin and aren't shipped. PDB is omitted.
Copy-Item "$bin\*.dll" $stage -Force
Copy-Item "$bin\data" $stage -Recurse -Force

# install.bat lives at the package root (beside the HsbgCardLookup folder it copies).
Copy-Item "$root\packaging\install.bat" $pkg -Force

# README templates are committed UTF-8 files under packaging\ — copied (NOT embedded as here-strings)
# so non-ASCII (Ukrainian) survives: PowerShell 5.1 parses .ps1 as the ANSI codepage, which mangled
# Cyrillic literals. We read each as UTF-8 and re-emit it as UTF-8 *with BOM* so Notepad on any
# Windows shows Cyrillic correctly. READMEs sit at the package root (visible right after extraction).
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
function Copy-Readme($name) {
    $src = Join-Path $root "packaging\$name"
    if (-not (Test-Path $src)) { throw "Readme template missing: $src" }
    $text = Get-Content -Raw -Encoding UTF8 $src
    [System.IO.File]::WriteAllText((Join-Path $pkg $name), $text, $utf8Bom)
}
Copy-Readme "README.txt"
Copy-Readme "UA_Readme.txt"

if ($SignPfx) {
    if (-not (Test-Path $SignPfx)) { throw "Certificate not found: $SignPfx" }
    if (-not $SignPasswordFile -or -not (Test-Path $SignPasswordFile)) { throw "-SignPasswordFile is required with -SignPfx" }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { throw "signtool.exe not found (Windows SDK)" }
    & $signtool sign /q /fd sha256 /f $SignPfx /p (Get-Content -Raw $SignPasswordFile).Trim() "$stage\HsbgCardLookup.dll"
    if ($LASTEXITCODE -ne 0) { throw "signtool failed on the zip's DLL (exit $LASTEXITCODE)" }
}

$zip = "$dist\HsbgCardLookup-v$Version.zip"
Compress-Archive -Path "$pkg\*" -DestinationPath $zip -Force

$size = "{0:N1} MB" -f ((Get-Item $zip).Length / 1MB)
Write-Host "Packaged: $zip ($size)" -ForegroundColor Green
Write-Host "Zip contents:" -ForegroundColor Cyan
Get-ChildItem $pkg -Recurse -File | ForEach-Object { "  " + $_.FullName.Substring($pkg.Length + 1) }

# The Setup wizard, from the same Release build (installer\build.ps1 stages bin\Release itself).
$signing = @{}
if ($SignPfx) { $signing = @{ SignPfx = $SignPfx; SignPasswordFile = $SignPasswordFile } }
$setup = & "$root\installer\build.ps1" -Version $Version -SkipBuild @signing
Copy-Item $setup.FullName $dist -Force
Write-Host "Packaged: $dist\$($setup.Name)" -ForegroundColor Green
