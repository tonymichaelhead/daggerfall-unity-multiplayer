# Daggerfall Unity Multiplayer - Core Hook Registry

This document tracks all modifications made to upstream DFU files (Layer 1).

## Rules for Core Hooks

1. **Additive only**: Never alter vanilla behavior when multiplayer is inactive or no listener is bound.
2. **Ultra-lightweight**: Target $\le 5$ lines per hook site.
3. **No networking dependencies**: NEVER `using Mirror`, `using FishNet`, etc. in `Assets/Scripts/**`.
4. **Registered and auditable**: Every hook site must appear in the table below. There is no separate hooks branch; the complete Layer 1 patch is derivable from `master` at any time (see Layer 1 Budget).

---

## Layer 1 Budget

The hook layer is measured, not assumed. Run this before and after every upstream sync:

```sh
# Complete Layer 1 footprint.
git diff --stat --ignore-cr-at-eol upstream/master..master -- Assets/Scripts

# Must print nothing.
git grep -nE "using Mirror|NetworkBehaviour|NetworkServer|NetworkClient" -- Assets/Scripts
```

Current baseline as of 2026-09-19: **33 files, 229 insertions, 34 deletions, 0 networking references.**

Growth not explained by a newly registered hook below means multiplayer logic has leaked
into Layer 1. Move it back into `Assets/DFMP/` rather than accepting it.

---

## Active Hooks

