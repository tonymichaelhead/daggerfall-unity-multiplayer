# DFMP Roadmap

This roadmap tracks the multiplayer architecture and milestones implemented on top of upstream Daggerfall Unity. It is deliberately scoped around a dedicated server for roughly 8-16 players. Scaling to 100+ players is a separate design and performance problem.

## Architectural Rules

- Upstream DFU files remain free of multiplayer logic.
- Additive hooks, when needed, live in `Assets/DFMP/Hooks/` and are documented in `HOOKS.md`.
- Networking, authority, persistence, and server bootstrap live in `Assets/DFMP/Runtime/`.
- Server-owned world and session state must never live on a player prefab.
- Client reports are inputs. The server validates and writes replicated state.
- Native Daggerfall world coordinates are authoritative. Unity scene positions are local presentation data because floating origin can rebase them.
- `Assets/DFMP/Runtime/` must not depend on edits to upstream DFU source, so the same assembly could ship inside this build or inside a mod bundle without redesign.

## Completed

### M0: Headless Dedicated Server Boot

Status: Complete.

- Command-line parsing for dedicated server settings.
- Headless startup avoids player, camera, audio, and UI assumptions.
- Dedicated server starts DFU in `StartMethods.Void` and maintains a heartbeat.
- Windows build subsystem post-processing supports batch-mode server execution.

Verification:

- Focused EditMode tests for command-line parsing.
- Batch-mode headless server smoke test at the configured tick rate.

### M0.5: Mirror/KCP Listener and Client Connection

Status: Complete.

- Dedicated KCP listener on configurable port.
- Client bootstrap and KCP connection lifecycle.
- Connect/disconnect logging and basic handshake behavior.

Verification:

- Headless server/client connection smoke test.

### M1: Server-Owned Game Time

Status: Complete.

- Server-owned `DFMPTimeState` publishes game time and time scale.
- Ready clients observe the time state without requiring a player network object.
- Clients apply replicated time state after spawn.

Verification:

- Time snapshot EditMode tests.
- Server/client smoke test confirms time-state spawn and client application.

### M2: Server-Owned Player Session State

Status: Complete.

- One server-owned `DFMPPlayerSessionState` per ready connection.
- Session state stores canonical Daggerfall coordinates, player identity, spawn confirmation, facing, and visual appearance metadata.
- Server assigns Daggerfall City as an initial exterior context.
- Client uses DFU's normal `RandomStartMarker` relocation path and acknowledges its final grounded coordinate.
- Save loads requeue the assignment after DFU restores saved position data.

Verification:

- EditMode tests for spawn lifecycle, acknowledgement validation, and Daggerfall City coordinate rules.
- Graphical clean-save smoke test confirms grounded Daggerfall City placement and reload behavior.

### M3: First Visible Multiplayer Presence

Status: In progress.

Complete slices:

- M3.1: Clients report native world coordinates at a low fixed rate. The server validates report rate, bounds, spawn confirmation, and displacement before mutating session state.
- M3.2: Clients render confirmed remote sessions in the same exterior map pixel with floating-origin-safe conversion.
- M3.3: Remote presentation includes distance culling, interpolation, and camera-facing labels.
- M3.4: Server-owned display names and facing yaw drive remote presentation.
- M3.5: Remote visuals use DFU's native `MobilePersonBillboard` rather than a player or enemy gameplay prefab.
- M3.6: Race, gender, outfit, and face metadata configure native avatar visuals. Unsupported native mobile-person races use a neutral visual fallback while their actual race remains in session state.

Remaining M3 work:

- M3.7: Derive moving/idle visual state from accepted authoritative movement and drive `MobilePersonBillboard.IsIdle`.
- M3.8: Add travel/respawn-aware movement rules and concise diagnostics for rejected reports.
- M3 close-out: Two graphical clients connect, spawn, see grounded named avatars, observe movement and facing, and hide remote avatars outside presentation scope.

Verification:

- Focused EditMode tests for spawn, position, appearance, and remote-presentation rules.
- Two-client graphical exterior smoke test.

