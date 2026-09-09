# DFMP Roadmap

DFMP is a multiplayer framework for Daggerfall Unity. Players join a dedicated server using a matching client build.

Development is split into three phases with different definitions of "done".

**Phase 1 — Private Beta.** Everything needed to run one author-operated, whitelisted server that Discord testers can join and actually play on. The gameplay loop must be complete and stable: identity and login, character persistence, chat, a shared world, combat, and dungeon enemies. The build is not handed to other server owners in this phase, so a deep configuration surface, server-side scripting, a launcher, and public documentation are deliberately out of scope.

**Phase 2 — Public Release.** Everything needed for someone else to run their own DFMP server without the author's help: server-side scripting, a full configuration surface, admin and moderation tooling, client distribution and a launcher, operational hardening, and documentation.

**Phase 3 — Post-Release Expansion.** Perpetual, demand-driven work with no exit criteria: server-driven mod provisioning, proximity and voice chat, deeper shared world state, scale and trust hardening, and ecosystem features. Priority here comes from what live servers ask for, not from this document.

Phase 1 answers *"is this fun and stable enough to play with my friends?"*. Phase 2 answers *"can anyone else run this without me?"*. Phase 3 answers *"what do real servers need next?"*.

Target scale in both phases is roughly 8-16 concurrent players, built so growth toward ~100 does not require an architectural rewrite.

## At a Glance

### Phase 1 — Private Beta (author-hosted, whitelisted)

| # | Milestone | Status |
| --- | --- | --- |
| M0 | Headless dedicated server boots DFU in batch mode with command-line configuration. | Done |
| M0.5 | Mirror/KCP listener accepts client connections with a basic handshake. | Done |
| M1 | Server-owned game time replicates to clients instead of living on a player prefab. | Done |
| M2 | Server-owned player session state holds canonical Daggerfall coordinates and identity. | Done |
| M3 | Players see each other move as named, grounded avatars in the shared exterior world. | Done |
| M4 | Global text chat, baseline server configuration, and a server-side event bus. | Done |
| M5 | First-join character creation, account identity, whitelist, and server-side character persistence. | Done |
| M6 | World context, location occupancy, interest management, and safe transitions. | Planned |
| M6.5 | Timeboxed spike: can the headless server host dungeon geometry for server-side AI? | Planned |
| M7 | Server-authoritative vitals and validated combat damage, with a PvP toggle. | Planned |
| M8 | Server-owned dungeon enemies with rosters, replication, AI, and kill credit. | Planned |
| M9 | Tester client build, minimal ops, and beta stability pass. | Planned |
| — | **Phase 1 exit: private beta server live for Discord testers.** | Planned |

### Phase 2 — Public Release (other people host their own servers)

| # | Milestone | Status |
| --- | --- | --- |
| R1 | Full server configuration surface, validation, and documented defaults. | Planned |
| R2 | Server-side scripting layer bound to the event bus. | Planned |
| R3 | Admin, moderation, and chat command tooling. | Planned |
| R4 | Client distribution and launcher. | Planned |
| R5 | Operational hardening: backups, restart recovery, logging, metrics. | Planned |
| R6 | Documentation and release packaging. | Planned |
| — | **Phase 2 exit: public release for third-party server owners.** | Planned |

### Phase 3 — Post-Release Expansion (perpetual, demand-driven)

| # | Milestone | Status |
| --- | --- | --- |
| P-MOD | Server-supplied mod manifest with automatic client download, enable, and configuration. | Future |
| P-VOICE | Proximity text chat, spatialized proximity voice, and party channels. | Future |
| P-WORLD | Shared quests, shared world state and economy, ambient NPC sync. | Future |
| P-SCALE | SQL persistence, strict authority and anti-cheat, 100+ player scaling. | Future |
| P-COMMUNITY | Server browser, expanded scripting API, script sharing, friends and invites. | Future |

## Phase 1 Gameplay Target

Phase 1 is complete when a tester can run this loop end to end on the author's server:

> Join a server, create a character, spawn in Daggerfall city, see and chat with other players, enter a dungeon together, fight the same enemies, optionally fight each other when PvP is enabled, log off, and return later with the same character.

