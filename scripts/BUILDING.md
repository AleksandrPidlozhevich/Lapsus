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

## Issuing license keys

Keys and activation tokens are ECDSA P-256 signatures, verified offline against the public key
compiled into the app. **Signing happens only on the server** — three Supabase Edge Functions, whose
source lives in the Supabase dashboard and not in this repository:

| Function | Called by | Guard | Issues |
|---|---|---|---|
| `lapsus-issue` | the [trial form](https://getlapsus.com/en/trial/) | none, by design | pro, 1 seat, 30 days — hard-coded, request fields ignored |
| `lapsus-admin-issue` | you, from a terminal | `x-lapsus-admin` header | any edition, seat count and expiry, taken from the request |
| `lapsus-activate` | the app | none needed | a 35-day activation token, after checking the key's signature and claiming a seat |

`lapsus-activate` needs no guard because the request proves itself: it carries a key that only the
private half could have signed. `lapsus-admin-issue` can prove nothing of the sort — "issue a license
for Acme" says nothing about who is asking — so it needs a shared secret, `LAPSUS_ADMIN_SECRET`, held
in Supabase secrets and nowhere else. Without it that endpoint is an open Business-key generator, and
the trial limits become pointless: why take thirty days when the next URL along grants forever?

Issuing a paid key, in Git Bash:

```bash
curl -X POST "https://<project>.supabase.co/functions/v1/lapsus-admin-issue" -H "content-type: application/json" -H "x-lapsus-admin: $LAPSUS_ADMIN_SECRET" -d '{"email":"buyer@example.com","name":"Acme","edition":"business","seats":25}'
```

`edition` is `pro` or `business`; `seats` defaults to 1. Business defaults to one year and Pro to no
expiry at all, and either is overridden by `"years": 3` or `"expires": "2030-01-01"`.

### Creating or rotating the signing pair

In **Git Bash** (it ships with OpenSSL; PowerShell does not), somewhere outside the repository:

```bash
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 | openssl pkcs8 -topk8 -nocrypt -outform DER -out priv.der && openssl pkey -in priv.der -inform DER -pubout -outform DER -out pub.der && echo "PUBLIC : $(base64 -w0 pub.der)" && echo "PRIVATE: $(base64 -w0 priv.der)"
```

`-topk8` is not optional. Without it OpenSSL writes a SEC1 key, which starts `MHcCAQEE`, and the app
rejects it. A correct private key starts `MIGH`; the public one starts
`MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE`.

A rotation touches **three** places, and missing the third is the failure that looks like a bug:

1. `LAPSUS_PRIVATE_KEY` in Supabase secrets — signs keys and tokens.
2. `LAPSUS_PUBLIC_KEY` in Supabase secrets — `lapsus-activate` checks pasted keys with it.
3. `LicenseVerifier.EmbeddedPublicKey` in this repository — the app checks everything with it.

Change the first two and not the third, and the app rejects keys the server has just minted. Change
the pair at all after the first sale, and every key already issued stops verifying.

Verify a new public key took:

```bash
dotnet test Lapsus.Core.Tests --filter TheShippedVerifierNeverThrows
```

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
