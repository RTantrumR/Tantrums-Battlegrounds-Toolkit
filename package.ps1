<#
.SYNOPSIS
  Build Release and package the Setup wizard for Tantrum's Battlegrounds Toolkit.

.DESCRIPTION
  Produces dist\TantrumsBattlegroundsToolkit-Setup-<Version>.exe: the Inno Setup wizard from
  installer\HsbgCardLookup.iss, built by installer\build.ps1 (which also does the Release build).
  It is the download the toolkit's update notice points at.

  -Version defaults to AssemblyVersion in HsbgCardLookup\Properties\AssemblyInfo.cs.
  -SignPfx / -SignPasswordFile sign our own binaries and the Setup with that certificate. A release
  is built with them; without, it is unsigned.
#>
param([string]$Version, [string]$SignPfx, [string]$SignPasswordFile)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$buildArgs = @{}
if ($Version) { $buildArgs.Version = $Version }
if ($SignPfx) { $buildArgs.SignPfx = $SignPfx; $buildArgs.SignPasswordFile = $SignPasswordFile }
# build.ps1 returns the Setup's file last; everything before it (MSBuild's and Inno Setup's output)
# goes to the screen as it comes, so a failed build shows its compiler errors.
$setup = $null
& "$root\installer\build.ps1" @buildArgs | ForEach-Object {
    if ($_ -is [System.IO.FileInfo]) { $setup = $_ } else { Write-Host $_ }
}
if (-not $setup) { throw "installer\build.ps1 returned no Setup file" }

$dist = "$root\dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item $setup.FullName $dist -Force
Write-Host "Packaged: $dist\$($setup.Name)" -ForegroundColor Green