Systems are divided by a single rule: **replicate state when a desync between two co-located players would break immersion or be exploitable. Otherwise keep it personal to each client and document it.**

Server-owned in Phase 1: player presence and movement, global chat, game time and weather, character persistence, vitals, combat damage, PvP policy, dungeon enemies, location occupancy, and door state.

Personal (client-local) in Phase 1: wandering town citizens, static NPCs and shopkeepers, shop inventories and guild services, loot, and quests. Daggerfall's world geometry, dungeon layouts, and static flats are deterministic from game data, so they are never replicated.

Explicitly deferred to Phase 2, even though it would be tempting to build early: server-side scripting, an exhaustive configuration surface, in-game admin and moderation commands, a launcher and auto-updater, and player-facing documentation. Phase 1 configuration stays at whatever the author needs to run one server, and operational tasks may be manual.

## Architectural Rules

- Upstream DFU files remain free of multiplayer logic.
- Additive hooks, when needed, live in `Assets/DFMP/Hooks/` and are documented in `HOOKS.md`.
- Networking, authority, persistence, and server bootstrap live in `Assets/DFMP/Runtime/`.
- Server-owned world and session state must never live on a player prefab.
- Client reports are inputs. The server validates and writes replicated state.
- Native Daggerfall world coordinates are authoritative. Unity scene positions are local presentation data because floating origin can rebase them.
- Interest management keys on Daggerfall location identity and map pixels, never Unity transform distance.
- Server-tunable behavior belongs in `dfmp-server.json` from the moment it is implemented. Phase 1 only has to expose the values the author needs; R1 exposes the rest, so values must be read from a single configuration object rather than scattered constants.
- Server-side gameplay events are raised on a single event bus so a future scripting layer binds to it rather than being retrofitted into finished systems.
- Phase 1 may defer Phase 2 features, but must not make them expensive. Deferring polish is fine; hard-coding an assumption that R1-R6 would have to unwind is not.
- `Assets/DFMP/Runtime/` must not depend on edits to upstream DFU source, so the same assembly could ship inside this build or inside a mod bundle without redesign.

## Phase 1 Milestones: Foundation (Complete)

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

Status: Complete.

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

## Phase 1 Milestones: M4 Onward

### M4: Global Chat, Baseline Server Configuration, and Event Bus

Status: Complete.

One global MMO-style text channel, plus the configuration and event surfaces that later milestones build on.

- Clients submit bounded chat input messages.
- Server validates message size, characters, sender session, and rate limit.
- Server broadcasts accepted messages to all ready clients.
- Client UI displays a scrolling global-channel history with sender display names.
- Expand `dfmp-server.json` into a structured server configuration document covering server identity, connection limits, chat rate limits, and world rules. This is the baseline surface only; the exhaustive, documented configuration surface for third-party owners is R1.
- Introduce a server-side event bus raising `PlayerConnected`, `PlayerDisconnected`, `PlayerSpawned`, `ChatMessageReceived`, and `LocationEntered`. No scripting engine is bound in this milestone; the bus exists so later systems publish through it by default and R2 can bind to it rather than retrofitting finished systems.

Out of scope: private messages, party or proximity channels, chat history persistence, moderation roles, and chat commands.

Verification:

- EditMode tests for message sanitization, length limits, rate limiting, and sender/session validation.
- EditMode tests for configuration parsing, defaults, and invalid-value handling.
- Two-client smoke test confirms accepted messages appear once and rejected messages are not broadcast.

### M5: Identity, Whitelist, and Character Persistence

Status: Complete.

Players create a character on first join and return to it on later sessions.