## Upcoming

### M3.9: Global Text Chat

Status: Planned.

Implement one global MMO-style text channel for all connected players.

- Clients submit bounded chat input messages.
- Server validates message size, characters, sender session, and rate limit.
- Server broadcasts accepted messages to all ready clients.
- Client UI displays a scrolling global-channel history with sender display names.
- No private messages, party channels, persistence, moderation roles, or chat commands in this milestone.

Verification:

- EditMode tests for message sanitization, length limits, rate limiting, and sender/session validation.
- Two-client smoke test confirms accepted messages appear once and rejected messages are not broadcast.

### M4: World Context and Interest Management

- Server-managed exterior interest based on map pixels and native Daggerfall coordinates.
- Do not use Unity transform distance or Mirror's standard distance interest management because DFU floating origin invalidates it.
- Explicit exterior, building interior, and dungeon context state.
- Location identity and dungeon generation context for shared visibility.
- Safe transitions for doors, dungeon entry/exit, fast travel, death/respawn, save load, and reconnect.

Verification:

- EditMode tests for coordinate/context conversion and observer selection.
- Headless and graphical transition smoke tests.

### M5: Shared Server-Owned World State

- Weather synchronization.
- Persistent doors and loot state.
- Server persistence and restart recovery.
- Quest synchronization policy after core world-state reliability is established.

Verification:

- EditMode persistence and authority tests.
- Restart/reconnect smoke tests.

### M6: Gameplay Authority

- Server-authoritative vitals, damage, and combat intent validation.
- Server-owned enemy spawning, state, and AI.
- Inventory, equipment, trade, interaction, and loot authority.

Verification:

- EditMode tests for authority, message validation, lifecycle transitions, and persistence.
- Headless and graphical combat/inventory integration smoke tests.

### M7: Client Distribution and Launcher

Status: Planned. Not started.

Ship DFMP as a separate client application that reuses the player's existing Daggerfall data instead of replacing or modifying their Daggerfall Unity install. The DFMP client is a sibling of DFU in the same way DFU is a sibling of classic Daggerfall: another engine binary reading the same `arena2` data.

- Launcher locates the player's existing Daggerfall game files and writes `MyDaggerfallPath` into the DFMP client settings, using the same path resolution the dedicated server bootstrap already relies on.
- DFMP client ships as a portable install (`Portable.txt`) so settings, saves, keybinds, and mods live in its own `PortableAppdata` folder and never read or write DFU's persistent data folder.
- Launcher owns client version management and update integrity.
- Launcher provides a server list and launches the client with connect arguments.
- No game files are copied, moved, or patched. The player's vanilla DFU install keeps working side by side.

Verification:

- Launch a DFMP client against an unmodified Daggerfall install and confirm DFU's persistent data folder is untouched.
- Confirm launcher-supplied connect arguments reach the client bootstrap and establish a session.

### Optional: Mod-Packaged Client (Not Planned)

Status: Optional. Deliberately not scheduled.

A `.dfmod` distribution would let players join from their own DFU install with no second client binary. It is recorded here only to keep the option open, not as committed work.

- DFU's mod loader can load precompiled assemblies from a mod bundle, so `DFMP.Runtime` and its transport dependencies could ship as binaries.
- The hook layer would be replaced by runtime patching against upstream DFU methods, which requires vendoring a patching library. This is the entire cost of the route.
- Runtime patching fails silently when upstream changes behavior without changing signatures, unlike a rebase, which fails loudly. That risk is why this route is not the default.
- This route applies to the client only. The dedicated server needs build post-processing and boot control, so it remains a first-party build regardless.
- The architectural rule keeping `Assets/DFMP/Runtime/` free of upstream source dependencies is what keeps this option cheap. Preserve it even though the route is unscheduled.

## Delivery Standard

Each DFMP behavior change should add focused automated coverage when possible. Extract deterministic protocol or coordinate rules into pure helpers for EditMode tests. Use manual headless or graphical smoke tests only at DFU and Mirror integration boundaries, and record expected log evidence in the relevant implementation or change notes.
