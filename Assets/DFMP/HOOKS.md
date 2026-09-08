# Daggerfall Unity Multiplayer - Core Hook Registry

This document tracks all modifications made to upstream DFU files (Layer 1).

## Rules for Core Hooks

1. **Additive only**: Never alter vanilla behavior when multiplayer is inactive or no listener is bound.
2. **Ultra-lightweight**: Target $\le 5$ lines per hook site.
3. **No networking dependencies**: NEVER `using Mirror`, `using FishNet`, etc. in `Assets/Scripts/**`.
4. **Isolated commits**: Each hook or group of related hooks must be a clean commit on branch `coop/hooks`.

---

## Active Hooks

| ID | File | Method / Location | Purpose | Added Date |
|---|---|---|---|---|
| *(None yet - Milestone 0 aims for zero core hooks)* | | | | |

---

## Hook Interface Definitions

Static hook entry points live under `Assets/DFMP/Hooks/` (e.g. `DaggerfallHooks.cs`).
