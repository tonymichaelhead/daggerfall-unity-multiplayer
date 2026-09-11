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
| M6-REST-001 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallRestWindow.cs` | `DoRestForAWhile()`, `DoRestUntilHealed()`, and `LoiterButton_OnMouseClick()` before vanilla rest/loiter state changes | Allow DFMP to consume multiplayer rest and loiter actions before client-side world-time and quest ticks can advance. | 2026-09-08 |
| M6-DOOR-001 | `Assets/Scripts/Game/PlayerEnterExit.cs` | `TransitionInterior()` before vanilla building-interior mutations | Allow DFMP to consume multiplayer exterior-to-building transitions and wait for a server-issued door assignment before running vanilla entry. | 2026-09-08 |
| M6-DOOR-002 | `Assets/Scripts/Game/PlayerEnterExit.cs` | `TransitionExterior()` before vanilla building-exterior mutations | Allow DFMP to consume multiplayer building-to-exterior transitions and wait for a server-issued door assignment before running vanilla exit. | 2026-09-08 |
| M6-DUNGEON-001 | `Assets/Scripts/Game/PlayerEnterExit.cs` | `TransitionDungeonInterior()` before vanilla dungeon-entry mutations | Allow DFMP to consume multiplayer dungeon entry and wait for a server-issued dungeon transition assignment before running vanilla entry. | 2026-09-08 |
| M6-DUNGEON-002 | `Assets/Scripts/Game/PlayerEnterExit.cs` | `TransitionDungeonExterior()` before vanilla dungeon-exit mutations | Allow DFMP to consume multiplayer dungeon exit and wait for a server-issued dungeon transition assignment before running vanilla exit. | 2026-09-08 |
| M6-TIME-TRAIN-001 | `Assets/Scripts/Game/Questing/Actions/TrainPc.cs` | `QuestCompleteMessage_OnClose()` before the three-hour training advance | Preserve quest training while preventing the client from advancing shared server time. | 2026-09-10 |
| M6-TIME-TRAIN-002 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallGuildServiceTraining.cs` | `TrainSkill()` before the three-hour training advance | Preserve paid guild training while preventing the client from advancing shared server time. | 2026-09-10 |
| M6-TIME-PRISON-001 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallCourtWindow.cs` | `UpdatePrisonScreen()` before sentence-time advance | Preserve prison completion while preventing the client from advancing shared server time. | 2026-09-10 |
| M6-TIME-PRISON-002 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallCourtWindow.cs` | `ReleaseFromPrison()` before release-time advance | Preserve prison release while preventing the client from advancing shared server time. | 2026-09-10 |
| M6-TIME-CURE-001 | `Assets/Scripts/Game/MagicAndEffects/Effects/Special/VampirismEffect.cs` | `CureVampirism()` before one-minute cleanup advance | Preserve vampirism cure while preventing the client from advancing shared server time. | 2026-09-10 |
| M6-TIME-CURE-002 | `Assets/Scripts/Game/MagicAndEffects/Effects/Special/LycanthropyEffect.cs` | `CureLycanthropy()` before one-minute cleanup advance | Preserve lycanthropy cure while preventing the client from advancing shared server time. | 2026-09-10 |
| DFMP-STARTUP-001 | `Assets/Scripts/Game/Utility/SceneControl.cs` | `Start()` before the startup-scene vs game-scene branch | Allow DFMP to keep the launcher menu reachable so the single-player game scene is never auto-loaded. | 2026-09-09 |
| DFMP-STARTUP-004 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallUnitySetupGameWizard.cs` | `Setup()` startup-stage selection | When DFMP forces the startup menu and the game-data path is valid, open the options/Join Server page instead of the first-time game-folder page. | 2026-09-09 |
| DFMP-STARTUP-002 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallUnitySetupGameWizard.cs` | End of `ShowOptionsPanel()` | Allow DFMP to relabel and resize the launcher's confirm button ("Play" -> "Join Server"). | 2026-09-09 |
| DFMP-STARTUP-003 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallUnitySetupGameWizard.cs` | `ShowNextStage()`, `SetupStages.LaunchGame` case | Allow DFMP to consume the launch action and open the server list instead of loading the single-player game scene. | 2026-09-09 |

---

## M6 Time-Advance Audit

The upstream rest window has no separate wait-until-dawn action. Its only client-side time-advance entry points are timed rest, rest-until-healed, and loiter, all covered by `M6-REST-001`. Server-mediated rest and healing remains a separate M6.10 implementation task.

Additional `RaiseTime()` paths were audited in upstream DFU. Fast travel is already covered by `M6-FAST-TRAVEL-001`, and the vanilla rest tick cannot start after `M6-REST-001` consumes the rest action. Quest training, guild training, prison/court time, and one-minute vampirism or lycanthropy cure cleanup advances are now blocked in multiplayer while preserving their surrounding effects. The two-week vampirism transformation transition remains separate because it also relocates the player and requires a server-owned transition design.

## Hook Interface Definitions

Static hook entry points live under `Assets/DFMP/Hooks/` (e.g. `DaggerfallHooks.cs`).
