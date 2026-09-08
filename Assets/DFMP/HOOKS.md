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
| M6-FAST-TRAVEL-001 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallTravelPopUp.cs` | `performFastTravel()` before vanilla travel mutations | Allow DFMP to consume multiplayer fast travel before client-side gold, world, and time changes, then request a server-issued transition assignment. | 2026-09-08 |
| M6-DOOR-001 | `Assets/Scripts/Game/PlayerEnterExit.cs` | `TransitionInterior()` before vanilla building-interior mutations | Allow DFMP to consume multiplayer exterior-to-building transitions and wait for a server-issued door assignment before running vanilla entry. | 2026-09-08 |
| M6-DOOR-002 | `Assets/Scripts/Game/PlayerEnterExit.cs` | `TransitionExterior()` before vanilla building-exterior mutations | Allow DFMP to consume multiplayer building-to-exterior transitions and wait for a server-issued door assignment before running vanilla exit. | 2026-09-08 |

---

## Hook Interface Definitions

Static hook entry points live under `Assets/DFMP/Hooks/` (e.g. `DaggerfallHooks.cs`).