- Stable account identity per player, established at connect time.
- Optional whitelist gating connections for private and invite-only servers.
- Server stores each character as a **structured record** with explicit fields for identity, position and world context, vitals, attributes and skills, inventory and equipment, and progression. It is deliberately not an opaque DFU save blob, so individual fields can be validated as authority tightens.
- Progression is stored as Daggerfall models it. There are no experience points; level derives from the starting and current level-up skill sums, so the record persists the starting sum and the client recomputes the current sum from restored skills.
- The character's class is persisted as a serialized `DFCareer`, which covers custom classes as well as the standard eighteen. Without it a returning client falls back to DFU's default Mage career, which silently drives the wrong level-up skill set, magicka pool, tolerances, and class advantages.
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
- Configurable starting-location policy: server owners can choose a default city center, a named gate or start marker, explicit Daggerfall world coordinates, or a later scripted spawn rule. The server assigns the location and clients never choose it.
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

### M9: Beta Server Launch Readiness

Status: Planned.

The smallest amount of non-gameplay work required to actually put testers on the author's server. Everything here is intentionally minimal, because the polished versions are Phase 2.

- Tester client build: a versioned, zipped portable client that testers download directly, with the server address supplied by a config file or command-line argument. No launcher, no auto-update, no server browser.
- Client and server version handshake, so a mismatched tester build is rejected at connect with a readable reason instead of desyncing.
- Whitelist administered by hand, out of band via Discord, using the M5 whitelist store.
- Manual operations are acceptable: file-copy character backups, restart by hand, read logs on disk.
- Beta stability pass: run the server continuously for a multi-day soak, watch for leaks, unbounded growth in session or roster state, and reconnect edge cases.
- A short tester-facing setup note and a bug reporting channel. This is not the Phase 2 documentation set.

Out of scope: launcher, auto-update, server browser, mod provisioning, in-game admin commands, metrics dashboards.

Verification:

- A tester who has never run DFMP can install the client, connect, create a character, play, disconnect, and return to the same character.
- Multi-day soak run with no unbounded resource growth and no manual intervention required to keep the server up.

### Quest Policy for Phase 1

Quests remain **personal per player** rather than shared or disabled. Full quest-state synchronization is explicitly out of scope; the reference fork's approach demonstrated that it does not decompose cleanly.

- Each client runs its own quest state machine. Quest NPCs, items, dialogue, and journal are client-local, so two players may independently hold the same quest from the same NPC.
- Quest-spawned **enemies** are registered with the server as ordinary server-owned enemies tagged with an owner. Co-located players can see and fight them, and kill credit routes back to the quest owner. This delivers most of the perceived co-op value at a small fraction of full quest synchronization cost.
- Quest deadlines are suppressed by default for beta. The server disables quest timeout actions rather than editing upstream quest scripts, keeping the change configurable and rebasable.
- Configuration exposes a quest mode of `personal` or `disabled`, plus flags for quest-foe replication and deadline enforcement. A `shared` mode is reserved for a future milestone.

## Phase 2 Milestones: Public Release

Phase 2 turns a server the author can run into a product other people can run. Nothing here changes the core gameplay loop; it changes who is capable of operating it.

Phase 2 does not begin until Phase 1 has been running a live beta long enough to know which knobs owners will actually want. Guessing the configuration and scripting surface before the beta produces the wrong surface.

### R1: Full Server Configuration Surface

Status: Planned.

- Promote every hard-coded Phase 1 constant that a server owner would reasonably want to change into `dfmp-server.json`, organized into coherent sections: identity and listing, connection and whitelist, chat, world and time, rest and travel, PvP and combat, enemies and loot, quests, and persistence.
- Schema versioning and migration for the config document itself, matching the approach used for character records.
- Startup validation that rejects malformed values with a specific, actionable message and exits, rather than silently falling back to defaults.
- Config reload for the subset of values that are safe to change on a running server, with the rest clearly marked restart-only.
- A documented, fully commented reference config shipped with the build.

Verification:

- EditMode tests for parsing, defaults, invalid-value handling, migration, and reload safety classification.
- A server started from the shipped reference config runs without warnings.

### R2: Server-Side Scripting Layer

Status: Planned.

The M4 event bus exists precisely so this milestone is a binding exercise rather than a rewrite.

