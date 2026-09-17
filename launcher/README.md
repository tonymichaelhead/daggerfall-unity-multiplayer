# DFMP Launcher

Login and launch shell for the Daggerfall Unity Multiplayer client. Built with
[Tauri](https://tauri.app/), so it uses the operating system's existing webview instead of bundling
a browser and stays around five megabytes.

Scope is deliberately small (roadmap milestone M8.5):

- Sign in with a DFMP username and password.
- Locate the player's Daggerfall game files and write `MyDaggerfallPath` into the client settings.
- Resolve the bundled DFMP client automatically and launch it with a session handoff.

The server list stays in the client, where the advanced options panel lives. Client version
management, updating, and mod provisioning are R4. Discord login is R0.

## Bundled install layout

Players run the launcher from a zip that already contains the portable DFMP client. The default
layout is:

```
DFMP/
  DFMP Launcher.exe
  dfmp-launcher.json          (optional override; ships as a template)
  client/
    Daggerfall Unity.exe
    Daggerfall Unity_Data/
    …
```

The launcher never asks the player for the client location. It resolves the executable in this
order:

1. `DFMP_CLIENT_PATH` environment variable (absolute, or relative to the launcher directory).
2. `clientPath` in an optional `dfmp-launcher.json` beside the launcher executable. Relative paths
   resolve against the launcher directory, so the file stays portable with the zip.
3. The default `client/` subfolder beside the launcher (`Daggerfall Unity.exe` on Windows,
   `Daggerfall Unity.x86_64` on Linux).
4. Debug builds only: the Unity `Build/` output at `dfu-mp-source/Build/`, for `npm run tauri
   dev` without copying files into a fake install tree.

A missing or invalid `dfmp-launcher.json` is reported as an error rather than silently ignored, so
a typo in a hand-edited override is visible. Account credentials and the Daggerfall path continue
to live in `%APPDATA%\dfmp-launcher\launcher.json` (and the OS credential store); the exe-adjacent
file is only for install-relative paths.

Copy `dfmp-launcher.json` from this folder next to the built launcher when packaging a release if
you want the override documented in the zip. Leaving it out is fine — the default `client/` layout
applies.

## Credential handling

The password never leaves this machine. The launcher derives `PBKDF2-HMAC-SHA256(password,
salt = "dfmp-credential-v1:<username>", 600000 iterations)` and only that derived value is stored or
sent. It is kept in the operating system credential store — Windows Credential Manager, macOS
Keychain, or Secret Service on Linux — never in a plaintext file.

The derived value is handed to the client through a one-shot session file with owner-only
permissions, which the client deletes on read. It is not passed on the command line, because any
local process can read another process's arguments.

`credential.rs` must stay in lockstep with `DFMPCredential` in
`Assets/DFMP/Runtime/Auth/DFMPCredential.cs`. Both carry the same RFC known-answer test.

**Known beta limitation:** the KCP transport is unencrypted, so the derived value crosses the
network in the clear and can be replayed against that same server by anyone able to observe the
connection. This is acceptable for an invite-only tester group on a server you control. R0 replaces
it with short-lived, audience-bound, single-use tokens, and this scheme must not reach public
release.

## Prerequisites

- [Rust](https://rustup.rs/) — `winget install Rustlang.Rustup` on Windows, or
  `curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh` elsewhere.
- Node.js 20 or newer.
- Linux only: `libwebkit2gtk-4.1-dev`, `librsvg2-dev`, `libsoup-3.0-dev`, `build-essential`,
  and `libssl-dev`.

## Development

```
cd launcher
npm install
npm run tauri dev
```

Debug builds fall back to `../Build/Daggerfall Unity.exe` relative to the Unity project root when
no bundled `client/` folder is present. Point `DFMP_CLIENT_PATH` at a different executable if
needed.

## Build

```
cd launcher
npm install
npm run tauri build
```

Artifacts land in `src-tauri/target/release/bundle/`.

Builds are unsigned. Windows shows a SmartScreen warning on first run, and macOS requires
right-click then Open, or `xattr -d com.apple.quarantine "DFMP Launcher.app"`. Code signing
certificates and Apple notarization are paid and out of scope for the private beta.

## Rust tests

```
cd launcher/src-tauri
cargo test
```
