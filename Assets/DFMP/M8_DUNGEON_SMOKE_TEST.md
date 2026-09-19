# M8 Dungeon Enemy Smoke Test

This is the comprehensive two-client closeout test for M8. Run it through the graphical client and dedicated server builds. Do not use Unity batchmode while the Editor project is open.

## Setup

- Start one dedicated server with `dfmp-server.json` and a clean server log directory.
- Start two matching client builds with separate account IDs.
- Use a dungeon with at least two connected blocks and enough room to observe movement.
- Keep the server and both client logs available for inspection.
- Record the server build timestamp and client build timestamps before testing.

## Test Order

### 1. Shared entry and roster

1. Connect both clients to the server.
2. Enter the same dungeon and remain in different dungeon blocks briefly.
3. Move both clients into the same block.
4. Confirm both clients receive the same dynamic enemy IDs for that block.
5. Confirm each client sees the same enemy positions, facing, mobile types, and movement.

Expected server evidence:

- One dungeon geometry scope is hosted for the shared dungeon instance.
- One roster activation is logged per occupied dungeon block.
- State projections are spawned for the active roster.
- No native local enemy roster appears on either client.

### 2. Geometry movement and sensing

1. Allow an enemy to pursue a player toward a visible wall, pillar, brazier, or doorway.
2. Confirm the enemy does not pass through hosted dungeon geometry.
3. Confirm a thin walkable blocker (brazier, pillar) causes a ±45° detour rather than a permanent freeze.
4. Confirm an enemy meeting a solid wall follows it sideways instead of freezing flush against it, and does **not** drop the target (no stuck-recovery release).
5. Stand behind a pillar with the enemy on the far side and confirm it comes around rather than stalling at the pillar face.
6. For a `CanOpenDoors` enemy, confirm an unlocked closed door opens and pursuit continues through.
7. Move the player out of detection until give-up expires, then reacquire.

Expected server evidence:

- Target acquisition is logged once per target transition.
- Blocked movement does not produce wall-crossing positions.
- `Detour started` appears when an enemy meets a blocker, rate-limited per enemy; the enemy position changes over the following ticks rather than repeating one value.
- Detour-failed warnings appear only when every 45° sample is blocked, never a stuck-recovery target release.
- Unlocked doors opened by enemies emit an action-door sync to observers.
- Strict line-of-sight behavior fails closed if geometry is unavailable.

### 3. Player damage and shared health

1. Have client 1 damage a dynamic enemy with melee.
2. Confirm both clients observe the same reduced enemy health through the death result or subsequent state.
3. Have client 2 damage the same enemy with a ranged attack.
4. Confirm the server accepts both through the M7 damage chokepoint.
5. Verify malformed, out-of-range, dead-attacker, or transition-pending damage is rejected if those cases are exercised during the run.

Expected server evidence:

- Accepted player damage identifies the same enemy ID.
- Rejected damage includes a concise validation reason.
- No client directly changes enemy health.

### 4. Enemy attack profiles and presentation

1. Observe a melee-capable enemy attack at close range.
2. Observe a ranged-capable enemy, such as a Harpy or Archer profile, attack from its longer range.
3. Observe a magic-capable enemy, such as an Orc Shaman or Lich profile, apply direct damage.
4. Confirm both clients show the corresponding native attack animation.
5. Confirm attack animation changes are not duplicated every AI tick.

Expected server evidence:

- Each accepted enemy attack logs enemy ID, target connection, attack kind, and damage.
- Enemy damage is applied through the server-owned vital path.
- Enemy attacks stop when the target is dead or in a pending transition.

### 5. Death, loot, and respawn

1. Kill one shared dynamic enemy using both clients.
2. Confirm exactly one server `EnemyDied` event and one `LootGenerated` event.
3. Confirm each client receives one personal corpse presentation for the dead enemy.
4. Attempt to damage the dead enemy again and confirm rejection.
5. Exercise player death and M7 respawn while the dungeon remains occupied.
6. Confirm enemy state and player state do not cross-contaminate.

Expected server evidence:

- The enemy transitions to `Dead` and stops moving or attacking.
- One kill-credit record is emitted.
- No stale attack cooldown or motor/senses state survives death.
- Player respawn completes through the existing M7 transition path.

### 6. Re-entry and disconnect cleanup

1. Have both clients leave the dungeon block.
2. Wait through the configured despawn delay.
3. Confirm state projections and geometry are torn down when the dungeon becomes empty.
4. Re-enter with one client, then the second client.
5. Confirm alive enemy state resumes as expected and dead state remains dead for the server lifetime.
6. Disconnect one client unexpectedly while enemy proxies are visible.
7. Reconnect and confirm no stale enemy proxies, colliders, corpse objects, or duplicate state projections remain.

Expected server/client evidence:

- Empty-context despawn is logged once.
- Re-entry cancels a pending despawn when applicable.
- Geometry scope occupancy counts return to zero after the final departure.
- Client disconnect removes dynamic enemy proxies immediately.
- Reconnection does not duplicate enemy IDs or presentation objects.

## Pass Criteria

M8 passes only when all of the following are true:

- Both clients observe the same server-owned enemy IDs and positions.
- Enemy movement respects hosted dungeon geometry and native-style detours around walkable blockers.
- No enemy remains pinned against a wall, pillar, or doorway while it still has a target. **Known exception:** intermittent sticking at 90-degree corners is a deferred defect (M9 AI parity item 7) and does not block this smoke test.
- Enemies do not drop targets solely because movement is blocked.
- Melee, ranged, and direct-damage magic profiles work through server authority.
- Both clients observe replicated attack presentation state.
- Shared enemy health, death, kill credit, and personal corpse loot behave correctly.
- M7 player death and respawn still works during dungeon combat.
- Empty-context teardown and re-entry preserve intended in-memory lifecycle.
- Unexpected disconnects leave no stale enemy presentation or hit targets.
- Server and client logs contain no unexplained exceptions, duplicate roster activation, duplicate death/loot events, or persistent stale-state warnings.

Record the date, build identifiers, server configuration, dungeon location, and log filenames with the result. Keep the smoke evidence with the M8 closeout change before changing the roadmap status to Done.
