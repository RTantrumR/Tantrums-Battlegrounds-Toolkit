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
$setup = & "$root\installer\build.ps1" @buildArgs

$dist = "$root\dist"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item $setup.FullName $dist -Force
Write-Host "Packaged: $dist\$($setup.Name)" -ForegroundColor Green
