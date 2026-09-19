# Quest Action Compatibility Audit

This audit covers all 82 active templates registered by
`QuestMachine.RegisterActionTemplates()`. The commented `JuggleAction` example is
not registered and is not counted.

## Classification rules

- **Personal-local** — runs in the owning character's native quest machine and
  changes only that character's quest graph, UI, inventory, dialogue, faction
  state, or personal quest presentation.
- **Server-authorized transition/time** — may read replicated world time, but
  any relocation or world-time mutation must be consumed by DFMP and completed
  through a validated server assignment.
- **Owner-scoped quest-enemy command** — targets a quest `Foe`; the owner quest
  machine decides intent, while the server owns the physical foe and returns
  idempotent objective results to that owner.
- **Unsupported defect** — the native implementation performs an unscoped
  scene-wide or local enemy operation that cannot safely run in multiplayer.

Compatibility status is separate from classification. `Ready` means the native
client-local behavior can run unchanged. `Guarded` means an existing DFMP hook
already removes the authority violation. `Blocked` means the action must not be
allowed to execute its native side effect until the named route exists.

## Persistence map

The current quest envelope captures four sections:

- **Q** — `QuestMachineData_v1`: quests, tasks, action save data, resources,
  clocks, and `SiteLink`s.
- **F** — `FactionData_v2`: personal faction and NPC reputation state.
- **C** — `TalkManager.SaveDataConversation`: quest rumors/dialogue state.
- **N** — `PlayerNotebook.NotebookData_v1`: journal/notebook entries.

The character record separately persists **R** fields used here: world context
and pose, vitals, gold, attributes, skills, inventory, equipment, and complete
quest-item identity. Restore order must remain Q/F/C/N before inventory so item
resources exist before quest-linked items are reconstructed.

The quest envelope now also captures:

- **P1** — discovered travel-map locations.
- **P2** — diseases, vampirism/lycanthropy, and effect bundles.
- **P3** — `CrimeCommitted` and per-region legal reputation.
- **P4** — `WorldDataVariants` mutations.
- **P5** — `TimeOfLastSkillTraining`.
- **P6** — owner-scoped foe generation, health, queued loot/spells, restraint/team/infighting, and pending credit.
- **P7** — `QuestListsManager.oneTimeQuestsAccepted`.
- **P8** — first-class guild and vampire memberships (group, rank, last rank change, variant, flags). Envelope v1 still restores memberships from `QuestAdjacentPlayerJson`.
- **P9** — biography mods, skill-use progress, wagon/other items, rented rooms, Thieves Guild / Dark Brotherhood invitation tallies, bank accounts, houses, owned ship, and escorting-companion faces.
- Close-flush: pause-menu Exit, window close, and `StopClient` send identity, pose/context, and the full envelope before disconnect. Guild join/leave also request an immediate envelope save.

## Registered action matrix

