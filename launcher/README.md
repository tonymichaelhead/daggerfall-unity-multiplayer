# DFMP Launcher

Player and server setup is in [docs/getting-started.md](../docs/getting-started.md). This file is the launcher-specific build and install layout.

Launch shell for the Daggerfall Unity Multiplayer client. Built with
[Tauri](https://tauri.app/), so it uses the operating system's existing webview instead of bundling
a browser and stays around five megabytes.

Scope is deliberately small (roadmap milestone M8.5):

- Local profile (`profileId`) and Daggerfall game-file location. There is no DFMP account login.
- Locate the player's Daggerfall game files and write `MyDaggerfallPath` into the client settings.
- Resolve the bundled DFMP client automatically and launch it with a session handoff.

Discord login happens in the DFMP client when you connect to a server that uses `Identity.Mode = discord`.
The server list stays in the client, where the advanced options panel lives. Client version
management, auto-update, and mod provisioning are R4. Username/password as a player-facing flow is deferred.

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
a typo in a hand-edited override is visible. The local profile id and the Daggerfall path live in
`%APPDATA%\dfmp-launcher\launcher.json`; the exe-adjacent file is only for install-relative paths.

Copy `dfmp-launcher.json` from this folder next to the built launcher when packaging a release if
you want the override documented in the zip. Leaving it out is fine — the default `client/` layout
applies.

## Session handoff

Play writes a one-shot session file with the local `profileId` (owner-only permissions). The client
deletes it on read. It is not passed on the command line, because any local process can read another
process's arguments. Discord identity is assigned by the server at connect; the profile id is only
used for `open` mode and future local settings.

`credential.rs` remains so a later `server_local` username/password flow cannot silently drift from
`DFMPCredential` in `Assets/DFMP/Runtime/Auth/DFMPCredential.cs`. Both carry the same RFC
known-answer test. That derivation is unused by the launcher UI today.

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
