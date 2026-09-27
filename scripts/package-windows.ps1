<#
.SYNOPSIS
    Builds the Windows installer and update feed for Lapsus.

.DESCRIPTION
    Publishes Lapsus and hands it to `vpk pack` (Velopack's CLI), which builds Lapsus-win-Setup.exe,
    Lapsus-win.msi, a portable zip, and the release feed that AppUpdater.cs / GithubSource reads to
    find updates. Upload everything under build/windows/Releases/ to a GitHub Release with matching
    tag "v<version>" — that release IS the update feed, there is nothing else to host. The macOS half
    of the same release comes from scripts/package-macos.sh; both read the same VERSION file.

    Velopack installs per-user into %LocalAppData%\Lapsus (no admin, no UAC prompt): `current\` holds
    the app files and is replaced wholesale on every update, which is why settings, dictionaries and
    models live in %APPDATA%\Lapsus instead. The folder *name* is stable, so the autostart Run key
    written by WindowsStartupRegistration keeps pointing at the right exe after an update.

    The icon: unlike the macOS script, which generates its .icns here from Assets/icon-light.svg,
    Lapsus/Assets/app.ico is committed — it is a compile input for Lapsus.exe, not just a packaging
    input. Regenerate it from the SVG (16/32/48/64/128/256, edge-to-edge, PNG-compressed entries) if
    the artwork ever changes; Svg.Skia — already an indirect dependency via Svg.Controls.Skia.Avalonia
    — rasterizes it, and an .ico is an ICONDIR plus one ICONDIRENTRY and one PNG payload per size.

.PARAMETER Rid
    Target runtime identifier. win-x64 by default; win-arm64 also has ONNX Runtime GenAI natives.

.PARAMETER Version
    Overrides the version. Defaults to the repo-root VERSION file, which Directory.Build.props also
    reads for the .NET assembly version — so this stays in sync with the macOS build without the
    number being duplicated per platform.

    Note for the day VERSION grows a prerelease suffix: an MSI ProductVersion is three numbers with
    hard limits (255.255.65535) and cannot carry one, so `1.1.0-beta.2` needs `--msiVersion` passed
    to vpk as well. Plain `major.minor.patch` needs nothing.

.EXAMPLE
    pwsh scripts/package-windows.ps1

.NOTES
    Requires the .NET SDK and the `vpk` global tool: dotnet tool install -g vpk

    The output is unsigned, so Windows SmartScreen shows "Windows protected your PC" until the
    installer earns reputation. To sign, set LAPSUS_SIGN_PARAMS to the signtool arguments, e.g.
        $env:LAPSUS_SIGN_PARAMS = '/a /fd sha256 /tr http://timestamp.digicert.com /td sha256'
#>

[CmdletBinding()]
param(
    [string]$Rid = 'win-x64',
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appName = 'Lapsus'

if (-not $Version) {
    $Version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
}

$env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Error 'vpk not found. Install it with: dotnet tool install -g vpk'
}

$buildDir = Join-Path $root 'build\windows'
$publishDir = Join-Path $buildDir "publish-$Rid"
$outputDir = Join-Path $buildDir 'Releases'
$icon = Join-Path $root 'Lapsus\Assets\app.ico'

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "==> Publishing ($Rid, v$Version)"
# PublishSingleFile stays off: the ONNX Runtime GenAI DirectML package ships native DLLs that the
# loader has to find on disk, and Velopack packs a folder anyway.
dotnet publish (Join-Path $root 'Lapsus\Lapsus.csproj') -c Release -r $Rid --self-contained `
    -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed ($LASTEXITCODE)" }

Write-Host '==> vpk pack'
# --shortcuts StartMenuRoot: Lapsus lives in the tray, so a desktop icon is clutter for something you
# launch once. Note the tempting `Startup` value is deliberately NOT used — the app has its own "run at
# startup" setting (an HKCU Run value), and a Startup shortcut on top of it would launch a second copy
# and leave that setting's checkbox lying about the real state.
# --mainExe takes the file name with its extension here, unlike the macOS script.
# --yes: re-running the same version locally while testing is the common case, and this only
# overwrites local build output (build/ is gitignored) — never anything already uploaded to GitHub.
$packArgs = @(
    'pack'
    '--packId', $appName
    '--packVersion', $Version
    '--packDir', $publishDir
    '--mainExe', "$appName.exe"
    '--packTitle', $appName
    '--packAuthors', $appName
    '--icon', $icon
    '--outputDir', $outputDir
    '--runtime', $Rid
    '--shortcuts', 'StartMenuRoot'
    # A second installer beside Setup.exe, built with WiX 5, for IT deployment: Group Policy /
    # Intune / SCCM can push an .msi and cannot push an .exe. --instLocation is left at its default
    # ("Either"), so the MSI UI asks for per-user or per-machine and an admin can force the choice
    # with `msiexec /i Lapsus-win.msi /qn ALLUSERS=1` (plus VELOPACK_INSTALLDIR=... to relocate it).
    # Setup.exe stays the download for an ordinary user: it needs no admin, and it is the one whose
    # install location the app can write to, so in-app updates keep working.
    '--msi'
    '--yes'
)

if ($env:LAPSUS_SIGN_PARAMS) {
    Write-Host '    (signing with LAPSUS_SIGN_PARAMS)'
    $packArgs += @('--signParams', $env:LAPSUS_SIGN_PARAMS)
}

vpk @packArgs
if ($LASTEXITCODE -ne 0) { Write-Error "vpk pack failed ($LASTEXITCODE)" }

# Leave the working copy as we found it. AvaloniaUI.DiagnosticsSupport is referenced with an
# IncludeAssets condition on $(Configuration) (see Lapsus.csproj), and NuGet keeps one
# obj/project.assets.json for the whole project rather than one per configuration — so whichever
# configuration restored last decides whether that package contributes a compile reference at all.
# The Release publish above therefore leaves the assets file saying "no assembly", and an IDE reading
# it stops resolving WithDeveloperTools() in the #if DEBUG block of Program.cs. Nothing is broken —
# Release never compiles that line, and any Debug build restores first — but the editor shows a red
# squiggle until something does. One second here is cheaper than that confusion.
Write-Host '==> Restoring Debug assets (leaves the IDE resolving debug-only APIs)'
dotnet restore (Join-Path $root 'Lapsus\Lapsus.csproj') -p:Configuration=Debug --verbosity quiet

Write-Host "==> Done: $outputDir"
Write-Host "    Upload every file in that folder to a GitHub Release tagged v$Version."
