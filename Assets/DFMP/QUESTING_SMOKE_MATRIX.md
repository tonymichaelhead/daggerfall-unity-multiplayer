# Client-Owned Questing Smoke Matrix

Run these checks only after the listed EditMode fixtures pass. Use a dedicated
server and two normal clients unless a row says otherwise. Keep the server log
for the `[DFMP Quest Metrics]` sample at the end of each slice.

## Automated fixtures (run manually in Unity)

- `DFMPServerConfigAndDiscoveryTests`
- `M5CharacterPersistenceTests`
- `QuestObjectiveProtocolTests`
- `DynamicEnemyPolicyTests.QuestOwnerCue_RequiresQuestOwnershipNearRangeAndFocus`
- `QuestClockPolicyTests`
- `QuestObjectiveProtocolTests.Reentry_PreservesGenerationAndHealth`
- `QuestObjectiveProtocolTests.QuestTeleport_RejectsInvalidMarkerBounds`

## Slice 1: personal non-combat persistence

1. Client A accepts a non-combat random quest, advances at least two branches,
   adds a journal/notebook entry, and reveals a location.
2. Record active quest count, current log text, inventory quest item, faction
   reputation, guild state, legal reputation, and discovered map locations.
3. Disconnect cleanly (pause-menu Exit **or** closing the client window), restart
   the dedicated server, and reconnect.
4. Verify every recorded value, quest item UID/symbol, and current branch is
   unchanged. Complete the quest and verify rewards persist after a second
   reconnect.
5. Log out inside a guild hall, reconnect into that interior, and accept a quest
   from the questor. Verify the journal gains the quest, the questor stops
   offering new work, and the client log shows no `Terminating quest runtime`.
6. Join the Fighters Guild (or equivalent), change nothing else, Exit immediately
   from the pause menu, reconnect, and confirm membership at rank 0 with faction
   reputation intact. Repeat once by closing the client window after changing
   gold, equipment, or health.

## Slice 2: marker-bound foe and helper credit

1. Clients A and B enter the same dungeon. A owns a marker-bound quest foe.
2. Verify no foe exists until A is in the exact context and within 30 metres.
3. Verify both clients see one server-owned foe and the owner cue appears only
   near/focused.
4. B kills the foe. Verify only A's quest advances and duplicate result delivery
   does not grant a second kill.
5. Repeat, but move A beyond 45 metres for 10 seconds while B stays fighting.
   Verify despawn. Re-enter and verify the same generation returns with retained
   health/injury state.

## Slice 2b: first-entry building foe

1. While connected, accept a Fighters Guild quest that places a marker-bound foe
   in a local building.
2. Record the reveal log's `questUid`, `mapId`, `buildingKey`, and name. At the
   target door, verify the building-entry log has the same `buildingKey`.
3. Enter once. Verify there is no `Unknown message id: 8107` or disconnect and
   that the registration result shows the requested context matching the
   server-issued pending context.
4. Verify the objective becomes `Armed` during the pending transition, then
   `SpawnedAlive` only after the transition is acknowledged, the confirmed
   building context matches, and the owner is within 30 metres.
5. Kill the foe and verify quest credit without leaving and re-entering.
6. Repeat with a different nearby building. Its door key must differ from the
   revealed target, no registration should be sent, and no quest foe should
   appear.

## Slice 3: foe-carried quest item

1. Use a quest that queues an item on a foe.
2. Have B deliver the killing blow.
3. Verify A's personal corpse loot contains the quest item with the correct UID
   and symbol. Verify B sees only ordinary personal loot and cannot take A's
   quest item.
4. Disconnect A before looting, reconnect, and verify the pending corpse/item
   state survives.

## Slice 4: dynamic `CreateFoe`

Run one representative quest in each context:

- building interior
- dungeon
- town exterior
- wilderness exterior

Verify native interval, chance, count, FOV-facing placement, geometry safety,
pending-wave cancellation, owner-only activation, helper combat, and departure
hysteresis. Any local-only foe or simplified placement is a failure.

## Slice 5: progression breadth

- Complete representative fighters, mages, thieves, temple, knightly order,
  and miscellaneous quests across reconnects.
- Exercise disease/cure, crime/legal reputation, training, location reveal,
  chained/subquests, quest teleport, guards, and scene-wide enemy actions.
- Complete the main quest with at least one logout and one server restart per
  major branch.

## Scale/lifecycle soak

Create the maximum configured objective count on multiple characters and cycle
contexts for at least one hour. Confirm:

- no global per-frame player/objective cross-product scan
- objective/spawn/pending-ack metrics return to baseline after completion
- no duplicate credit after retries
- no orphan state projection after cancellation, disconnect, or restart
- payload sizes stay below configured bounds
- server tick and per-context enemy work remain stable
