# Getting started

This guide covers building DFMP from source, pointing it at classic Daggerfall data, hosting a dedicated server, and joining as a client. There is no separate server binary: the same standalone player build runs headless when you pass dedicated-server flags.

You need a legally obtained copy of DOS Daggerfall. DFMP does not ship `arena2` files.

## Requirements

- **Unity 2019.4.41f2** (2019.4 LTS; C# 7.3 / .NET Standard 2.0)
- Classic Daggerfall game data (`arena2` or `ARENA2`), for example from [Steam's free Daggerfall release](https://store.steampowered.com/app/1812390/The_Elder_Scrolls_II_Daggerfall/)
- Git
- Windows is the best-tested host OS today. Linux/macOS Unity builds may work but are not the primary development path.

Client and server **must** be the same DFMP build. Protocol mismatches are rejected at connect.

## Clone and open the project

```sh
git clone https://github.com/tonymichaelhead/daggerfall-unity-multiplayer.git
```

Open the cloned folder in Unity 2019.4.41f2 (this repository **is** the Unity project root). Let Unity import. The first import is slow.

Point the Editor at your Arena2 folder the same way you would for Daggerfall Unity (DFU's usual game-files setup). Dedicated-server launches can also pass `-arena2 <path>`.

## Build a standalone player

In Unity: **File → Build Settings → PC, Mac & Linux Standalone**, then Build. Typical Windows output is `Daggerfall Unity.exe` plus `Daggerfall Unity_Data/`.

Windows dedicated-server builds are post-processed to the Console subsystem so logs show in the terminal.

Do not commit `Build/` output. Keep Arena2 and Discord secrets off git.

## Host a dedicated server

1. Copy [`dfmp-server.example.json`](../dfmp-server.example.json) to `dfmp-server.json` next to the process working directory (usually next to the executable when you launch it from that folder).
2. Edit the copy. Start with `"Identity": { "Mode": "open" }` for LAN/dev. Never put Discord client secrets in JSON.
3. Run the **same** standalone build headless:

```text
"Daggerfall Unity.exe" -batchmode -nographics -server -port 7777 -tickrate 30 -maxplayers 16 -heartbeat 5
```

`-batchmode` without `-client` also enters dedicated-server mode. `-server` / `-dedicated` are explicit.

Optional flags:

| Flag | Purpose |
| --- | --- |
| `-arena2 <path>` | Arena2 / ARENA2 directory |
| `-name <display name>` | Overrides `ServerName` when the value is non-empty |
| `-port <n>` | Game port (default 7777). CLI wins over JSON only when the value is not the parser default (7777) |
| `-tickrate`, `-maxplayers` / `-maxconnections`, `-heartbeat` | Same idea: non-default CLI values override JSON |

Open UDP **7777** (game, KCP) and **7778** (LAN discovery) on the host firewall if players are not on the same machine.

Logs go to `Logs/DFMP/server.log` relative to the working directory. Character JSON lives under Unity `Application.persistentDataPath/DFMP/Characters/`.

### Discord-authenticated servers

Each operator registers **their own** Discord application. DFMP does not run a central login service.

Set environment variables (never commit them):

- `DFMP_DISCORD_CLIENT_ID`
- `DFMP_DISCORD_CLIENT_SECRET`
- `DFMP_DISCORD_BOT_TOKEN` (optional; guild role checks)

In `dfmp-server.json` set `Identity.Mode` to `discord` and keep `Identity.DiscordRedirectUri` as a loopback URL with an explicit port (default `http://127.0.0.1:53682/dfmp-auth`). Add that redirect URI in the Discord developer portal.

Players complete Discord login in the **game client** when they connect, not in the launcher.

See [server-configuration.md](server-configuration.md) for whitelist, admin lists, and every JSON field.

## Join as a client

### From a packaged launcher zip

The expected layout is documented in [launcher/README.md](../launcher/README.md):

```text
DFMP/
  DFMP Launcher.exe
  client/Daggerfall Unity.exe
```

The launcher stores the Daggerfall path and starts the client with a one-shot session file. Pick a server in the in-game list (LAN discovery) or use advanced connect options.

### Direct connect (same binary)

```text
"Daggerfall Unity.exe" -client -address 127.0.0.1 -port 7777
```

### Unity Editor

Use **DFMP → Development Session** to drive local client/server workflows without a full zip. Details: [development.md](development.md).

## What is not ready yet

- Server-side Lua/scripting and a full “every knob” config surface (Phase 2)
- Public master server list
- Launcher auto-update and signed installers
- Shipping prebuilt client/server zips from this repo (build from source for now)

If something in single-player DFU is missing or wrong in multiplayer, check [roadmap.md](roadmap.md) before assuming it is a simple bug.
