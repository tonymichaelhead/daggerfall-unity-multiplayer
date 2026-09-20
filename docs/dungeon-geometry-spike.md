# M6.5 Dungeon Geometry Spike

## Status

Complete as a timeboxed feasibility spike. Native DFU dungeon geometry can be instantiated in the dedicated headless server after disabling optional dungeon texture randomization. The current native loading path is not production-ready for M8 without isolation and lifecycle hardening.

## Probe

The opt-in probe runs the native `DaggerfallDungeon.SetDungeon(location, importEnemies: false)` path from the dedicated bootstrap. It keeps generated geometry alive, records generated block count, elapsed layout time, managed-memory delta, and server heartbeat evidence, and logs full exceptions.

The probe uses `DungeonTextureUse.Disabled` because the headless settings path exposed an invalid `RandomDungeonTextures` value. Without that bypass, DFU failed before geometry layout in `RandomTextureTableClassic()` with an `IndexOutOfRangeException`.

Commands used:

```text
Daggerfall Unity.exe -batchmode -nographics -server -port 7777 -tickrate 30 -maxplayers 16 -heartbeat 2 -dfmp-dungeon-spike -dfmp-dungeon-spike-count 1
Daggerfall Unity.exe -batchmode -nographics -server -port 7777 -tickrate 30 -maxplayers 16 -heartbeat 2 -dfmp-dungeon-spike -dfmp-dungeon-spike-count 3
```

## Results

| Probe | Dungeons | Blocks | Layout time | Managed-memory delta | Post-probe heartbeat |
| --- | ---: | ---: | ---: | ---: | --- |
| Single | 1 | 5 | 413 ms | +36.4 MB | 29.7-30.0 FPS |
| Multi | 3 | 44 | 1,206 ms | +39.9 MB | 29.8-29.9 FPS |

Generated locations:

- Daggerfall / Privateer's Hold: 5 blocks
- Wayrest / Wayrest: 21 blocks
- Sentinel / Sentinel: 18 blocks

The process remained alive and continued publishing server heartbeats after both probes. Process working-set deltas reported as zero in `-nographics`, so managed-memory delta and frame rate are the useful measurements from this run.

## Findings

- Headless DFU can instantiate native dungeon geometry without a local player object or camera being required by this probe path.
- Geometry layout cost is approximately linear for this small sample: 413 ms for 5 blocks and 1,206 ms for 44 blocks.
- The current path emits duplicate action-door `LoadID` warnings when multiple generated dungeons coexist. These objects are not safe to serialize as-is and require unique identity/lifecycle handling before M8 persistence or shared dungeon state.
- Headless generation also emits audio-channel warnings. Server dungeon generation must suppress or remove audio behavior rather than relying on global audio mute settings.
- The probe disables enemy import, so it does not establish the cost or correctness of server-side enemy AI, navigation, collision, or replication.

## Recommendation

Proceed with the M7/M8 design on the assumption that selective native dungeon geometry hosting is viable for the target 8-16 player scale, but do not place `DaggerfallDungeon` instances directly into the final enemy service yet.

The next dungeon-host implementation must:

- own geometry in a server-side dungeon context service rather than a player prefab;
- generate only for occupied dungeon contexts and tear down after an explicit empty-context grace period;
- isolate or replace DFU action-door/load-ID registration so coexisting dungeons do not collide;
- disable audio, quest resources, UI, cameras, and player-only gameplay components on the server path;
- add deterministic generation and teardown diagnostics;
- measure process memory in a server build where working-set reporting is available;
- separately benchmark enemy import, navigation, collision, and tick cost before M8 commits to native AI.

The spike does not prove that full native dungeon AI is affordable. If enemy simulation or native collision proves too expensive, retain geometry only for collision/navigation support and use a simplified server-side enemy simulation.
