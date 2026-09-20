# Daggerfall Unity Multiplayer (DFMP)

DFMP is a dedicated-server multiplayer fork of [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity), the open-source recreation of *The Elder Scrolls II: Daggerfall*. You can play on a shared world with friends, or host your own server using the same Unity build in headless mode.

This repository is a fork of [Interkarma/daggerfall-unity](https://github.com/Interkarma/daggerfall-unity). Upstream DFU remains single-player; all multiplayer code lives in `Assets/DFMP/` plus a small set of additive hooks in upstream files.

## Features

- Headless dedicated server (Mirror / KCP)
- Shared world time, chat, and other player presence
- Server-authoritative vitals, combat, and dungeon enemies
- Character persistence and character select
- Discord, open (LAN/dev), and local-account authentication modes
- A small Tauri launcher that locates Daggerfall game files and starts the client
- Client-owned quests with server-owned quest enemies (see the docs for limits)

DFMP is actively developed. Client and server **must** be the same build. Some single-player DFU systems are not fully networked yet (for example lycanthropy, disease persistence across reconnect, server-side scripting, and a public master server list). See [docs/roadmap.md](docs/roadmap.md).

## Game data (required)

DFMP does **not** include Bethesda's Daggerfall assets. You need a legally obtained copy of DOS Daggerfall (`arena2` / `ARENA2`). A free copy is available on [Steam](https://store.steampowered.com/app/1812390/The_Elder_Scrolls_II_Daggerfall/).

## Documentation

| Doc | What it covers |
| --- | --- |
| [Documentation index](docs/README.md) | Full map of user, operator, and contributor docs |
| [Getting started](docs/getting-started.md) | Unity version, Arena2, building, hosting a server, joining |
| [Server configuration](docs/server-configuration.md) | `dfmp-server.json` fields and CLI overrides |
| [Contributing](docs/contributing.md) | Architecture, hooks, tests, upstream merges |

## License

Upstream Daggerfall Unity is MIT, copyright Daggerfall Workshop. See [LICENSE](LICENSE). DFMP changes in this fork are released under the same MIT terms. Third-party networking code (Mirror, kcp2k, Mono.Cecil) keeps its own license files under `Assets/DFMP/Runtime/ThirdParty/Mirror/`.