| ID | File | Method / Location | Purpose | Added Date |
|---|---|---|---|---|
| M6-FAST-TRAVEL-001 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallTravelPopUp.cs` | `performFastTravel()` before vanilla travel mutations | Allow DFMP to consume multiplayer fast travel before client-side gold, world, and time changes, then request a server-issued transition assignment. | 2026-09-08 |
| M6-REST-001 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallRestWindow.cs` | `DoRestForAWhile()`, `DoRestUntilHealed()`, and `LoiterButton_OnMouseClick()` before vanilla rest/loiter state changes | Allow DFMP to consume rest or loiter before vanilla starts it. Loiter is always consumed in multiplayer. Timed rest and rest-until-healed are consumed only when the replicated rest policy is disabled. | 2026-09-08 |
| M6-REST-002 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallRestWindow.cs` | `TickRest()` immediately before `RaiseTime()` / `QuestMachine.Tick()` | Allow DFMP to skip rest-driven world-time and quest ticks while still counting rest hours, vitals, and interrupts. | 2026-09-18 |
| M6-REST-003 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallRestWindow.cs` | `TickRest()` after vanilla `TickVitals()` on a counted rest hour | Notify DFMP so the server can apply one vanilla rest hour of vitals without advancing the clock. | 2026-09-18 |
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
| QUEST-CLOCK-001 | `Assets/Scripts/Game/Questing/Clock.cs` | `Tick()` immediately before the same-named expiry task is started | Allow DFMP to suppress an explicitly classified client-owned quest failure deadline while leaving the clock lifecycle and every sequencing, daily-condition, foe-cadence, and quest-machine tick unchanged. Unknown or altered clocks preserve native expiry. | 2026-09-19 |
| M6-VAMP-TRANSITION-001 | `Assets/Scripts/Game/MagicAndEffects/Effects/Diseases/VampirismInfection.cs` | `DeployFullBlownVampirism()` before vanilla transformation mutations | Allow DFMP to consume the two-week vampirism transformation and request the server-owned time, cemetery relocation, and client effect transition. | 2026-09-10 |
| DFMP-STARTUP-001 | `Assets/Scripts/Game/Utility/SceneControl.cs` | `Start()` before the startup-scene vs game-scene branch | Allow DFMP to keep the launcher menu reachable so the single-player game scene is never auto-loaded. | 2026-09-09 |
| DFMP-STARTUP-004 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallUnitySetupGameWizard.cs` | `Setup()` startup-stage selection | When DFMP forces the startup menu and the game-data path is valid, open the options/Join Server page instead of the first-time game-folder page. | 2026-09-09 |
| DFMP-STARTUP-002 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallUnitySetupGameWizard.cs` | End of `ShowOptionsPanel()` | Allow DFMP to relabel and resize the launcher's confirm button ("Play" -> "Join Server"). | 2026-09-09 |
| DFMP-STARTUP-003 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallUnitySetupGameWizard.cs` | `ShowNextStage()`, `SetupStages.LaunchGame` case | Allow DFMP to consume the launch action and open the server list instead of loading the single-player game scene. | 2026-09-09 |
| DFMP-STARTUP-005 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallAdvancedSettingsWindow.cs` | `Gameplay()` checkbox construction and `SaveSettings()` | Hide DFU's Start In Dungeon option so testers cannot force every join through Privateer's Hold. | 2026-09-17 |
| DFMP-STARTUP-006 | `Assets/Scripts/Game/Utility/StartGameBehaviour.cs` | `StartNewCharacter()` start-parameter selection | Ignore Start In Dungeon during multiplayer new-character startup so the server-assigned persisted or configured spawn can apply. | 2026-09-17 |
| M7-PVP-HIT-001 | `Assets/Scripts/Game/WeaponManager.cs` | `WeaponDamage()` before vanilla entity damage resolution | Allow DFMP to consume native player melee and bow hits against a remote-player collider and submit authoritative damage without changing vanilla single-player behavior. The hook receives native arrow flags so DFMP can distinguish ranged hit production in diagnostics; vanilla missile collision and ordinary enemy handling remain untouched. | 2026-09-12 |
| M7-PVP-MISSILE-001 | `Assets/Scripts/Game/DaggerfallMissile.cs` | `AssignBowDamageToTarget()` before the vanilla `targetEntities` guard | Allow DFMP to consume a player-fired arrow that hit a rendering-only remote-player collider, which has no `DaggerfallEntityBehaviour` and therefore cannot enter the vanilla target list. Returning false preserves the existing `WeaponManager.WeaponDamage()` path for ordinary DFU entities. | 2026-09-12 |
| M8-ACTION-DOOR-001 | `Assets/Scripts/Internal/DaggerfallActionDoor.cs` | `Open()` and `Close()` after tween setup when `activatedByPlayer == true` | Notify DFMP when a player toggles an action door so its state can be synchronized to co-located players in the same dungeon or building interior context without altering single-player door behavior. | 2026-09-13 |
| M8-INTERIOR-COLLISION-001 | `Assets/Scripts/Internal/DaggerfallInterior.cs` | new `DoCollisionLayout()`, plus an additive `AssignBlockData(door, location)` overload | Additive layout path that builds interior models and action doors without people, flats, or furniture activation so the dedicated server can host building collision. The location must be passed in because a headless server has no local player and `PlayerGPS.CurrentLocation` cannot resolve block data for a remote player's building. The existing `AssignBlockData(door)` keeps its `PlayerGPS` behavior and vanilla `DoLayout()` is unchanged. | 2026-09-20 |
| QUEST-FOE-PLACE-001 | `Assets/Scripts/Utility/GameObjectHelper.cs` | `AddQuestFoe()` before native quest-foe object creation | Allow client-owned quest mode to register a resolved marker-bound foe with the server-owned quest enemy provider and suppress only the local gameplay enemy. | 2026-09-19 |
| QUEST-FOE-DYNAMIC-001 | `Assets/Scripts/Game/Questing/Actions/CreateFoe.cs` | `PlaceFoeFreely()` after native geometry/FOV placement succeeds and before the local foe is finalized | Preserve native `CreateFoe` cadence, chance, context restrictions, FOV choice, and geometry-safe placement while replacing the local gameplay foe with an owner-scoped server registration. The hook receives both the native placement point and `floorHit.point`, because native parks the foe 1.25m above that floor and only settles it in `FinalizeFoe`, which this hook pre-empts; DFMP descriptors are feet-grounded and register the ground contact. | 2026-09-20 |
| QUEST-CLOCK-JOURNAL-001 | `Assets/Scripts/Game/Questing/Clock.cs` | `ExpandMacro(DetailsMacro)` before native remaining-days text | Show `no deadline` for suppressed failure clocks instead of a frozen or zeroed countdown. | 2026-09-19 |
| QUEST-TELEPORT-001 | `Assets/Scripts/Game/Questing/Actions/TeleportPc.cs` | `Update()` after marker pose resolution and before `RespawnPlayer()` | Route quest teleport through a validated `QuestTeleport` transition assignment. | 2026-09-19 |
| QUEST-FOE-CMD-001 | `Assets/Scripts/Game/Questing/Actions/KillFoe.cs` | `Update()` before `Foe.Kill()` | Replace local instant kill with an owner-authorized server kill of the registered quest enemy. | 2026-09-19 |
| QUEST-FOE-CMD-002 | `Assets/Scripts/Game/Questing/Actions/RemoveFoe.cs` | `Update()` before hiding the foe | Hide the logical foe and retire the physical server encounter. | 2026-09-19 |
| QUEST-FOE-CMD-003 | `Assets/Scripts/Game/Questing/Actions/RestrainFoe.cs` | `Update()` before `SetRestrained()` | Keep native logical restraint and command the server-owned encounter. | 2026-09-19 |
| QUEST-FOE-CMD-004 | `Assets/Scripts/Game/Questing/Actions/UnrestrainFoe.cs` | `Update()` before `ClearRestrained()` | Keep native logical restraint clear and command the server-owned encounter. | 2026-09-19 |
| QUEST-FOE-CMD-005 | `Assets/Scripts/Game/Questing/Actions/GiveItem.cs` | `Update()` after `Foe.QueueItem()` before mutating a native enemy/corpse | Keep the logical item queue and place quest loot only in the owner's server-owned corpse loot. | 2026-09-19 |
| QUEST-FOE-CMD-006 | `Assets/Scripts/Game/Questing/Actions/CastSpellOnFoe.cs` | `Update()` after `QueueSpell()` | Persist the native spell queue on the owner objective; do not substitute direct damage. | 2026-09-19 |
| QUEST-FOE-CMD-007 | `Assets/Scripts/Game/Questing/Actions/ChangeFoeTeam.cs` | `Update()` before scanning active local enemies | Prevent unscoped local enemy scans; apply team on the owner-scoped server encounter. | 2026-09-19 |
| QUEST-FOE-CMD-008 | `Assets/Scripts/Game/Questing/Actions/ChangeFoeInfighting.cs` | `Update()` before scanning active local enemies | Prevent unscoped local enemy scans; apply infighting on the owner-scoped server encounter. | 2026-09-19 |
| QUEST-SCENE-001 | `Assets/Scripts/Game/Questing/Actions/Enemies.cs` | `Update()` before `ClearEnemies()` / `MakeEnemiesHostile()` | Consume the unscoped scene-wide command so it cannot mutate server-owned or other owners' enemies. | 2026-09-19 |
| QUEST-SCENE-002 | `Assets/Scripts/Game/Questing/Actions/SpawnCityGuards.cs` | `Update()` before `SpawnCityGuards()` | Consume local unsynchronized guard spawns until a server-owned guard provider exists. | 2026-09-19 |
| DFMP-PERSIST-GUILD-001 | `Assets/Scripts/Game/Guilds/GuildManager.cs` | `AddMembership()` / `RemoveMembership()` after membership mutation | Notify DFMP so guild join/leave can flush quest-adjacent character state immediately rather than waiting for the 60s autosave. | 2026-09-19 |
| DFMP-PERSIST-EXIT-001 | `Assets/Scripts/Game/DaggerfallUI.cs` | `dfuiExitGame` before vanilla `Application.Quit` | Allow DFMP to flush persistable character state and delay quit until the reliable payload can land. | 2026-09-19 |
| QUEST-REVEAL-001 | `Assets/Scripts/Game/Questing/Actions/RevealLocation.cs` | `Update()` after native `DiscoverLocation()` | Notify DFMP so a named local building can be discovered on the city automap and the quest envelope can flush immediately. | 2026-09-19 |
| QUEST-START-001 | `Assets/Scripts/Game/Questing/QuestMachine.cs` | `StartQuest(Quest)` before `AddQuestTopicWithInfoAndRumors()` | Register the quest UID before TalkManager topics so residence undiscover and dialog reveal can resolve the live quest. | 2026-09-19 |
| QUEST-START-002 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallQuestPopupWindow.cs` | `OfferQuest_OnButtonClick()` Yes path | Start the quest before expanding AcceptQuest text so `_house_` dialog reveal can find `dictQuestInfo`. | 2026-09-19 |
| QUEST-START-003 | `Assets/Scripts/Game/UserInterfaceWindows/DaggerfallDaedraSummonedWindow.cs` | Accept-key path before `HandleAnswer(AcceptQuest)` | Same start-before-accept-text order as guild quest popups. | 2026-09-19 |

---

## M6 Time-Advance Audit

The upstream rest window has no separate wait-until-dawn action. Its only client-side time-advance entry points are timed rest, rest-until-healed, and loiter. `M6-REST-001` consumes loiter always and consumes timed/full rest when the server policy is disabled. When `Rest.Policy = ServerManaged`, vanilla rest proceeds and `M6-REST-002` skips `RaiseTime()` / quest ticks so shared world time does not move.

Additional `RaiseTime()` paths were audited in upstream DFU. Fast travel is already covered by `M6-FAST-TRAVEL-001`. Quest training, guild training, prison/court time, and one-minute vampirism or lycanthropy cure cleanup advances are now blocked in multiplayer while preserving their surrounding effects. The two-week vampirism transformation is now intercepted in multiplayer and routed through the server-owned time and cemetery transition path.

## Hook Interface Definitions

Static hook entry points live under `Assets/DFMP/Hooks/` (e.g. `DaggerfallHooks.cs`).
