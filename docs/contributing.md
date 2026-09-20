# Contributing to DFMP

Thanks for helping. DFMP is a fork of [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity). We keep multiplayer out of upstream files except for tiny additive hooks so future DFU merges stay possible.

## Before you start

1. Read [architecture.md](architecture.md) and [getting-started.md](getting-started.md).
2. Check [roadmap.md](roadmap.md) so the change matches a milestone instead of inventing a parallel system.
3. Prefer Layer 3 (`Assets/DFMP/Runtime/`, `Assets/DFMP/Editor/`) over touching `Assets/Scripts/`.

Do not add Cursor/Copilot instruction files, `.cursor/` trees, or personal `dfmp-server.json` to pull requests. Those are gitignored on purpose.

## Three-layer rule

| Layer | Where | Rule |
| --- | --- | --- |
| 1 | `Assets/Scripts/**`, scenes, other upstream DFU | No networking. No `using Mirror`. Change only with a registered additive hook. |
| 2 | `Assets/DFMP/Hooks/**` | Thin events/interfaces. No gameplay netcode. |
| 3 | `Assets/DFMP/Runtime/**`, tests | Dedicated server, Mirror, authority, persistence, RPCs. |

Every Layer 1 edit must be listed in [hooks.md](hooks.md) with rationale. Target a few lines per site. Vanilla DFU must keep working when no listener is bound.

Do not simplify native DFU gameplay “for now” without maintainers agreeing. Approved placeholders belong in [roadmap.md](roadmap.md) with the deferred native behavior and the milestone that replaces them.

## Tests

- Add or update EditMode tests when the behavior is high-value and does not need a full player run: authority, message validation, lifecycle, coordinates, persistence.
- Skip trivial getter/setter tests.
- Unity Editor is often already open for maintainers; **do not** assume CI ran EditMode tests unless a maintainer says so. Name the fixtures you added in the PR.

## Git and upstream

- Default branch is `master`.
- Merge upstream; **never rebase or force-push `master`**. See [upstream-sync.md](upstream-sync.md).
- Line endings are LF via `.gitattributes`. Run `git config merge.renormalize true` in your clone.

## Pull requests

- Keep the Layer 1 diff small (`git diff --stat upstream/master..HEAD -- Assets/Scripts`).
- Describe player-visible behavior and any config (`dfmp-server.json`) implications.
- If you changed defaults, list the Editor fixtures maintainers should run.
