# Development

## Editor: Development Session

In the Unity Editor, open **DFMP → Development Session**. Use it for local client/server loops without assembling a launcher zip.

Direct client connect from a built player:

```text
"Daggerfall Unity.exe" -client -address 127.0.0.1 -port 7777
```

`-account` / `-identity` can supply an account id for `open` or local testing.

## Developer commands

Server-gated console commands for live testing (vampirism infection, time advance, godmode, damage intents). They are **off** unless `Developer.CommandsEnabled` is true in `dfmp-server.json`. Full list: [developer-commands.md](developer-commands.md).

Never enable these on a public server.

## Launcher development

Build and debug steps live in [launcher/README.md](../launcher/README.md) (Node 20+, Rust, Tauri). Debug builds can fall back to Unity `Build/` output.

## Tests and smoke

EditMode fixtures live under `Assets/DFMP/Tests/Editor/`. Maintainers run them in the already-open Unity Editor. Headless or two-client smokes are listed in [questing-smoke-matrix.md](questing-smoke-matrix.md) and [m8-dungeon-smoke-test.md](m8-dungeon-smoke-test.md).