| Registered template | Classification | Actual code effect | Persistence | Status / route |
|---|---|---|---|---|
| `WhenPcEntersExits` | Personal-local | Tests `PlayerGPS` against quest place enter/exit state. | Q + R context | Ready. |
| `WhenNpcIsAvailable` | Personal-local | Listens for faction persons and the last clicked static NPC. | Q + F | Ready. |
| `WhenReputeWith` | Personal-local | Tests personal NPC/faction reputation. | Q + F | Ready. |
| `WhenSkillLevel` | Personal-local | Tests the player's live skill value. | Q + R skills | Ready. |
| `WhenAttributeLevel` | Personal-local | Tests the player's live attribute value. | Q + R attributes | Ready. |
| `WhenTask` | Personal-local | Tests another task in the same quest. | Q | Ready. |
| `ClickedNpc` | Personal-local | Consumes owner-local static-NPC click state; may branch or deduct gold. | Q + R gold | Ready. |
| `ClickedItem` | Personal-local | Tests click state on an owner-local quest item. | Q + R item identity | Ready. |
| `LevelCompleted` | Personal-local | Tests personal level progression. | Q + R level/skills | Ready. |
| `InjuredFoe` | Owner-scoped quest-enemy command | Tests `Foe.InjuredTrigger`. | Q + P6 | Ready: non-fatal server damage delivers an injured result to the owner. |
| `KilledFoe` | Owner-scoped quest-enemy command | Tests `Foe.KillCount` and optionally displays a message. | Q + P6 | Ready: server death credit updates the owner's logical kill count. |
| `TotingItemAndClickedNpc` | Personal-local | Tests owner inventory plus NPC click, then releases the quest item for reoffer. | Q + R item identity | Ready. |
| `DailyFrom` | Personal-local | Tests a daily interval against replicated `WorldTime`. | Q; time is replicated | Ready; read-only time dependency. |
| `DroppedItemAtPlace` | Personal-local | Tests owner-local quest item placement at a quest place. | Q + R item identity/context | Ready. |
| `Season` | Personal-local | Tests season from replicated `WorldTime`. | Q; time is replicated | Ready; read-only time dependency. |
| `Weather` | Server-authorized transition/time | Tests `WeatherManager` state that Phase 1 defines as server-owned. | Q; missing replicated weather state | Blocked: no Layer 3 weather replication exists, so clients can evaluate this trigger differently. |
| `Climate` | Personal-local | Tests the player's current local climate. | Q + R context | Ready. |
| `EndQuest` | Personal-local | Marks the quest broken/ended for tombstoning. | Q | Ready. |
| `Prompt` | Personal-local | Shows yes/no UI and starts the selected task. | Q | Ready. |
| `Say` | Personal-local | Shows a quest message popup. | Q | Ready. |
| `PlaySound` | Personal-local | Plays owner-local audio with a world-time cooldown. | Q | Ready. |
| `StartTask` | Personal-local | Starts a task in the same quest. | Q | Ready. |
| `ClearTask` | Personal-local | Clears a task in the same quest. | Q | Ready. |
| `LogMessage` | Personal-local | Adds a quest log step. | Q | Ready. |
| `PickOneOf` | Personal-local | Uses the quest-machine seed to choose and start one task. | Q | Ready; preserve the native seed/action state. |
| `RemoveLogMessage` | Personal-local | Removes a quest log step. | Q | Ready. |
| `PlayVideo` | Personal-local | Plays a local quest video. | Q | Ready. |
| `PcAt` | Personal-local | Tests owner context/place and starts a task. | Q + R context | Ready. |
| `CreateNpcAt` | Personal-local | Legacy no-op reservation action; DFU placement actions create the `SiteLink`. | Q | Ready. |
| `CreateNpc` | Personal-local | Places a quest `Person` at its generated home. | Q | Ready; NPC remains owner-local. |
| `PlaceNpc` | Personal-local | Creates a `SiteLink` and assigns a `Person` to a quest marker. | Q | Ready. |
| `PlaceItem` | Personal-local | Creates a `SiteLink` and assigns an item to a quest marker. | Q + R item identity | Ready; object remains owner-only. |
| `GivePc` | Personal-local | Gives/reoffers a quest item or completes with no reward. | Q + R item identity | Ready. |
| `GiveItem` | Owner-scoped quest-enemy command | Queues an item on a `Foe` (the practical native use), updates a spawned enemy/corpse, and removes the owner's copy. | Q + R item identity + P6 | Ready for foe targets: logical queue remains; physical loot is owner-only corpse loot. |
| `StartStopTimer` | Personal-local | Starts/stops a quest `Clock`; elapsed time comes from replicated world time. | Q | Ready; do not blanket-disable clocks. |
| `CreateFoe` | Owner-scoped quest-enemy command | On a world-time interval/chance, creates native enemy GameObjects around the player. | Q + P6 | Ready: native cadence/placement remains, physical spawn is an owner-scoped server registration. |
| `PlaceFoe` | Owner-scoped quest-enemy command | Creates a `SiteLink` and binds a `Foe` to a quest marker. | Q + P6 | Ready: logical placement remains; physical activation uses the owner-scoped server provider. |
| `HideNpc` | Personal-local | Sets a quest `Person.IsHidden`. | Q | Ready. |
| `RestoreNpc` | Personal-local | Clears a quest `Person.IsHidden`. | Q | Ready. |
| `AddFace` | Personal-local | Adds an owner HUD escort portrait for a person or foe. | Q | Ready; no physical foe mutation. |
| `DropFace` | Personal-local | Removes an owner HUD escort portrait for a person or foe. | Q | Ready; no physical foe mutation. |
| `GetItem` | Personal-local | Transfers an item from an owner-local NPC/resource to the player. | Q + R item identity | Ready for personal NPC/object resources. |
| `StartQuest` | Personal-local | Loads and schedules another quest. | Q + P7 | Ready; one-time quest acceptance is in the quest envelope. |
| `RunQuest` | Personal-local | Schedules a subquest, waits for its result, branches, then tombstones it. | Q + P7 | Ready; one-time quest acceptance is in the quest envelope. |
| `UnsetTask` | Personal-local | Drops one or more tasks. | Q | Ready. |
| `ChangeReputeWith` | Personal-local | Changes personal faction/NPC reputation. | Q + F | Ready. |
| `ReputeExceedsDo` | Personal-local | Tests personal reputation and starts a task. | Q + F | Ready. |
| `RevealLocation` | Personal-local | Discovers a travel-map location and optionally adds a notebook note. | Q + N + P1 | Ready; discovered locations persist in the quest envelope. |
| `RestrainFoe` | Owner-scoped quest-enemy command | Calls `Foe.SetRestrained()`. | Q + P6 | Ready: logical restraint plus owner-scoped server command. |
| `MakePermanent` | Personal-local | Converts a quest item into a permanent inventory item. | Q + R item identity | Ready. |
| `HaveItem` | Personal-local | Tests owner inventory for a quest item. | Q + R item identity | Ready. |
| `AddAsQuestor` | Personal-local | Adds a resource to the quest's questor set. | Q | Ready. |
| `DropAsQuestor` | Personal-local | Removes a resource from the quest's questor set. | Q | Ready. |
| `ItemUsedDo` | Personal-local | Listens for owner use of a quest item and starts a task. | Q + R item identity | Ready. |
| `TakeItem` | Personal-local | Releases/removes a quest item from owner inventory. | Q + R item identity | Ready. |
| `TeleportPc` | Server-authorized transition/time | Creates a `SiteLink`, resolves a dungeon and marker, calls `RespawnPlayer()`, then writes the local transform on the next tick. | Q + R context/pose | Guarded: `QUEST-TELEPORT-001` consumes native relocation and waits for a `QuestTeleport` assignment. |
| `DialogLink` | Personal-local | Adds owner-local dialogue linked to a quest resource. | Q + C | Ready. |
| `AddDialog` | Personal-local | Adds quest dialogue to the owner conversation system. | Q + C | Ready. |
| `RumorMill` | Personal-local | Adds a quest rumor to the owner `TalkManager`. | Q + C | Ready. |
| `MakePcDiseased` | Personal-local | Creates and assigns a disease effect bundle. | Q + P2 | Ready; quest-adjacent player payload persists effect bundles. |
| `CurePcDisease` | Personal-local | Cures disease or ends vampirism/lycanthropy. | Q + P2 | Ready; quest-adjacent player payload persists effect bundles. Existing one-minute cure hooks still prevent client world-time mutation. |
| `CastSpellDo` | Personal-local | Observes the owner's readied/cast spell and starts a task on effect match. | Q | Ready. |
| `CastEffectDo` | Personal-local | Observes the owner's readied/cast effect key and starts a task. | Q | Ready. |
| `CastSpellOnFoe` | Owner-scoped quest-enemy command | Queues a classic/custom spell on a `Foe`. | Q + P6 | Guarded: spell identity is queued on the owner objective; no direct-damage stand-in. Native M8 spell reproduction remains follow-up. |
| `RemoveFoe` | Owner-scoped quest-enemy command | Sets the logical `Foe.IsHidden`. | Q + P6 | Ready: logical hide plus physical despawn of that owner's encounter. |
| `LegalRepute` | Personal-local | Changes current-region legal reputation. | Q + P3 | Ready; legal reputation persists in quest-adjacent player state. |
| `MuteNpc` | Personal-local | Changes owner-local quest-person dialogue availability. | Q | Ready. |
| `DestroyNpc` | Personal-local | Soft-destroys an owner-local quest `Person`. | Q | Ready. |
| `WorldUpdate` | Personal-local | Mutates client `WorldDataVariants` for a location/block/building. | Q + P4 | Ready; world-variation payload is personal and persisted. Must not become shared world state. |
| `Enemies` | Unsupported defect | Calls scene-wide `GameManager.ClearEnemies()` or `MakeEnemiesHostile()`. | None sufficient | Guarded: native side effect is consumed in multiplayer so it cannot target server-owned or other owners' foes. |
| `ClickedFoe` | Owner-scoped quest-enemy command | Tests `Foe.HasPlayerClicked`, may deduct owner gold, shows dialogue, and rearms click state. | Q + R gold + P6 | Ready: owner activate on the server enemy delivers an idempotent click result. |
| `KillFoe` | Owner-scoped quest-enemy command | Calls `Foe.Kill()` immediately. | Q + P6 | Ready: owner-authorized server kill uses the M7/M8 death lifecycle. |
| `PayMoney` | Personal-local | Tests and deducts owner gold, then branches. | Q + R gold | Ready. |
| `JournalNote` | Personal-local | Adds a message to the owner notebook. | Q + N | Ready. |
| `ChangeFoeInfighting` | Owner-scoped quest-enemy command | Scans active native enemies and changes `QuestResourceBehaviour.IsAttackableByAI`. | Q + P6 | Guarded: local scan is consumed; flag is stored on the owner objective. |
| `ChangeFoeTeam` | Owner-scoped quest-enemy command | Scans active native enemies and changes `EnemyEntity.Team`. | Q + P6 | Guarded: local scan is consumed; team is stored on the owner objective. |
| `PlaySong` | Personal-local | Changes owner-local music presentation. | Q | Ready. |
| `SetPlayerCrime` | Personal-local | Sets `PlayerEntity.CrimeCommitted`. | Q + P3 | Ready; crime state persists in quest-adjacent player payload. |
| `SpawnCityGuards` | Unsupported defect | Calls `PlayerEntity.SpawnCityGuards()`, creating unsynchronized local combatants. | None sufficient | Guarded: native spawn is consumed until guards have a server-owned provider. |
| `UnrestrainFoe` | Owner-scoped quest-enemy command | Calls `Foe.ClearRestrained()`. | Q + P6 | Ready: logical clear plus owner-scoped server command. |
| `TrainPc` | Server-authorized transition/time | Completes the quest, records training time, attempts a three-hour advance, reduces fatigue, and tallies the skill. | Q + R skills/fatigue + P5 | Guarded: `M6-TIME-TRAIN-001` consumes the three-hour client advance. Training time persists in quest-adjacent player state. |
| `PromptMulti` | Personal-local | Shows a multi-choice owner UI and starts the selected task. | Q | Ready. |

