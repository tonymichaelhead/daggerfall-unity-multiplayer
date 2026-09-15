# DFMP Developer Commands

Developer commands are server-authorized tools for repeatable testing and development. They are disabled by default and must never be treated as an administrator permission system.

## Enablement

Set this in the server's `dfmp-server.json`:

```json
"Developer": {
  "CommandsEnabled": true
}
```

Restart the server after changing the setting. Keep it disabled on any server that accepts untrusted players.

## `dfmp_infect_self`

Run this in the connected DFMP client's DFU console:

```text
dfmp_infect_self
```

The client sends a request to the server. The server accepts it only when developer commands are enabled, the requester has a session, and the session has completed spawning. The server then authorizes the client to assign DFU's `Vampirism-Infection` bundle through the `SpecialInfection` path.

The command starts infection; it does not immediately transform the character. Advance the server-owned game clock through the infection lifecycle, then verify the normal multiplayer transformation path:

1. The client consumes vanilla's local transformation before it advances time or relocates.
2. The server advances shared time to the two-week-to-dusk target.
3. The server selects a cemetery in the current region and sends a transition assignment.
4. The client relocates, applies the vampire effect, and acknowledges the transition.

Expected log evidence includes `[DFMP Developer] Authorized vampirism infection`, `[DFMP Time] Server advanced time`, and `[DFMP Transition] Accepted vampirism transformation request`.

This command and the transformation transition are intended for live-session testing. Active vampirism persistence across disconnect, reconnect, server restart, and character restore is deferred to Phase 2. Lycanthropy is completely deferred to Phase 2 and has no supported DFMP developer command or multiplayer transformation path in M6.

## `dfmp_advance_time`

Run this in the connected DFMP client's DFU console:

```text
dfmp_advance_time <minutes>
```

The server accepts positive advances up to seven in-game days (`10080` minutes). Examples:

```text
dfmp_advance_time 1440
dfmp_advance_time 4320
```

This changes shared server time and replicates the result to clients. It does not call `RaiseTime` locally. For the vampirism test, use `dfmp_advance_time 1440` followed by `dfmp_advance_time 4320`; the infection videos still close according to their normal DFU lifecycle.

## `dfmp_damage_player`

Run this in the connected DFMP client's DFU console while developer commands are enabled:

```text
dfmp_damage_player <connectionId> <amount>
```

This sends a bounded player damage intent through the normal PvP validation and vital application chokepoint. The server derives the source from the requesting connection and still enforces PvP policy, same-world context, range, cooldown, rate limits, and target lifecycle. It is a deterministic PvP smoke-test action, not a replacement for native weapon hit detection or an administrator permission system.

## `dfmp_damage_self`

Run this in the connected DFMP client's DFU console while developer commands are enabled:

```text
dfmp_damage_self <amount>
```

This exercises the beta-trusted local quest PvE path. The server derives both source and target from the submitting connection, so the request can only damage its owner. It uses the same bounds, cooldown, rate-limit, persistence, replication, and death/respawn chokepoint as PvP. It does not represent a synchronized quest enemy; actual quest-enemy production remains client-local and future native hooks remain out of scope.

## `dfmp_godmode`

Run this in the connected DFMP client's DFU console while developer commands are enabled:

```text
dfmp_godmode on
dfmp_godmode off
```

This toggles server-side invulnerability for the requesting connection. While enabled, accepted player damage and server-owned enemy damage are suppressed at the authoritative damage boundary, so health, death, respawn, and persistence state are not changed by those hits. Suppressed hits are logged for testing.

Godmode is an ephemeral developer test flag. It is cleared on disconnect, disabled unless developer commands are enabled, and is not an administrator permission or production gameplay feature. Admin-menu access is deferred to the future admin action surface.

## `dfmp_teleport_dungeon`

Run this in the connected DFMP client's DFU console while developer commands are enabled:

```text
dfmp_teleport_dungeon Daggerfall "Privateer's Hold"
```

The server resolves the region and location name, verifies that the location has a dungeon, and sends the normal server-authoritative dungeon-entry assignment. This works even when the location is not revealed on the local fast-travel map. The command is developer-only and intended for testing; it does not grant general teleport or administrator permissions.

The command is intentionally developer-only and bounded. It accepts only a target connection ID and damage amount for this smoke-test path. Future server actions should use the same validated service boundary:

- R2 scripts call server actions rather than mutating player objects directly.
- R3 admin/GM commands send authorized server requests and use the same action implementation.
- The future R3 GM menu should expose an `Advance World Time` action using this same server implementation, with a bounded amount input, role/permission checks, confirmation for large jumps, and audit logging.

All future world-control actions should return structured rejection reasons and produce an audit event before they are exposed to broader roles.

## Design Boundary

Developer commands are server-authorized tools for repeatable testing, not an administrator permission system. They must remain disabled on servers that accept untrusted players.
