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

## Design Boundary

The command is intentionally self-targeted and developer-only. It does not accept a client-selected target, destination, time, or effect payload. Future server actions should use the same validated service boundary:

- R2 scripts call server actions rather than mutating player objects directly.
- R3 admin/GM commands send authorized server requests and use the same action implementation.
- The future R3 GM menu should expose an `Advance World Time` action using this same server implementation, with a bounded amount input, role/permission checks, confirmation for large jumps, and audit logging.

All future world-control actions should return structured rejection reasons and produce an audit event before they are exposed to broader roles.