## Exact authority hook points

No new hook is added by this audit because none of the blocked routes can be
completed safely without the concurrent server/objective work or a change to
the protected server handler surface.

### Teleport

The minimal Layer 1 interception point is
`Assets/Scripts/Game/Questing/Actions/TeleportPc.cs`,
`TeleportPc.Update()`, after the target `Place`, location, and quest marker have
resolved but **before** `PlayerEnterExit.RespawnPlayer()` (currently line 116).
The hook must be a consume-style delegate carrying quest UID, place symbol/site
identity, marker index, and resolved marker pose. When consumed, the action must
wait for a server response and call `SetComplete()` only after the normal
transition acknowledgement. The current next-tick write to
`PlayerObject.transform.position` (currently line 84) must not run on the
multiplayer path.

Layer 3 must add a dedicated quest-teleport request/response that validates the
bound character, pending-transition state, destination location, dungeon
identity, and marker bounds, then issues a normal `DFMPTransitionAssignment`
with a distinct quest-teleport kind. The existing door/dungeon requests validate
physical current-location transitions and cannot safely authorize an arbitrary
quest destination. The developer teleport route is privileged tooling and is
not a gameplay authorization path.

### Owner-scoped foes

The logical-to-physical boundary belongs at these native side effects:

- `CreateFoe.Update()` before `CreatePendingFoeSpawn()` and therefore before
  `GameObjectHelper.CreateFoeGameObjects()`.
