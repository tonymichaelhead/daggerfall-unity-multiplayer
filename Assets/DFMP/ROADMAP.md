# DFMP Roadmap

DFMP is a customizable multiplayer framework for Daggerfall Unity. Server owners host dedicated servers, tune behavior through configuration, and eventually extend it through a scripting layer. Players join those servers with a matching client build.

The near-term target is a playable public beta for roughly 8-16 concurrent players, built so that growth toward ~100 players does not require an architectural rewrite.

## At a Glance

| # | Milestone | Status |
| --- | --- | --- |
| M0 | Headless dedicated server boots DFU in batch mode with command-line configuration. | Done |
| M0.5 | Mirror/KCP listener accepts client connections with a basic handshake. | Done |
| M1 | Server-owned game time replicates to clients instead of living on a player prefab. | Done |
| M2 | Server-owned player session state holds canonical Daggerfall coordinates and identity. | Done |
| M3 | Players see each other move as named, grounded avatars in the shared exterior world. | In progress |
| M4 | Global text chat, expanded server configuration, and a server-side event bus. | Planned |
| M5 | First-join character creation, account identity, whitelist, and server-side character persistence. | Planned |
| M6 | World context, location occupancy, interest management, and safe transitions. | Planned |
| M6.5 | Timeboxed spike: can the headless server host dungeon geometry for server-side AI? | Planned |
| M7 | Server-authoritative vitals and validated combat damage, with a PvP toggle. | Planned |
| M8 | Server-owned dungeon enemies with rosters, replication, AI, and kill credit. | Planned |
| — | **Public beta release.** | Planned |
| M9+ | Shared economy options, proximity and voice chat, shared quests, scripting layer. | Future |

## MVP Definition

The public beta target is a single complete loop:

> Join a server, create a character, spawn in Daggerfall city, see and chat with other players, enter a dungeon together, fight the same enemies, optionally fight each other when PvP is enabled, log off, and return later with the same character.

Systems are divided by a single rule: **replicate state when a desync between two co-located players would break immersion or be exploitable. Otherwise keep it personal to each client and document it.**

Server-owned in MVP: player presence and movement, global chat, game time and weather, character persistence, vitals, combat damage, PvP policy, dungeon enemies, location occupancy, and door state.

Personal (client-local) in MVP: wandering town citizens, static NPCs and shopkeepers, shop inventories and guild services, loot, and quests. Daggerfall's world geometry, dungeon layouts, and static flats are deterministic from game data, so they are never replicated.

## Architectural Rules

- Upstream DFU files remain free of multiplayer logic.
- Additive hooks, when needed, live in `Assets/DFMP/Hooks/` and are documented in `HOOKS.md`.
- Networking, authority, persistence, and server bootstrap live in `Assets/DFMP/Runtime/`.
- Server-owned world and session state must never live on a player prefab.
- Client reports are inputs. The server validates and writes replicated state.
- Native Daggerfall world coordinates are authoritative. Unity scene positions are local presentation data because floating origin can rebase them.
- Interest management keys on Daggerfall location identity and map pixels, never Unity transform distance.
- Server-tunable behavior belongs in `dfmp-server.json` from the moment it is implemented.
- Server-side gameplay events are raised on a single event bus so a future scripting layer binds to it rather than being retrofitted into finished systems.
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
- M3.7: Accepted authoritative movement reports derive a replicated moving/idle state and drive `MobilePersonBillboard.IsIdle`.
- M3.8: Spawn re-anchoring resets stale movement state, and rejected position reports expose concise server-side rejection reasons.

Remaining M3 work:

- M3 close-out: Two graphical clients connect, spawn, see grounded named avatars, observe movement and facing, and hide remote avatars outside presentation scope.

Verification:

- Focused EditMode tests for spawn, position, appearance, and remote-presentation rules.
- Two-client graphical exterior smoke test.

## Upcoming

### M4: Global Chat, Server Configuration, and Event Bus

Status: Planned.

One global MMO-style text channel, plus the configuration and event surfaces that later milestones build on.

- Clients submit bounded chat input messages.
- Server validates message size, characters, sender session, and rate limit.
- Server broadcasts accepted messages to all ready clients.
- Client UI displays a scrolling global-channel history with sender display names.
- Expand `dfmp-server.json` into a structured server configuration document covering server identity, connection limits, chat rate limits, and world rules.
- Introduce a server-side event bus raising `PlayerConnected`, `PlayerDisconnected`, `PlayerSpawned`, `ChatMessageReceived`, and `LocationEntered`. No scripting engine is bound in this milestone; the bus exists so later systems publish through it by default.

Out of scope: private messages, party or proximity channels, chat history persistence, moderation roles, and chat commands.

Verification:

- EditMode tests for message sanitization, length limits, rate limiting, and sender/session validation.
- EditMode tests for configuration parsing, defaults, and invalid-value handling.
- Two-client smoke test confirms accepted messages appear once and rejected messages are not broadcast.

### M5: Identity, Whitelist, and Character Persistence

Status: Planned.

