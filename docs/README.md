# DFMP documentation

Start here, then follow the link that matches what you want to do.

## Play or host

- [Getting started](getting-started.md) — Unity version, Daggerfall game files, building, running a dedicated server, connecting a client
- [Server configuration](server-configuration.md) — `dfmp-server.json` and command-line flags
- [Launcher](../launcher/README.md) — Tauri launcher layout, session handoff, and how to build it

## Develop

- [Contributing](contributing.md) — how to work on DFMP and open pull requests
- [Architecture](architecture.md) — three-layer fork model
- [Development](development.md) — Editor Development Session and developer commands
- [Hook registry](hooks.md) — every Layer 1 edit to upstream DFU
- [Upstream sync](upstream-sync.md) — merging `Interkarma/daggerfall-unity` (never rebase `master`)
- [Roadmap](roadmap.md) — milestone scope and deferred work

## Design notes and smoke checklists

These are contributor internals, not a player guide.

- [Developer commands](developer-commands.md)
- [Scripting architecture](scripting-architecture.md) (Phase 2 design; not a shipped API)
- [Dungeon geometry spike](dungeon-geometry-spike.md)
- [Quest enemy networking spike](quest-enemy-networking-spike.md)
- [Quest action compatibility](quest-action-compatibility.md)
- [Questing smoke matrix](questing-smoke-matrix.md)
- [M8 dungeon smoke test](m8-dungeon-smoke-test.md)