- An embedded scripting runtime loading scripts from a server-side scripts folder.
- Event API: scripts subscribe to bus events (`PlayerConnected`, `PlayerSpawned`, `ChatMessageReceived`, `LocationEntered`, `PlayerDamaged`, `PlayerDied`, `PlayerRespawned`, `EnemySpawned`, `EnemyDied`, `LootGenerated`).
- Command API: scripts act on the world through a narrow, validated surface (send chat, teleport a player, adjust vitals, spawn or despawn enemies, grant items or gold, kick or ban, read and write per-character script data).
- Handlers may veto or modify eligible events, with the veto points defined explicitly rather than every event being interceptable.
- Sandboxing and error isolation: a faulty script is disabled with a logged error and never takes the server down. Execution time budgets prevent a script from stalling the tick.
- Per-character and per-server script key-value storage persisted through the M5 store interface.
- Hot reload of scripts on a running server.

Verification:

- EditMode tests for event dispatch, veto semantics, command validation, error isolation, and time budgeting.
- A sample script set shipped as documentation-by-example, exercised in a smoke test.

### R3: Admin, Moderation, and Chat Commands

Status: Planned.

- Role and permission model: owner, admin, moderator, player, with permissions granted per command.
- In-game chat command framework, with commands registerable by both the core and R2 scripts.
- Core moderation commands: kick, ban, unban, mute, whitelist add and remove, teleport, and player lookup.
- Audit log of moderation actions, keyed to account identity.
- Additional chat channels beyond the single global channel, at minimum a staff channel and private messages.

Verification:

- EditMode tests for permission resolution, command parsing, and moderation state transitions.
- Smoke test confirms an unprivileged player cannot invoke privileged commands.

### R4: Client Distribution and Launcher

Status: Planned.

Ship DFMP as a separate client application that reuses the player's existing Daggerfall data instead of replacing or modifying their Daggerfall Unity install. The DFMP client is a sibling of DFU in the same way DFU is a sibling of classic Daggerfall: another engine binary reading the same `arena2` data.

- Launcher locates the player's existing Daggerfall game files and writes `MyDaggerfallPath` into the DFMP client settings, using the same path resolution the dedicated server bootstrap already relies on.
- DFMP client ships as a portable install (`Portable.txt`) so settings, saves, keybinds, and mod settings live in its own `PortableAppdata` folder and never read or write DFU's persistent data folder.
- Mod bundles are already install-local because `ModDirectory` defaults to `StreamingAssets/Mods`, so the DFMP client has its own mod folder independent of the player's DFU install.
- Net effect: separate settings, saves, keybinds, mod list, and mod configs. The only shared resource is the read-only `arena2` game data.
- Players do not inherit their existing DFU mods or keybinds. Keybind import is a possible later launcher convenience. Mod inheritance is explicitly not wanted.
- Server dictates the allowed mod set and the launcher provisions the client's mod folder to match, without touching the player's single-player setup. R4 only has to detect a mismatch and tell the player what is required; fully automatic download, enable, and configuration is P-MOD.
- Launcher owns client version management and update integrity.
- Launcher provides a server list and launches the client with connect arguments.
- No game files are copied, moved, or patched. The player's vanilla DFU install keeps working side by side.

Verification:

- Launch a DFMP client against an unmodified Daggerfall install and confirm DFU's persistent data folder is untouched.
- Confirm the DFMP client resolves settings, saves, and mods from its own portable paths while a vanilla DFU install is present.
- Confirm launcher-supplied connect arguments reach the client bootstrap and establish a session.

### R5: Operational Hardening

Status: Planned.

The work that separates "the author babysits it" from "a stranger runs it on a rented box".

- Scheduled character and world state backups with retention, plus a documented restore path.
- Crash and restart recovery: clean shutdown persistence, and recovery of in-flight state after an unclean stop.
- Structured server logging with levels and rotation, so log files do not grow without bound.
- Operational metrics: player count, tick time, bandwidth, replication volume, and rejection counts, exposed for basic monitoring.
- Server-side rate limiting and abuse protection on every client-submitted message type.
- Documented deployment shapes for Windows and Linux servers, including running as a service.

Verification:

- Kill and restart a loaded server, confirming characters and world state survive.
- Restore from a backup into a clean deployment.

### R6: Documentation and Release Packaging