Players create a character on first join and return to it on later sessions.

- Stable account identity per player, established at connect time.
- Optional whitelist gating connections for private and invite-only servers.
- Server stores each character as a **structured record** with explicit fields for identity, position and world context, vitals, attributes and skills, inventory and equipment, and progression. It is deliberately not an opaque DFU save blob, so individual fields can be validated as authority tightens.
- The record carries a schema version with a defined migration path, so beta characters survive later milestones that add fields.

#### Join Flow

The server resolves every connection down the same path, branching only on whether a character record already exists:

1. Client connects and presents its account identity.
2. Server checks the whitelist and connection limits, rejecting with a readable reason when refused.
3. Server looks up a stored character for that identity.
4. **No record (first join):** the client runs DFU's normal character creation UI. The resulting character is submitted to the server, which validates it against the server's creation rules, persists it, and assigns the configured starting location. Daggerfall city is the beta default; the starting location is server-configurable and later scriptable.
5. **Record exists (returning player):** the server sends the stored record, and the client restores it rather than showing creation or the DFU load-game UI.
6. In both cases the server assigns the spawn context, the client relocates through DFU's normal grounding path, and the client acknowledges its final coordinate. This reuses the M2 spawn-assignment and acknowledgement flow rather than adding a second one.
7. The character record is written back on a periodic autosave, on clean disconnect, and on server shutdown.

The client never chooses its own character or spawn point, and DFU's single-player title, save, and load menus are suppressed in multiplayer sessions.

#### Storage

- Persistence sits behind a narrow character-store interface so the backing store is a deployment choice, not an architectural one.
- Beta ships a file-backed store: one structured JSON record per character, written atomically. At beta player counts this is sufficient, trivially inspectable, and hand-editable while debugging.
- A SQL-backed store (SQLite for single-server, PostgreSQL for larger or multi-server deployments) is a later addition behind the same interface, motivated by concurrent access, query needs, and administrative tooling rather than by beta scale.
- Records are keyed by account identity and server world identity, so one player can hold separate characters on separate servers.

Trust model for beta: the server stores the character and the client simulates it, so a modified client can still misreport its own stats. This is an accepted beta limitation on a whitelisted server. Strict per-field validation is a later hardening pass, which the structured schema and the single store interface are designed to enable without a rewrite.

Verification:

- EditMode tests for record serialization, schema migration, whitelist decisions, join-flow branching, and reconnect resolution.
- Smoke test confirms a first-time join creates a character and spawns in the configured start location, and that the same character is restored after disconnect and after server restart.

### M6: World Context, Occupancy, and Interest Management

Status: Planned.

Everything the server needs to know where players are before it can own entities near them.

- Explicit exterior, building interior, and dungeon context state per session.
- Location identity and dungeon generation context tracked server-side.
- Server-managed occupancy: which players are currently in which location and dungeon block.
- Interest management derived from occupancy, so a client receives only entities in its own location. Bandwidth then scales with co-located players rather than total players.
- Safe transitions for doors, dungeon entry and exit, death and respawn, save load, and reconnect.
- **Time-advance policy.** Global game time is server-owned, so no single player may advance it:
  - Configurable server time scale, expressed as a multiplier where `1.0` is real time and `12.0` matches DFU's default. Owners choosing slow time get long, grounded days; owners choosing fast time get vanilla pacing.
  - Fast travel becomes a server-issued instant teleport with no time cost. Clients never self-relocate.
  - Loiter, rest-until-healed, and wait-until-dawn paths are removed as time-advance mechanisms.
  - Audit vampirism, lycanthropy, and guild-rank timers for hidden dependencies on player-driven time advancement.
- **Rest and healing replacement**, since rest no longer advances the clock:
  - Server-mediated rest in place. The server computes the vanilla heal and spell-point rates from the character's attributes and skills, then delivers that recovery over compressed real time with a configurable rate multiplier. Blocked while hostiles are present, and interruptible on damage or movement.
  - Renting an inn room grants an immediate full restore, preserving the vanilla gold sink.
  - Temple and guild paid restoration services continue to work unchanged.

Verification:

- EditMode tests for coordinate and context conversion, occupancy transitions, observer selection, time-scale math, and rest recovery rates.
- Headless and graphical transition smoke tests covering doors, dungeon entry, fast travel, death, and reconnect.

### M6.5: Spike — Headless Dungeon Geometry

Status: Planned. Timeboxed investigation, not a shipped feature.

Server-side enemy AI needs navigation, collision, and raycasts against real dungeon meshes, but the M0 headless path intentionally starts DFU in `StartMethods.Void` with world suppression. This spike answers whether the dedicated server can selectively instantiate dungeon geometry for occupied locations without cameras, audio, or UI, and at what CPU and memory cost per occupied dungeon.

The outcome gates the design of M7 and M8. If full geometry proves too expensive, the fallback is a simplified server-side enemy simulation running against a precomputed navigation representation.

Verification:

- Headless run instantiating one and several dungeons, with recorded startup time, frame cost, and memory footprint.
- Written recommendation and a decision recorded before M8 design begins.