- `PlaceFoe.Update()` at `Place.AssignQuestResource(foe.Symbol, marker)`: retain
  the logical Q mutation, then register marker/context intent with Layer 3.
- `KillFoe.Update()` before `Foe.Kill()`.
- `RemoveFoe.Update()` before `Foe.IsHidden = true`.
- `RestrainFoe.Update()` / `UnrestrainFoe.Update()` before
  `SetRestrained()` / `ClearRestrained()`.
- `CastSpellOnFoe.Update()` before `Foe.QueueSpell()`.
- `GiveItem.Update()` before `Foe.QueueItem()` or mutation of a native
  enemy/corpse collection.
- `ChangeFoeInfighting.Update()` and `ChangeFoeTeam.Update()` before their
  `ActiveGameObjectDatabase.GetActiveEnemyEntities()` scans.
- `InjuredFoe.CheckTrigger()`, `KilledFoe.CheckTrigger()`, and
  `ClickedFoe.CheckTrigger()` must consume server-delivered owner objective
  results rather than infer physical state from native local enemies.

Prefer one narrow quest-foe bridge in Layer 2 over one networking-aware edit per
action. Layer 3 owns registration, activation distance/context validation,
generation identity, health, death, personal quest loot, result retry/ack, and
reconnect persistence.

### Existing time hook

`TrainPc.QuestCompleteMessage_OnClose()` already calls
`DaggerfallHooks.TryHandleTimeAdvance("QuestTraining", 10800)` immediately
before `WorldTime.Now.RaiseTime()`. `DFMPRestAdvanceController` consumes this
while connected, preserving training's fatigue and skill effects without moving
shared time. This is registered as `M6-TIME-TRAIN-001` in `HOOKS.md`.

## Blockers

1. `Enemies` and `SpawnCityGuards` remain unsupported native defects. Multiplayer consumes their side effects so they cannot mutate shared enemies or spawn local guards. A scoped replacement still requires native-intent research (`Enemies`) and a server-owned guard provider (`SpawnCityGuards`).
2. `CastSpellOnFoe` queues spell identity on the owner objective and does not fake damage. Native spell reproduction on M8 enemies is follow-up.
3. `ChangeFoeTeam` / `ChangeFoeInfighting` no longer scan the local enemy list; stored flags still need M8 AI enforcement.
4. `Weather` still has no Layer 3 weather replication, so clients can evaluate that trigger differently.