Status: Planned.

- Server owner guide: install, configure, run, whitelist, moderate, back up, and update.
- Scripting reference for the R2 event and command API, with worked examples.
- Player guide: obtaining the client, connecting, and known differences from single-player DFU.
- Contributor documentation covering the three-layer architecture, the hook policy, and the rebase workflow.
- Versioned release artifacts for server, client, and launcher, with a changelog and a compatibility statement tying client and server versions together.

Verification:

- A person who has never seen the project stands up a working server from the documentation alone, without asking the author a question.

## Phase 3 Milestones: Post-Release Expansion

Phase 3 is perpetual. It has no exit criteria and no fixed order, because after public release the priority order should be driven by what live servers and their players actually ask for rather than by a plan written before launch.

Items graduate from this list into scheduled work when there is real demand, and items may be dropped outright if the beta and release show nobody wants them. Anything here that would change core authority boundaries must respect the same architectural rules as Phase 1 and Phase 2.

### P-MOD: Server-Driven Mod Provisioning

Status: Future. Strongest candidate to be scheduled first.

R4 establishes that the server dictates the allowed mod set and the launcher matches it. This milestone makes that fully automatic, so joining a modded server never requires a player to hunt down downloads, match versions, or fix load order by hand.

- Server publishes a **mod manifest**: an ordered list of required and optional mods, each with an identifier, version, content hash, load order position, and mod-specific settings.
- Client compares the manifest against its local mod cache and resolves the difference before connecting.
- Missing or outdated mods are downloaded automatically, verified against the manifest hash, and installed into the client's own portable mod folder. The player's separate single-player DFU install is never touched.
- Client applies the server's load order and per-mod settings for the duration of that session, then restores its own configuration when connecting elsewhere. Per-server mod profiles mean a player can move between differently modded servers without manual reconfiguration.
- Content is served either from the game server itself or from an owner-configured content host, since shipping large mod payloads over the game transport is a poor default.
- Mods are validated for multiplayer compatibility, and mods known to conflict with server authority are rejected with a clear reason rather than silently desyncing.
- Mods that affect gameplay state must be classified as server-relevant or purely cosmetic. Cosmetic mods stay a client choice; server-relevant mods are enforced from the manifest.
- Cache management, integrity re-verification, and reclaiming space from unused mod profiles.

Open questions to settle before scheduling: hosting and bandwidth cost for owners, redistribution permission from mod authors, and how far a mod may alter gameplay before the server must simulate it rather than trust it.

### P-VOICE: Proximity and Voice Chat

Status: Future.

- Proximity text chat scoped by distance and world context, building on the M6 occupancy and interest data rather than Unity transform distance.
- Proximity voice chat with spatialized falloff, push-to-talk and voice activation, and per-player mute.
- Party and guild voice channels independent of position.
- Server-side voice policy: enable or disable, quality and bandwidth ceilings, and moderation controls including server mute.

### P-WORLD: Deeper Shared World State

Status: Future.

- Shared quest progression for parties, building on the personal quest model rather than replacing it.
- Shared and persistent world state options: doors, containers, loot mode selection, and a shared economy with server-owned shop inventories and prices.
- Optional citizen and ambient NPC synchronization, only if it proves to matter in practice.
- Server-owned weather and seasonal events beyond the Phase 1 time and weather baseline.

### P-SCALE: Scale, Storage, and Trust

Status: Future.

- SQL-backed character and world persistence (SQLite, then PostgreSQL) behind the M5 store interface, plus administrative tooling.
- Strict authority: full server-side combat validation, inventory and equipment authority, validated trade, and anti-cheat.
- Scaling work toward 100+ concurrent players, including replication budgeting, tick profiling, and load testing.
- Multi-server deployments sharing an account identity and character store.

### P-COMMUNITY: Ecosystem

Status: Future.

- Public server browser and listing service.
- Richer scripting API surface driven by what server owners hit the limits of in R2.
- A script and plugin sharing ecosystem, so owners exchange systems rather than each rebuilding them.
- Player-facing quality of life: friends lists, invites, and joining a friend's session directly.

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
