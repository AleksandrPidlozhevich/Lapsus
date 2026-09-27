<#
.SYNOPSIS
    Builds the Microsoft Store package (.msixbundle) for Lapsus.

.DESCRIPTION
    Publishes Lapsus for win-x64 and win-arm64, lays each publish out as an MSIX package next to
    Lapsus/Packaging/Store/AppxManifest.xml and its tile images, and bundles the two into
    build/store/Lapsus_<version>.msixbundle — the one file to upload in Partner Center → Packages.

    The Store build is the same binaries as the Velopack one (scripts/package-windows.ps1). What differs
    is decided at run time, from whether the process has a package identity (Lapsus/Startup/PackageIdentity.cs):
    no self-update (the Store updates it) and autostart through the manifest's startup task instead of
    the HKCU Run key.

    Nothing is signed: Partner Center re-signs every package it accepts. An unsigned .msix cannot be
    installed by double-click, which is what -Register is for.

    makeappx.exe and makepri.exe come from the Microsoft.Windows.SDK.BuildTools NuGet package,
    downloaded once into build/tools — the Windows SDK itself does not need to be installed.

.PARAMETER Version
    Overrides the version. Defaults to the repo-root VERSION file. The package version is that plus
    ".0": the Store requires four numbers with the last one zero.

.PARAMETER Register
    Local test instead of a bundle: builds this machine's architecture only and registers the unpacked
    layout with Add-AppxPackage -Register, so Lapsus shows up in the Start menu running exactly as the
    Store copy would (package identity, virtualized AppData, startup task). Needs Developer Mode.
    It registers under the real Store identity, so remove it before installing Lapsus from the Store.

.EXAMPLE
    pwsh scripts/package-store.ps1

.EXAMPLE
    pwsh scripts/package-store.ps1 -Register
#>

[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Register
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appName = 'Lapsus'
$buildToolsVersion = '10.0.28000.2705'

if (-not $Version) {
    $Version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "The Store needs a plain major.minor.patch version, got '$Version'."
}
$packageVersion = "$Version.0"

$storeDir = Join-Path $root 'Lapsus\Packaging\Store'
$buildDir = Join-Path $root 'build\store'
$template = Get-Content (Join-Path $storeDir 'AppxManifest.xml') -Raw

# ---- makeappx / makepri -------------------------------------------------------------------------
$toolsDir = Join-Path $root "build\tools\Microsoft.Windows.SDK.BuildTools.$buildToolsVersion"
if (-not (Test-Path $toolsDir)) {
    Write-Host "==> Downloading Microsoft.Windows.SDK.BuildTools $buildToolsVersion"
    $zip = Join-Path ([System.IO.Path]::GetTempPath()) "sdk-buildtools-$buildToolsVersion.zip"
    Invoke-WebRequest "https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools/$buildToolsVersion" `
        -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $toolsDir -Force
    Remove-Item $zip
}
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$sdkBin = Get-ChildItem (Join-Path $toolsDir 'bin') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$makeappx = Join-Path $sdkBin.FullName "$hostArch\makeappx.exe"
$makepri = Join-Path $sdkBin.FullName "$hostArch\makepri.exe"

function Invoke-Tool([string]$exe, [string[]]$arguments) {
    & $exe @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Error "$(Split-Path $exe -Leaf) failed ($LASTEXITCODE)" }
}

# ---- one package per architecture ---------------------------------------------------------------
$architectures = if ($Register) { @($hostArch) } else { @('x64', 'arm64') }

if (Test-Path $buildDir) {
    Remove-Item $buildDir -Recurse -Force
}
$msixDir = Join-Path $buildDir 'msix'
New-Item -ItemType Directory -Force $msixDir | Out-Null

# The tile images exist only in scale/targetsize variants, so the package needs a resources.pri to map
# Assets\Square44x44Logo.png onto them. It is built from a folder holding only the images: pointed at the
# whole layout, makepri would index every DLL, and read the satellite-assembly folders (ru\, de\...) as
# language qualifiers.
$priSource = Join-Path $buildDir 'pri'
New-Item -ItemType Directory -Force $priSource | Out-Null
Copy-Item (Join-Path $storeDir 'Assets') $priSource -Recurse
$priConfig = Join-Path $buildDir 'priconfig.xml'
Invoke-Tool $makepri @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0.0', '/o')

foreach ($arch in $architectures) {
    $rid = "win-$arch"
    $layout = Join-Path $buildDir "layout-$arch"

    Write-Host "==> Publishing ($rid, v$Version)"
    # PublishSingleFile stays off: the ONNX Runtime GenAI natives have to sit on disk next to the exe.
    dotnet publish (Join-Path $root 'Lapsus\Lapsus.csproj') -c Release -r $rid --self-contained `
        -p:PublishSingleFile=false -o $layout
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed ($LASTEXITCODE)" }

    Write-Host "==> Layout ($arch)"
    Copy-Item (Join-Path $storeDir 'Assets') $layout -Recurse
    $manifest = $template -replace '\{\{Version\}\}', $packageVersion -replace '\{\{Architecture\}\}', $arch
    $manifestPath = Join-Path $layout 'AppxManifest.xml'
    [System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

    Invoke-Tool $makepri @('new', '/pr', $priSource, '/cf', $priConfig, '/mn', $manifestPath,
        '/of', (Join-Path $layout 'resources.pri'), '/o')

    if (-not $Register) {
        Invoke-Tool $makeappx @('pack', '/d', $layout, '/p', (Join-Path $msixDir "${appName}_${packageVersion}_$arch.msix"), '/o')
    }
}

# ---- local registration -------------------------------------------------------------------------
if ($Register) {
    $unlock = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
        -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
    if (-not $unlock -or $unlock.AllowDevelopmentWithoutDevLicense -ne 1) {
        Write-Error 'Registering an unpacked package needs Developer Mode: Settings -> System -> For developers.'
    }

    # Appx does not load in PowerShell 7 on its own; the Windows PowerShell session it proxies to does.
    if ($PSVersionTable.PSEdition -eq 'Core') {
        Import-Module Appx -UseWindowsPowerShell -WarningAction SilentlyContinue
    }

    $name = ([xml]$template).Package.Identity.Name
    # A copy registered by an earlier run would block this one. Only the registration goes: its
    # settings, dictionaries and models (the package's LocalCache) are kept for the next run.
    Get-AppxPackage -Name $name | Remove-AppxPackage -PreserveApplicationData

    Write-Host "==> Registering $name ($hostArch)"
    Add-AppxPackage -Register (Join-Path $buildDir "layout-$hostArch\AppxManifest.xml")
    Write-Host '==> Done. Quit any running Lapsus first, then start it from the Start menu.'
}
else {
    Write-Host '==> Bundle'
    $bundle = Join-Path $buildDir "${appName}_$packageVersion.msixbundle"
    Invoke-Tool $makeappx @('bundle', '/d', $msixDir, '/p', $bundle, '/bv', $packageVersion, '/o')
    Write-Host "==> Done: $bundle"
    Write-Host '    Upload it in Partner Center -> your submission -> Packages.'
}

# Same reason as in package-windows.ps1: the Release publish leaves obj/project.assets.json without the
# Debug-only DiagnosticsSupport reference, and the IDE then flags WithDeveloperTools() until a restore.
Write-Host '==> Restoring Debug assets (leaves the IDE resolving debug-only APIs)'
dotnet restore (Join-Path $root 'Lapsus\Lapsus.csproj') -p:Configuration=Debug --verbosity quiet
