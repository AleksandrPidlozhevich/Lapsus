# Building Lapsus installers

This explains how to build the Lapsus installer yourself, on macOS or on Windows. You don't
need to be a developer — just follow the steps for your platform.

## What you need first (both platforms)

1. **.NET SDK 10** — download and install from https://dotnet.microsoft.com/download
   (Lapsus is built against .NET 10, so an older SDK won't do; after installing, check that
   `dotnet --version` prints a `10.` number).
2. **The `vpk` tool** (Velopack's packaging CLI) — open a terminal and run:
   ```
   dotnet tool install -g vpk
   ```
   You only need to do this once per machine.

## macOS

1. Open **Terminal**.
2. Go to the project folder, for example:
   ```
   cd ~/RiderProjects/Lapsus
   ```
3. Run the build script:
   ```
   ./scripts/package-macos.sh
   ```
   This builds for Apple Silicon (M1/M2/M3/M4) Macs by default. If you need an Intel Mac
   build instead, run:
   ```
   ./scripts/package-macos.sh osx-x64
   ```

The version number is read automatically from the `VERSION` file in the project root — you
don't need to type it. (To release a new version, edit that file first.)

When it finishes, you'll find the installer and related files in:
```
build/macos/Releases/
```
The file you hand to someone to install the app is `Lapsus-osx.dmg` — the familiar window
where you drag Lapsus into Applications. `Lapsus-osx-Setup.pkg` is the same app as a
step-through installer, and `Lapsus-osx-Portable.zip` a copy-and-run version.

**Note:** this build is not notarized by Apple, so macOS Gatekeeper may warn that the app
is from an "unidentified developer" the first time it's opened on another Mac. That's
expected for a self-built copy — right-click the app and choose "Open" to bypass it once.

## Windows

1. Open **PowerShell**. Either version works:
   - **PowerShell 7** (`pwsh`) — the modern one, if you have it installed.
   - **Windows PowerShell 5** (`powershell`) — the one already on every Windows machine,
     listed in the Start menu as "Windows PowerShell". Nothing extra to install.
2. Go to the project folder, for example:
   ```
   cd C:\Users\<you>\RiderProjects\Lapsus
   ```
3. Run the build script.

   On **PowerShell 7**:
   ```
   pwsh scripts/package-windows.ps1
   ```
   On **Windows PowerShell 5** — step into the `scripts` folder and run it by name:
   ```
   cd scripts
   .\package-windows.ps1
   ```
   The leading `.\` is not a typo: PowerShell won't run a script from the current folder
   without it. Which folder you start it from makes no difference — the script works out
   the project folder from its own location — so `.\scripts\package-windows.ps1` straight
   from the project root does exactly the same thing.

   Either way this builds a 64-bit (`win-x64`) installer by default. If you need the ARM64
   build instead (for Windows on ARM devices), add `-Rid win-arm64` at the end:
   ```
   pwsh scripts/package-windows.ps1 -Rid win-arm64   # PowerShell 7
   .\package-windows.ps1 -Rid win-arm64              # Windows PowerShell 5
   ```

The version number is likewise read automatically from the `VERSION` file.

When it finishes, you'll find the installer and related files in:
```
build\windows\Releases\
```
The file you hand to someone to install the app is `Lapsus-win-Setup.exe` (or
`Lapsus-win-Portable.zip` for a copy-and-run version, no installer needed). An `.msi` is
also produced for IT-managed deployments (Group Policy / Intune / SCCM). The names are the
same for the ARM64 build, so keep the two out of one folder if you build both.

**Note:** this build is not code-signed, so Windows SmartScreen may show "Windows
protected your PC" the first time it's run on another machine. Click "More info" then
"Run anyway" to continue — that's expected for a self-built, unsigned copy.

## Publishing a release

Building only produces files; nothing updates until they are published. Create one GitHub
Release tagged `v<version>` — matching the `VERSION` file — and upload **every** file from
`build\windows\Releases\` and `build/macos/Releases/` to it. That release *is* the update
feed `Lapsus/Updates/AppUpdater.cs` reads; there is nothing else to host.

Upload the whole folder, not just the installer. `RELEASES`, `releases.win.json` and the
`.nupkg` files are what the feed is made of — an installer on its own gives an existing user
nothing to find.

**The repository has to be public for this to work.** `AppUpdater` builds its `GithubSource`
with no access token, so it reads the feed anonymously, and a private repository answers 404
to an anonymous request. The app would then fail every check — silently, as far as the user
is concerned, with only a line in `crash.log` to show for it. Embedding a token instead is
not an option: it ships inside the client, where anyone can read it back out.

If the code must stay private, publish the releases to a second, public repository and point
`RepoUrl` at that one.

## Microsoft Store

The Store takes an MSIX package instead of the Velopack installer. `vpk` is not needed for
it, and neither is the Windows SDK — the script downloads the two tools it uses
(`makeappx`, `makepri`) into `build\tools\` the first time.

```
pwsh scripts/package-store.ps1
```
It builds both x64 and ARM64 and bundles them into
`build\store\Lapsus_<version>.0.msixbundle`. Upload that one file in your submission's
*Packages* page. It is unsigned on purpose: the Store signs it.

The package identity in `Lapsus\Packaging\Store\AppxManifest.xml` (`Pidlozhevich.Lapsus`, its
`CN=…` publisher and the publisher display name) is copied from Partner Center → *Product
management* → *Product identity* and must stay exactly as shown there, or the upload is rejected.

To try the Store copy on your own machine first, turn on **Developer Mode** (Settings →
System → For developers), quit Lapsus, and run:
```
pwsh scripts/package-store.ps1 -Register
```
Lapsus then appears in the Start menu running exactly as the Store copy does: the update
button is gone, "Run at startup" shows up in Task Manager → Startup apps, and settings live
under `%LOCALAPPDATA%\Packages\<package>\LocalCache\Roaming\Lapsus` instead of
`%APPDATA%\Lapsus`. It is registered under the real Store identity, so remove it again from
Settings → Apps before installing Lapsus from the Store — the two cannot sit side by side.

A Store release needs a new `VERSION` like any other — the Store rejects a package whose
version is not higher than the one already published.

## Troubleshooting

- **"vpk not found"** — you skipped the `dotnet tool install -g vpk` step above, or opened
  a new terminal window before the tool was picked up. Close and reopen your terminal and
  try again.
- **"cannot be loaded because running scripts is disabled on this system"** — some
  machines block PowerShell scripts by default. Allow them for this one window and run the
  script again:
  ```
  Set-ExecutionPolicy -Scope Process Bypass
  ```
  That lasts until you close the window and changes nothing permanently.
- **Build fails partway through** — make sure the .NET SDK is installed and up to date
  (`dotnet --version`).