### M7: Vitals and Combat Authority

Status: Planned.

- Server-authoritative health, spell points, and fatigue.
- Server-owned death and respawn.
- Clients submit damage *intent*; the server validates and applies it. All damage flows through a single server-side application chokepoint so validation can tighten in one place.
- Beta validation is deliberately loose: bounds, cooldown, range, and source-session sanity checks rather than full server-side combat simulation.
- PvP is a server configuration flag. When disabled, the server rejects player-versus-player damage at the same chokepoint.
- Event bus raises `PlayerDamaged`, `PlayerDied`, and `PlayerRespawned`.

Verification:

- EditMode tests for damage validation, PvP policy, death and respawn lifecycle, and vitals replication.
- Two-client graphical smoke test for PvE damage, PvP enabled, and PvP disabled.

### M8: Server-Owned Dungeon Enemies

Status: Planned.

- Shared persistent-world dungeons rather than per-party instances. Layouts are deterministic from game data, so only dynamic entities are replicated.
- The server populates a dungeon's enemy roster on first occupancy, seeded from location identity and a server world seed so rosters are reproducible and debuggable.
- Roster persists while the location is occupied and despawns on a configurable timer once empty.
- Server-owned enemy state, movement, and AI, replicated to occupants through M6 interest management.
- Kill credit and loot attribution routed through the server.
- **Personal loot.** Each player loots an independent copy, avoiding loot races and duplication exploits entirely.
- Event bus raises `EnemySpawned`, `EnemyDied`, and `LootGenerated`.

Verification:

- EditMode tests for roster seeding, spawn and despawn lifecycle, authority boundaries, and kill credit.
- Two-client graphical dungeon smoke test confirming both players see and fight the same enemies.

### Quest Policy for MVP

Quests remain **personal per player** rather than shared or disabled. Full quest-state synchronization is explicitly out of scope; the reference fork's approach demonstrated that it does not decompose cleanly.

- Each client runs its own quest state machine. Quest NPCs, items, dialogue, and journal are client-local, so two players may independently hold the same quest from the same NPC.
- Quest-spawned **enemies** are registered with the server as ordinary server-owned enemies tagged with an owner. Co-located players can see and fight them, and kill credit routes back to the quest owner. This delivers most of the perceived co-op value at a small fraction of full quest synchronization cost.
- Quest deadlines are suppressed by default for beta. The server disables quest timeout actions rather than editing upstream quest scripts, keeping the change configurable and rebasable.
- Configuration exposes a quest mode of `personal` or `disabled`, plus flags for quest-foe replication and deadline enforcement. A `shared` mode is reserved for a future milestone.

## Future Work

Beyond the public beta, in rough priority order:

- Additional chat channels, including proximity chat, and later proximity voice chat.
- Server scripting layer bound to the M4 event bus, with an event and command API for server owners.
- Shared quest progression for parties, building on the personal quest model.
- Shared and persistent world state options: doors, containers, loot mode selection, and shared economy.
- SQL-backed character and world persistence (SQLite, then PostgreSQL) behind the M5 store interface, plus restart recovery and administrative tooling.
- Strict authority: full server-side combat validation, inventory and equipment authority, trade, and anti-cheat.
- Scaling work toward 100+ concurrent players, including replication budgeting and load testing.
- Optional citizen and ambient NPC synchronization, only if it proves to matter in practice.

### M7: Client Distribution and Launcher

Status: Planned. Not started.

Ship DFMP as a separate client application that reuses the player's existing Daggerfall data instead of replacing or modifying their Daggerfall Unity install. The DFMP client is a sibling of DFU in the same way DFU is a sibling of classic Daggerfall: another engine binary reading the same `arena2` data.

- Launcher locates the player's existing Daggerfall game files and writes `MyDaggerfallPath` into the DFMP client settings, using the same path resolution the dedicated server bootstrap already relies on.
- DFMP client ships as a portable install (`Portable.txt`) so settings, saves, keybinds, and mod settings live in its own `PortableAppdata` folder and never read or write DFU's persistent data folder.
- Mod bundles are already install-local because `ModDirectory` defaults to `StreamingAssets/Mods`, so the DFMP client has its own mod folder independent of the player's DFU install.
- Net effect: separate settings, saves, keybinds, mod list, and mod configs. The only shared resource is the read-only `arena2` game data.
- Players do not inherit their existing DFU mods or keybinds. Keybind import is a possible later launcher convenience. Mod inheritance is explicitly not wanted.
- Server dictates the allowed mod set and the launcher provisions the client's mod folder to match, without touching the player's single-player setup.
- Launcher owns client version management and update integrity.
- Launcher provides a server list and launches the client with connect arguments.
- No game files are copied, moved, or patched. The player's vanilla DFU install keeps working side by side.

Verification:

- Launch a DFMP client against an unmodified Daggerfall install and confirm DFU's persistent data folder is untouched.
- Confirm the DFMP client resolves settings, saves, and mods from its own portable paths while a vanilla DFU install is present.
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
