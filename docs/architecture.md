# Architecture

DFMP is structured so Daggerfall Unity can still be merged from upstream without turning the fork into an unmaintainable overlay.

## Layers

1. **Layer 1 — upstream DFU** (`Assets/Scripts/**`, `Assets/Scenes/**`, and other Interkarma-owned trees)

   Tracked against `https://github.com/Interkarma/daggerfall-unity.git` (`upstream`). Never put multiplayer logic in these files. The Layer 1 footprint is:

   ```sh
   git diff --stat upstream/master..master -- Assets/Scripts
   ```

2. **Layer 2 — hooks** (`Assets/DFMP/Hooks/**`)

   Additive, lightweight calls or static events (no `using Mirror`). Document every core hook in [hooks.md](hooks.md).

3. **Layer 3 — multiplayer** (`Assets/DFMP/Runtime/**`, `Assets/DFMP/Editor/**`)

   Networking, dedicated-server bootstrap, authority, RPCs, persistence. Isolated with assembly definitions (`DFMP.Runtime.asmdef`, and related).

## Authority

World state (time, weather, loot, doors, dungeon enemies, combat resolution) belongs on the server, not on player prefabs. Dedicated-server code must not assume a local player camera, `AudioListener`, or `GameManager.Instance.PlayerObject`.

## Tooling and line endings

- Unity **2019.4 LTS** (this repo pins **2019.4.41f2**).
- The repository is LF-only via `.gitattributes` (`* text=auto eol=lf`). Git's clean filter strips CR on `git add`.
- `.editorconfig` sets `end_of_line = lf`, `charset = utf-8`, `insert_final_newline = true`. Unity `.meta` YAML fails to parse without a final newline.
- Unity writes trailing spaces into some `.meta` keys; those are real content. `.gitattributes` marks `*.meta` as `whitespace=-trailing-space`.
- Run `git config merge.renormalize true` in your clone so upstream merges do not conflict on line endings alone.
- If an upstream merge ever brings in CRLF, `git add --renormalize .` converts it. Do not hand-normalize.
- See [upstream-sync.md](upstream-sync.md) for merge policy.

## Related docs

- [Contributing](contributing.md)
- [Roadmap](roadmap.md)
- [Hook registry](hooks.md)
