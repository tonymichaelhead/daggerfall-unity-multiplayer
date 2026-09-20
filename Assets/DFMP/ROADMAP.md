# DFMP Roadmap

DFMP is a multiplayer framework for Daggerfall Unity. Players join a dedicated server using a matching client build.

Development is split into three phases with different definitions of "done".

**Phase 1 — Private Beta.** Everything needed to run one author-operated, whitelisted server that Discord testers can join and actually play on. The gameplay loop must be complete and stable: identity and login, character persistence, chat, a shared world, combat, and dungeon enemies. The build is not handed to other server owners in this phase, so a deep configuration surface, server-side scripting, and public documentation are deliberately out of scope. A minimal launcher is the single exception, pulled forward into M8.5, because testers cannot be handed a build without a way to launch the client and complete server-side Discord login.

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
| M6 | World context, location occupancy, interest management, and safe transitions. | Done |
| M6.5 | Timeboxed spike: can the headless server host dungeon geometry for server-side AI? | Done |
| M7 | Server-authoritative vitals and validated combat damage, with a PvP toggle. | Done |
| M8 | Server-owned dynamic world enemies, with dungeon enemies as the first vertical slice. | In-Progress |
| M8.5 | Account authentication, per-server Discord connect, account roles, and the client launcher. | In-Progress |
| M8.6 | Post-auth character select, multi-character persistence, and owner-gated create/delete. | In-Progress |
| M8.7 | Client-owned quest progression with persisted quest machines and owner-scoped, server-owned quest foes. | Done |
| M9 | Tester client build, minimal ops, and beta stability pass. | Planned |
| — | **Phase 1 exit: private beta server live for Discord testers.** | Planned |

### Phase 2 — Public Release (other people host their own servers)

| # | Milestone | Status |
| --- | --- | --- |
| R0 | Optional public master server list (listing only; not a hosted login service). | Planned |
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
| P-QUEST-FOES | Spike and implement a reliable server-owned quest-enemy model, if justified by play evidence. | Superseded by M8.7 |
| P-SCALE | SQL persistence, strict authority and anti-cheat, 100+ player scaling. | Future |
| P-COMMUNITY | Server browser, expanded scripting API, script sharing, friends and invites. | Future |

## Phase 1 Gameplay Target

Phase 1 is complete when a tester can run this loop end to end on the author's server:

> Join a server, create a character, spawn in Daggerfall city, see and chat with other players, enter a dungeon together, fight the same enemies, optionally fight each other when PvP is enabled, log off, and return later with the same character.

Systems are divided by a single rule: **replicate state when a desync between two co-located players would break immersion or be exploitable. Otherwise keep it personal to each client and document it.**

Server-owned in Phase 1: player presence and movement, global chat, game time and weather, character persistence, vitals, combat damage, PvP policy, dynamic world enemies (dungeon enemies first), quest foes and quest-machine persistence, location occupancy, and door state.

Personal (client-local) in Phase 1: wandering town citizens, static NPCs and shopkeepers, shop inventories and guild services, loot, and quest progression itself — each character runs its own quest machine, and only the physical quest foes are server-owned (M8.7). Daggerfall's world geometry, dungeon layouts, and static flats are deterministic from game data, so they are never replicated.

Explicitly deferred to Phase 2, even though it would be tempting to build early: server-side scripting, an exhaustive configuration surface, in-game admin and moderation commands, launcher auto-update (check for new DFMP releases on open, then download and install), tes3mp-style username/password as a player-facing auth mode, mod provisioning, and player-facing documentation. Phase 1 configuration stays at whatever the author needs to run one server, and operational tasks may be manual. DFMP does not operate a central identity or OAuth service in any phase.

## Architectural Rules

- Upstream DFU files remain free of multiplayer logic.
- **Replicate native DFU behavior from the start.** When a system has a native DFU implementation, port or reproduce that behavior rather than inventing a simplified stand-in. Invented mechanics with no upstream analogue (leashes, tethers, curated rosters, arbitrary radii) are a last resort, not a default starting point.
- **Placeholder implementations require explicit approval.** If native behavior genuinely cannot be reproduced in a slice, stop and get sign-off before landing a stand-in, then record it in the roadmap with the native behavior it defers and the milestone that replaces it. Do not let a scaffold quietly become the shipped behavior.
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
- Client UI displays a bounded, scrolling global-channel history with sender display names, F9 text entry, and fading lower-left HUD messages.
- Server-authored welcome, MOTD, player-joined, and player-left messages share the chat presentation with visually distinct status styling.
- Expand `dfmp-server.json` into a structured server configuration document covering server identity, connection limits, chat rate limits, and world rules. This is the baseline surface only; the exhaustive, documented configuration surface for third-party owners is R1.
- Introduce a server-side event bus raising `PlayerConnected`, `PlayerDisconnected`, `PlayerSpawned`, `ChatMessageReceived`, and `LocationEntered`. No scripting engine is bound in this milestone; the bus exists so later systems publish through it by default and R2 can bind to it rather than retrofitting finished systems.

Out of scope: private messages, party or proximity channels, chat history persistence, moderation roles, and chat commands. These are owned by R3 and P-VOICE.

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

The server resolves every connection down the same path, then branches on stored characters and the owner-gated select step:

1. Client connects and presents its account identity.
2. Server checks the whitelist and connection limits, rejecting with a readable reason when refused.
3. Server looks up stored characters for that identity.
4. **No record (first join), or the player chose New Character:** the client runs DFU's normal character creation UI. The resulting character is submitted to the server, which validates it against the server's creation rules, persists it, and assigns the configured starting location. Daggerfall city is the beta default; the starting location is server-configurable and later scriptable. The server does not persist a placeholder record before creation finishes.
5. **Record exists (returning player), or the player selected an existing character:** the server sends the stored record, and the client restores it rather than showing creation or the DFU load-game UI.
6. When character select UI is enabled, that choice happens after auth and before Ready/spawn. When it is disabled, zero characters auto-start creation and one or more characters auto-load the most recently played record.
7. In both bound cases the server assigns the spawn context, the client relocates through DFU's normal grounding path, and the client acknowledges its final coordinate. This reuses the M2 spawn-assignment and acknowledgement flow rather than adding a second one.
8. The character record is written back on a periodic autosave, on clean disconnect, and on server shutdown.

The client never chooses its spawn point. Which stored character to play is a server-gated select step: owners can show a character management UI, or skip it and auto-create or auto-load the most recently played character. DFU's single-player title, save, and load menus are suppressed in multiplayer sessions.

#### Storage

- Persistence sits behind a narrow character-store interface so the backing store is a deployment choice, not an architectural one.
- Beta ships a file-backed store: one structured JSON record per character, written atomically. At beta player counts this is sufficient, trivially inspectable, and hand-editable while debugging.
- A SQL-backed store (SQLite for single-server, PostgreSQL for larger or multi-server deployments) is a later addition behind the same interface, motivated by concurrent access, query needs, and administrative tooling rather than by beta scale.
- Records are keyed by account identity, server world identity, and character id, so one player can hold separate characters on the same server and on separate servers. When character select UI is disabled, join auto-loads the most recently played character for that account and world.

Trust model for beta: the server stores the character and the client simulates it, so a modified client can still misreport its own stats. This is an accepted beta limitation on a whitelisted server. Strict per-field validation is a later hardening pass, which the structured schema and the single store interface are designed to enable without a rewrite.

Verification:

- EditMode tests for record serialization, schema migration, whitelist decisions, join-flow branching, and reconnect resolution.
- Smoke test confirms a first-time join creates a character and spawns in the configured start location, and that the same character is restored after disconnect and after server restart.

### M6: World Context, Occupancy, and Interest Management

Status: Complete.

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
  - Vampirism transformation is implemented as a server-owned live-session transition: the server advances time, selects the cemetery, and the client applies transformed state after relocation.
  - Vampirism persistence across reconnect, restart, and character restore is explicitly deferred to Phase 2.
  - Lycanthropy transformation and its hidden timers are explicitly deferred to Phase 2; M6 does not intercept, synchronize, or partially implement lycanthropy.
- **Rest and healing replacement**, since rest no longer advances the clock:
  - **Default policy:** rest, rest-until-healed, and loiter are disabled (`Rest.Policy = Disabled`). Clients never advance server time through these actions.
  - **Delivered `Rest.Policy = ServerManaged`:** timed rest and rest-until-healed run as vanilla DFU rest (hour prompt, vitals, medical skill, sleep-end, inn rented-hour countdown, interruptible) without advancing server or client world time. Loiter stays disabled. Nearby existing server-owned enemies can still interrupt rest; random rest-time encounter spawns are not added.
  - Renting an inn room, and temple or guild paid restoration services, remain separate restoration paths and do not advance global time.

  M6 closeout notes:

  - Exterior, building-interior, and dungeon contexts are persisted per character and tracked in server occupancy.
  - Door entry and exit, dungeon entry and exit, fast travel, save load, reconnect, vampirism transformation, and death respawn use server-issued transition assignments with validated acknowledgements.
  - Saved tavern anchors are recorded on confirmed inn entry. M6 intentionally falls back to the configured exterior starting location during death respawn rather than attempting an invalid interior teleport; server-issued saved-interior reopening for **death respawn** remains owned by P-WORLD.
  - **Reconnect interior restore (post-M6 follow-up):** logging off inside a building or dungeon persists interior-local pose and building exterior-door reopen data (character schema v7). Returning players receive a `Reconnect` transition that reopens the interior via native `StartBuildingInterior` / `StartDungeonInterior` and snaps to the saved pose. Records missing reopen payload still fall back to exterior at the saved world coordinates.
  - Death handling is server-owned and no-wipe: duplicate or pending reports are rejected, pre-spawn deaths are not intercepted on the client, stale acknowledgements are rejected, and a pending death respawn is finalized before disconnect cleanup.
  - Vampirism is a live-session transformation only. Persistence across reconnect, restart, and character restore remains explicitly deferred to Phase 2. Lycanthropy remains fully deferred.
  - **`Rest.Policy = ServerManaged` is delivered.** Timed rest and rest-until-healed run as vanilla DFU rest without advancing shared world time. Loiter remains disabled. Policy is replicated at runtime via `DFMPWorldSettings` and `DFMPNetworkServer.SetRestPolicy` so an R3 admin/GM menu can toggle it later. Random rest-time encounter spawns and the admin-menu widget remain out of scope.

Verification:

- EditMode tests for coordinate and context conversion, occupancy transitions, observer selection, time-scale math, and rest recovery rates.
- Focused EditMode coverage includes spawn and transition acknowledgement validation, respawn-anchor selection, character persistence, join/reconnect resolution, and death-respawn lifecycle policy.
- Graphical transition smoke tests covered doors, dungeon entry and exit, fast travel, reconnect, death respawn, repeated death, and disconnect during a pending death respawn. Expected evidence includes accepted `DeathRespawn` assignments, validated acknowledgements, restored vitals, and `Finalized pending death respawn on disconnect` before disconnect cleanup.
- Vampirism transformation smoke evidence covers server-owned time advancement, cemetery relocation, client effect application, and transition acknowledgement. Vampirism persistence is not an M6 acceptance criterion.
- Rest smoke: with `ServerManaged`, timed rest and rest-until-healed restore vitals and medical/level-up while the world clock stays unchanged; nearby existing enemies can break rest; loiter is refused; `Disabled` blocks rest with an in-game message.

### M6.5: Spike — Headless Dungeon Geometry

Status: Complete. Timeboxed investigation, not a shipped feature.

Server-side enemy AI needs navigation, collision, and raycasts against real dungeon meshes, but the M0 headless path intentionally starts DFU in `StartMethods.Void` with world suppression. This spike answers whether the dedicated server can selectively instantiate dungeon geometry for occupied locations without cameras, audio, or UI, and at what CPU and memory cost per occupied dungeon.

The outcome informed M7 and is an M8 prerequisite. If full geometry proves too expensive, M8 retains a simplified server-side enemy simulation running against a precomputed navigation representation.

Verification:

- Headless run instantiating one and several dungeons, with recorded startup time, frame cost, and memory footprint.
- Native DFU geometry generated one dungeon with 5 blocks in 413 ms and three dungeons with 44 blocks in 1,206 ms; the headless server remained near 30 FPS after both runs.
- Written recommendation and caveats are recorded in [Dungeon Geometry Spike](DUNGEON_GEOMETRY_SPIKE.md). Native geometry is viable as a server-hosting foundation; its action-door, audio, teardown, and enemy-import concerns are owned by M8.

### M7: Vitals and Combat Authority

Status: Complete.

- Server-authoritative health, spell points, and fatigue.
- Server-owned death and respawn.
- Clients submit damage *intent*; the server validates and applies it. All damage flows through a single server-side application chokepoint so validation can tighten in one place.
- Beta validation is deliberately loose: bounds, cooldown, range, and source-session sanity checks rather than full server-side combat simulation.
- **Client-local quest PvE exception. Superseded by M8.7 (2026-09-20).** Quest foes are now server-owned, so quest damage arrives through the ordinary server-originated path and no real quest foe uses this trust exception any more. The `LocalQuestPve` source kind and its owner-only validation remain in the chokepoint for developer commands and as the fallback classification; if anything ever reports it in normal play, that is a quest foe that failed to register server-side. Original rule, retained for context: a client may report damage from its own local quest enemy only against its own character, under the same numeric bounds, rate limits, and source-session ownership checks, and never against another player.
- PvP is a server configuration flag. When disabled, the server rejects player-versus-player damage at the same chokepoint.
- Event bus raises `PlayerDamaged`, `PlayerDied`, and `PlayerRespawned`.
- **Authoritative vital restoration is not damage.** Applying a persisted join snapshot or a server respawn snapshot must preserve the stored vital values without triggering DFU damage flash, pain audio, or other damage presentation. Only an accepted damage application may produce damage feedback.

#### M7 Implementation Order

M7 is implemented as a sequence of vertical slices. The first end-to-end combat slice is player-versus-player combat because existing synchronized player sessions provide visible source and target actors, authoritative positions, world contexts, and a deterministic two-client smoke-test surface.

1. **PvP vertical slice.** Complete client damage-intent production, server validation, PvP enabled/disabled policy, same-context and range checks, cooldown and rate limits, authoritative vital replication, persistence, death/respawn integration, and combat lifecycle events. All damage continues through the source-agnostic server chokepoint.
2. **Local quest PvE exception.** Reuse the same chokepoint for client-local quest enemies. Restrict the target to the submitting player, retain bounded and rate-limited validation, and keep the source explicitly beta-trusted and non-shared. *(Delivered, then superseded by M8.7's server-owned quest foes.)*
3. **Server-owned dynamic world enemies.** Continue into M8 only after the PvP damage, vital replication, death/respawn, and policy-toggle paths are proven. Dungeon enemies are the first provider and vertical slice; wilderness encounters, city/night spawns, and future mod-provided encounters must fit the same server-owned entity, activation, replication, kill-credit, and loot boundaries. Enemy entities, AI, attack timing, navigation, kill credit, and loot remain M8 concerns and must submit server-originated damage through the existing chokepoint rather than create a second combat path.

Verification:

- EditMode tests for damage validation, the owner-only local quest PvE path, PvP policy, death and respawn lifecycle, and vitals replication.
- Two-client graphical smoke test first proves PvP damage with PvP enabled and disabled, then covers the owner-only local quest PvE exception. Server-owned dungeon enemy combat is verified separately in M8.
- Reconnect and respawn smoke coverage confirms that persisted low health restores without a damage-like visual or audio effect, while accepted damage still produces normal feedback.

### M8: Server-Owned Dynamic World Enemies

Status: In-Progress.

- Dungeon enemies are the first provider and vertical slice: shared persistent-world dungeons rather than per-party instances. Layouts are deterministic from game data, so only dynamic entities are replicated.
- **Native dungeon encounter parity is an MVP requirement.** Players must encounter the same native enemy types in the same native marker locations as the equivalent single-player DFU dungeon whenever the native generation path can be reused or reproduced safely. A curated or randomly substituted enemy roster is not acceptable for the MVP because dungeon identity is part of the player experience.
- The server populates a dungeon's server-owned roster on first occupancy by importing or reproducing DFU's native enemy descriptors, including native marker semantics, dungeon-specific enemy selection, roster count, generation inputs, and placement. The resulting descriptors are replicated through DFMP rather than native gameplay enemy objects.
- `NativeParity` is the default and MVP roster mode. It must never silently fall back to a configured or curated roster when native data cannot be resolved; native activation fails closed with diagnostics instead.
- A configured roster mode remains supported as a post-MVP server-owner customization feature. It may intentionally diverge from single-player DFU and can expose roster size, enemy pool, and placement policy controls for custom dungeons, events, and challenge rules.
- The current deterministic marker scan plus curated enemy-type list is the initial implementation of that future configured mode. It must be renamed or isolated behind an explicit `ConfiguredRoster` mode before post-MVP customization work is exposed to server owners.
- The enemy registry and lifecycle model remain provider-agnostic so later wilderness encounters, city/night spawns, and validated mod-provided encounters can use the same server-owned path without redefining entity identity or authority.
- Roster or encounter state persists while its activation scope is occupied and despawns on a configurable timer once empty.
- Server-owned enemy state, movement, and AI are replicated to observers through M6 context and occupancy interest management; Unity transform distance is not the authority boundary.
- Complete remote player avatar presentation in shared dungeon blocks as part of the two-client dungeon vertical slice. Replicate deterministic local scene positions for shared dungeon and compatible building-interior contexts; retain M6 `WorldContextKey` and occupancy as the interest boundary, and do not reuse exterior terrain grounding or Unity transform distance for interior placement.
- Kill credit and loot attribution routed through the server.
- **Personal loot.** Each player loots an independent copy, avoiding loot races and duplication exploits entirely.
- Event bus raises `EnemySpawned`, `EnemyDied`, and `LootGenerated`.

M8 closeout notes:
- Server-owned provider-agnostic enemy registry supports `SpawnedAlive`, `DespawnedAlive`, `Dead`, and `Retired` lifecycle transitions.
- Context-scoped Mirror state identities (`DFMPDynamicEnemyState`) replicate durable identity, provider kind, encounter key, lifecycle, and presentation descriptors without attaching local colliders, AI, or game logic to the replicated state object.
- **Temporary scaffold only:** presentation descriptors (dungeon local position, facing yaw, mobile type) are currently generated from native RDB editor markers in `TEXTURE.199` using the server world seed + context + roster index. This is useful for AI and replication testing but is not native parity.
- Native-parity roster generation must preserve the native meaning of random-monster, fixed-monster, quest, item, start, and enter markers; use the dungeon's native generation inputs and seed semantics; and retain native enemy type/count decisions. It must not treat every marker category as an interchangeable monster spawn.
- `DungeonRosterSize` applies only to the post-MVP `ConfiguredRoster` mode and must never override native roster count in `NativeParity` mode. The native mode derives its count from native monster markers and generation semantics.
- Configured roster controls must remain explicit and opt-in; a native parity failure must not silently switch to `ConfiguredRoster`.
- **Post-MVP candidate:** support DFU's `AlternateRandomEnemySelection` algorithm as an optional native selection mode. MVP NativeParity intentionally locks to the classic/default selection path with stable DFMP seeding.
- **Remaining movement bug:** grounded enemies can retain a stale vertical position while following a sloped dungeon floor. Add slope-aware vertical movement/ground re-resolution in the movement-hardening slice; this is intentionally deferred until the current presentation fixes are validated.
- Roster activation remains scoped per dungeon **block** for interest and lifecycle purposes, but native parity must derive the complete dungeon/block roster from the same layout inputs as DFU. A five-block smaller-dungeon layout must not silently become five independently invented rosters.
- Client-side visual proxies render billboard sprites via bare `DaggerfallMobileUnit` and expose hit targets for weapon/missile combat without native gameplay enemy components.
- Server applies authoritative damage to dynamic enemies through the M7 damage chokepoint, transitioning enemies to `Dead` upon lethal damage.
- When dynamic enemies are killed, personal corpse loot containers (`DaggerfallLoot`) are spawned locally on each client using the enemy's corpse texture and loot table key, avoiding loot races and duplication exploits.
- Vacated context despawn supports configurable delay (`DespawnDelaySeconds`) with automatic cancellation upon player re-entry.
- Event bus publishes `EnemySpawned`, `EnemyDied`, and `LootGenerated` events.
- All unsynchronized client-local native enemy spawns are suppressed during multiplayer sessions: dungeon layout imports are disabled via `Option_ImportEnemyPrefabs = false`, and intermittent random exterior encounters are suppressed by asserting `PlayerEntity.PreventEnemySpawns = true` every frame. Only synchronized server-owned dynamic enemies exist in the world.

Verification:

- EditMode tests for roster seeding, marker scanning, deterministic descriptor generation, spawn and despawn lifecycle, delayed despawn timer and re-entry cancellation, authority boundaries, damage application, death transitions, and kill credit.
- Two-client graphical dungeon smoke test confirming both players see and fight the same enemies.

#### M8 Public-Ready Dungeon Enemy Combat Plan

The current M8 implementation has an end-to-end shared dungeon enemy combat slice, but it is not public-server-ready until enemy sensing, movement, attack authority, geometry interaction, lifecycle cleanup, and presentation are hardened. This plan is scoped only to shared dynamic dungeon enemies. Wilderness, city/night, and ambient world enemies are deferred to later milestones or follow-up planning; quest foes shipped separately in M8.7 on top of this same registry.

Design decisions for M8 closeout:

- Pathing baseline: host native dungeon geometry server-side and use simple physics/path constraints first.
- Enemy persistence: dead or altered dungeon enemy state may reset on server restart for the public beta; persistent enemy state is deferred.
- Combat fidelity: include melee, ranged, and magic enemy attacks, with magic constrained to direct damage unless safe existing DFU helpers make more possible.
- Smoke testing: rely on focused EditMode tests per slice and reserve the comprehensive two-client dungeon smoke test for M8 closeout.

Implementation sequence:

1. **Restore native dungeon roster and placement parity for every supported dungeon.** Determine whether DFU's native descriptor generation can run server-side without importing native gameplay components. If not, extract or reproduce the smallest pure descriptor path that preserves each dungeon's native enemy types, native marker semantics, native roster count, generation seed inputs, and placement. Use Privateer's Hold and other known dungeons as parity fixtures for the general implementation, but do not special-case them: every supported dungeon must match the equivalent single-player DFU result. Keep the current curated roster behind an explicit development-only mode until this slice is complete.
2. Harden network-facing player-to-enemy damage validation. Dynamic enemy damage intents must only accept client `SourceKind.Player`, reject dead attackers, reject attackers in pending transitions, reject malformed mixed-target messages, and continue enforcing same dungeon context, dungeon-local range, request id, sequence, cooldown, and rate limits.
3. Introduce a server-owned dungeon geometry host service. Generate native dungeon geometry for occupied shared dungeon scopes with `importEnemies: false`, isolate it under server-owned roots, strip server-irrelevant audio/UI/player-only components, add diagnostics, and tear it down after the empty-context grace period.
4. Replace placeholder line-of-sight checks with geometry-service queries. Convert dungeon-local enemy/player positions into hosted dungeon scene space, ignore trigger and presentation colliders, and fail closed with a clear warning when strict line of sight is required but geometry is unavailable.
5. Constrain enemy movement against dungeon geometry. Replace straight-line wall crossing with simple capsule/raycast movement, basic slide-or-stop behavior, stuck handling, and vertical stability.
6. Add enemy combat profiles for melee, ranged, and direct-damage magic. Resolve attack range, damage, cooldown, and line-of-sight requirements from native mobile type plus Phase 1 config defaults, and route all accepted damage through the existing M7 vitals/death/respawn path.
7. Replicate attack presentation state. Extend enemy state with attack kind/sequence where needed so clients can show melee, ranged, and magic attacks without granting presentation authority.
8. Tighten lifecycle cleanup. Dead enemies must stop moving, clear targets, stop attacking, and never reacquire. Despawned alive enemies should preserve health while the server runs, clear stale targets, reset attack cooldowns, and resume cleanly on re-entry.
9. Harden client presentation and hit targeting. Keep dynamic enemy hit targets aligned with moving proxies, prevent native enemy confusion, create one personal corpse presentation per dead state, and clean up stale proxies on transition/unspawn.
10. Finish the Phase 1 enemy config and logging pass. Add only beta-needed knobs for line of sight, AI cadence, movement constraints, stuck behavior, melee/ranged/magic attack ranges, damage, cooldowns, and any simplified-AI fallback. Add concise, rate-limited logs for geometry lifecycle, target acquisition/loss, blocked movement, attacks, deaths, and cleanup.
11. Close M8 with one comprehensive two-client dungeon smoke test after focused EditMode fixtures pass. Expected evidence: both clients receive the same native-derived enemy ids and positions, known dungeons use recognizable native enemy types and marker locations, enemies move while respecting basic blocked geometry, both players damage the same enemy health pool, enemy death emits one server `EnemyDied` and `LootGenerated`, both clients see personal corpse loot, enemies attack players, M7 death/respawn still works, re-entry preserves intended in-memory lifecycle, and stale state/proxies do not remain after transitions or disconnects.

### M8.5: Account Authentication and Client Launcher

Status: In-Progress.

Taken out of order, ahead of the remaining M8 enemy AI parity work, because private beta testers need a real login flow and a launcher before a build can be handed to them at all.

This milestone establishes the **authentication spine**, per-server Discord connect, and the launcher as a local profile shell. There is no DFMP-hosted identity service and no global launcher account. Discord hosts identity; each dedicated game server validates connects itself.

The governing rule, taken from FiveM, is that **authentication and authorization are separate concerns**. Authentication answers "who are you" (Discord user id on this server). Authorization answers "may you play on *this* server" and is always evaluated per server at connect (ID whitelist and optional guild roles).

#### Authentication Spine

- A Mirror `NetworkAuthenticator` owns the connect handshake, so no gameplay message handler is reachable before a connection authenticates. Before this milestone no authenticator was attached, so Mirror marked every connection authenticated at the transport layer and the entire message surface, including the administration and developer commands, was reachable by anyone who knew the address.
- Client and server exchange a protocol version and build identifier in the same handshake, so a mismatched tester build is rejected at connect with a readable reason instead of desyncing. This item moves here from M9.
- The server issues a per-connection nonce that the client echoes, so a captured handshake cannot be replayed.
- Authentication mode is a server configuration choice rather than a build choice:
  - `discord` — the mode the author's beta server runs. The game server is the Discord confidential client. The player approves the server in their own browser through the authorization code flow with a loopback redirect (RFC 8252) plus PKCE; the client forwards only the resulting authorization code, and the server redeems it with its client secret and binds the connection to `discord:<snowflake>`. Discord access tokens never ride the unencrypted KCP game wire. Opt-in rather than generated, because it cannot start without a registered Discord application.
  - `open` — no credential. Development and LAN only. The server warns loudly at boot if it is bound to a non-loopback interface in this mode. Identity is the launcher's local profile id. A freshly generated `dfmp-server.json` selects this, so cloning the repo and starting a server works with no external setup.
  - `server_local` — username and password held per server. Protocol, PBKDF2 store, and Development Session remain; there is no player-facing launcher or in-game prompt yet. Deferred as a first-class flow.
  - `dfmp` — leftover reserved mode. Unsupported. A DFMP-operated identity service is not planned.
- An unset mode resolves to `open`; an unrecognized mode resolves to `server_local`, which no current client can satisfy, so a typo fails closed instead of silently disabling authentication. Selecting `discord` without the required secrets aborts startup rather than falling back.
- Discord secrets (`DFMP_DISCORD_CLIENT_ID`, `DFMP_DISCORD_CLIENT_SECRET`, and `DFMP_DISCORD_BOT_TOKEN` when role-whitelisting) come from the environment, never from `dfmp-server.json`.
- `Identity.DiscordRedirectUri` (default `http://127.0.0.1:53682/dfmp-auth`) must be registered verbatim under OAuth2 > Redirects for the operator's application. It must be loopback with an explicit port; the server refuses to start otherwise. The client binds that port with a raw `TcpListener` rather than `HttpListener`, because http.sys on Windows requires elevation or a pre-registered URL ACL and a player must need neither.

**Rejected: the OAuth2 device-code grant.** M8.5 was first built on Discord's device-code flow. Every call to `/oauth2/device/authorize` is refused with `{"code": 50023, "message": "Invalid client id"}` for an ordinary application, reproduced outside DFMP with `curl` using a valid application id, with and without Public Client, and with both HTTP Basic and form-body credentials. That grant is documented only under the Social SDK and console account-linking, where it is presented for approved console middleware, and it appears nowhere in Discord's general OAuth2 documentation. Even with approval it would not fit DFMP, because every server operator registers their own application and none of them can be expected to obtain partner access from Discord. The loopback authorization code flow uses the plain grant every application already has.
- After Discord identity is known, the existing M5 `WhitelistEnabled` / `AllowedAccountIds` list applies (`discord:<snowflake>`). Optional `DiscordGuildId` plus `DiscordAllowedRoleIds` checks guild roles via the bot token and fails closed.
- For `server_local` only: credentials never cross the wire in plaintext. The client sends a key derived from the password and username, and the server stores a salted hash of what it receives. Password records use PBKDF2-HMAC-SHA256 with a per-account salt and a constant-time comparison. Per-address throttling and per-account failed-attempt backoff use a generic failure reason. Accounts self-register on first successful connect, paired with the whitelist so only pre-listed names can claim an account.

Accepted beta limitation: KCP is unencrypted. The authorization code on the handshake is not a Discord token and cannot be redeemed without the server's client secret, and PKCE binds it to the client that requested it. `server_local` derived credentials on that transport remain replayable against that server; that mode must not become the public default.

#### Account Roles

- An account role of player, moderator, or admin, assigned by account identity in `dfmp-server.json` (Discord ids included).
- Privileged message handlers check role rather than merely checking that a connection is authenticated. This closes the gap recorded in R3, where the administration prototype intentionally allows every logged-in player to kick, teleport, and issue developer commands.
- Commands, audit logging, and the full owner/admin/moderator/player permission matrix remain R3. M8.5 establishes only enforcement and the role source.

#### Launcher

A Tauri application at `launcher/`, outside `Assets/` so Unity does not import its sources. Tauri uses the operating system's existing webview rather than bundling a browser, which keeps the download near five megabytes on Windows, macOS, and Linux.

- Local profile only: a stable `profileId` plus Daggerfall path. No DFMP account login. Discord happens in the client at connect.
- Locates the player's existing Daggerfall game files and writes `MyDaggerfallPath` into the DFMP client settings, reusing the same path resolution the dedicated server bootstrap already relies on. This item is pulled forward from R4.
- Resolves the bundled DFMP client from the install layout (`client/` beside the launcher, overridable via exe-adjacent `dfmp-launcher.json` or `DFMP_CLIENT_PATH`) rather than asking the player to pick an executable.
- Launches the DFMP client and hands off the local profile through a restricted-permission temporary session file rather than a command-line argument.

The server list stays in the DFMP client rather than moving into the launcher, so the client's advanced options panel remains reachable. Relocating it later is a decision, not a requirement.

Out of scope for M8.5: username/password as a player-facing flow, a DFMP-hosted identity service, the master server list, launcher auto-update, mod provisioning, in-game moderation commands, and audit logging.

#### Delivered

The authentication spine, the account role model, Discord connect, and the launcher profile shell are implemented.

- `Assets/DFMP/Runtime/Auth/` holds the protocol version, credential derivation, local account store, connect policy, Discord device-code client, attempt throttle, Mirror authenticator, and client session handoff.
- `DFMPAccountIdentityMessage` is deleted. The account identity now comes from the authenticator, never from a client-supplied message. In Discord mode the client-supplied account id is ignored.
- Because the target framework predates the hash-selecting overload of `Rfc2898DeriveBytes`, PBKDF2-HMAC-SHA256 is implemented explicitly in both `DFMPCredential` and the launcher's `credential.rs`. Both carry the same known-answer vector so the two implementations cannot silently diverge when `server_local` returns.
- Rejections use a delayed disconnect, because Mirror drops a queued message if the connection closes in the same frame, which would leave a rejected tester with no reason.
- The launcher builds and runs on Windows. `cargo test` and the EditMode auth fixtures pass.

#### Remaining

1. **Runtime verification.** Live Discord connect against a dedicated server is still outstanding after EditMode fixtures.
2. **`DFMPProtocol.BuildId` is a hard-coded constant.** The protocol version gates compatibility correctly, but the build identifier is cosmetic until it is injected at build time.
3. **The launcher is unbuilt and untested on macOS and Linux.** Only Windows has been exercised. macOS additionally needs a real `icon.icns`, which the icon script deliberately does not fabricate.
4. **Placeholder launcher icon.** `launcher/scripts/make-icons.mjs` draws a generated placeholder.
5. **Character and account stores resolve through Unity's `Application.persistentDataPath`, not the portable-aware path the rest of the client uses.** They land in the shared Daggerfall Workshop folder rather than beside `dfmp-server.json` or inside the portable install, which is confusing for an operator and inconsistent with the portable-install rule in R4. Only the server writes them today, so nothing is broken, but the decision should be made before anyone else runs a server. Deferred to R1 or R5.

Verification:

- EditMode tests for handshake version mismatch, nonce replay rejection, mode selection, credential derivation, account registration, correct and incorrect password paths, lockout behavior, Discord identity mapping, ID whitelist, guild-role allow/deny/fail-closed, and role resolution.
- A negative test confirming that administration and developer messages sent before authentication are dropped. Before this milestone that attempt succeeded, and it must not.
- Discord smoke: launcher Play with no login, in-game server list, browser consent overlay, approve and decline paths, loopback port already in use, whitelist miss (readable reject), whitelist hit (and optional role), character select.
- Confirm a client built with a bumped protocol version is rejected with a readable reason rather than desyncing.
- Confirm a non-admin account cannot invoke the F12 roster, kick, or any developer command.
- Editor `open` still joins without Discord. Development Session still covers `server_local`.

Migration note: account identities become `discord:<snowflake>` in Discord mode, and character records are keyed by account identity, so existing beta test characters are orphaned. This is accepted rather than migrated.

### M8.6: Character Select

Status: In-Progress.

Players can keep more than one character per account on a given server. After authentication and before Ready/spawn, the client shows a DFU-native character management window unless the owner has turned that UI off.

- `dfmp-server.json` Identity knobs: `CharacterSelectEnabled` (default true), `MaxCharactersPerAccount` (default 4, clamp 1–16), `AllowCharacterDelete` (default true). Setting `CharacterSelectEnabled` to false restores auto-join: no characters starts creation, one or more loads the most recently played character.
- Character records are keyed by account, world, and character id. Existing one-file-per-account records migrate on first list/load.
- First-join creation no longer writes a placeholder record. A mid-wizard disconnect consumes no slot.
- The select window lists characters, supports Enter World, New Character (native DFU wizard), Delete with confirm when allowed, and Back to the server list. Globally disabled actions are hidden; situationally unavailable actions are dimmed.
- The client never chooses its spawn point. In-game character swapping is out of scope.

Verification:

- EditMode tests for config clamp, multi-character store list/save/delete, legacy-file migration, join-policy auto-join vs select, create-at-cap, delete-disabled, and select-unknown-id rejection.
- Smoke test: empty account sees New Character and reaches the wizard; a second character can be created up to the cap; delete confirms and respects the owner toggle; UI-off auto-joins an existing character and auto-creates when none exist; a pre-M8.6 single-file character still loads after migration.

### M8.7: Client-Owned Questing

Status: Complete (2026-09-20). A character can take a quest from a questor, progress it, log out, return after a server restart, and finish it, with other players able to help fight its foes. Two follow-ups stay open and are tracked where they belong: exterior quest-foe geometry in M9 below, and the QUEST-START-001 divergence at the end of this section. Implementation is done; [QUESTING_SMOKE_MATRIX.md](QUESTING_SMOKE_MATRIX.md) is deliberately retained as the manual verification sheet and is signed off during the M9 beta stability pass, not here.

Quest progression is **client-owned per character**. Each client runs one native quest machine, while the server persists that machine and owns physical quest enemies. Full quest-graph replication is out of scope and is not required for other players to assist with quest combat.

- `Quests.Mode` reserves two architecture modes: `ClientOwned` and future `Shared`. `ClientOwned` is the only implemented mode; a server configured for `Shared` fails startup explicitly.
- Quest dialogue, journal, branching, placed NPCs, placed objects, rewards, and faction effects remain personal. Placed quest objects are visible and interactive only for their owner, preventing another player from stealing an objective.
- Quest-machine state, SiteLinks, quest-adjacent character state, and complete quest-item identity persist in the server's per-character record. Restore ordering is quest machine first, then quest-linked inventory and world resources.
- Quest failure deadlines are disabled by default, while clocks required for quest sequencing continue to run. Built-in clock expiry paths require explicit classification; blanket clock suppression is not acceptable because it can stall the main quest.
- Every quest foe is owner-scoped and server-owned. Only the owner can activate it, but every nearby observer can see, fight, and be attacked by it. Death advances only the owning objective regardless of who dealt damage or the killing blow.
- Marker-bound and dynamic quest foes use the M8 provider-agnostic enemy registry, M6 context interest, and M7 damage chokepoint. A subtle owner cue disambiguates otherwise identical owner-scoped foes.
- Quest-foe AI requires hosted server geometry for its context. Dungeons and building interiors are hosted; exteriors are not yet, so an outdoor quest foe cannot currently sense or move. Closing that gap is an M9 pre-release polish item.
- Activation defaults to exact authoritative context plus 30 metres from the objective. Despawn begins outside 45 metres or after leaving context, with a 10-second grace period. Despawn preserves the live logical generation and health so crossing the boundary cannot heal the foe.
- A foe-carried quest item appears only in the owner's personal corpse loot and retains its quest UID and resource symbol. Non-owners cannot take or consume it.
- Objective, encounter generation, and network enemy IDs remain separate. Future `Shared` mode can coalesce equivalent personal objectives without replacing personal quest machines.
- Built-in quest actions that touch server authority, including teleport, shared time, guards, and scene-wide enemy commands, require an explicit compatibility classification and validated server route. Full-game progression does not permit a silent quest blacklist.
- The owner-scoped lifecycle and future shared migration path are specified in [Quest Enemy Networking Spike](QUEST_ENEMY_NETWORKING_SPIKE.md).
- **Known divergence (QUEST-START-001, 2026-09-20):** DFMP registers the quest UID before `TalkManager.AddQuestTopicWithInfoAndRumors()`, so `UndiscoverQuestResidence()` can resolve the live quest. Upstream DFU adds topics first, then inserts the quest, so `GetQuest()` is still null and that undiscover call no-ops. `PlayerGPS.UndiscoverBuilding()` only mutates the currently loaded map pixel, so a remote site (for example `M0B00Y07` Yeomsly Residence in Woodsly Hall, accepted in Daggerfall city) is unaffected. A same-town quest residence the player had already discovered can be hidden on accept in DFMP and would stay discovered in vanilla. Do not treat a missing city-map nameplate as a DFMP bug unless an NPC has marked the building (`TalkManager.MarkKeySubjectLocationOnMap`); residences stay unnamed until then in both games. Replacement: either restore the vanilla no-op for undiscover while keeping UID registration for dialog reveal, or keep DFMP's working undiscover and treat it as intentional parity-plus.

Verification:

- EditMode fixtures for quest config validation, quest-state persistence round trips, objective registration and credit, re-entry generation/health retention, teleport marker bounds, clock classification, and the owner cue.
- Manual coverage lives in [QUESTING_SMOKE_MATRIX.md](QUESTING_SMOKE_MATRIX.md) and runs against a dedicated server with two clients.

### M9: Beta Server Launch Readiness

Status: Planned.

The smallest amount of non-gameplay work required to actually put testers on the author's server. Everything here is intentionally minimal, because the polished versions are Phase 2.

- Tester client build: one versioned zip containing the M8.5 launcher and a `client/` portable DFMP build that the launcher resolves automatically. No auto-update and no server browser.
- Whitelist administered in `dfmp-server.json` (`AllowedAccountIds` as `discord:<snowflake>`) and optionally via Discord guild roles. The author still issues Discord app credentials out of band.
- Manual operations are acceptable: file-copy character backups, restart by hand, read logs on disk.
- Beta stability pass: run the server continuously for a multi-day soak, watch for leaks, unbounded growth in session or roster state, and reconnect edge cases.
- Dungeon enemy navigation hardening: **Delivered with M9 AI parity items 2 and 5.** Native `FindDetour` / `GetDestination` replace stop-flush-and-release. Remaining overlap of stacked pursuers is the enemy-separation item below.
- **End of M9, Tony:** corner-sticking polish pass, AI parity item 7. Compare a 90-degree corner against single-player DFU before touching the port again.
- Dungeon enemy separation: add native-like enemy-to-enemy collision or local avoidance so pursuing enemies do not converge onto the same point and overlap.
- **Exterior and wilderness quest-foe geometry.** Dungeons and building interiors are now hosted server-side, so quest foes in those contexts sense, move, and attack against real collision. Exterior contexts still have no hosted geometry: `DFMPDungeonGeometryService.TryCreateScope` rejects them, strict line of sight fails closed, and grounded movement cannot resolve, so a `CreateFoe` target placed outdoors stands still and never strikes — the same defect that interior hosting fixed. This is reached in ordinary play, not just at the edges: `M0B00Y15` ("Hunt for Giant Rodents") arms `create foe _rats_ every 2 minutes 7 times` when the player enters the quest house, and native `CreateFoe.TryPlacement` then follows the player outdoors, so leaving the building mid-cycle spawns inert rats in the street. Before the beta build ships, either host exterior collision for the occupied map pixel (terrain plus location geometry, released on the same occupancy grace period) or define an explicitly classified exterior movement and line-of-sight path. A silently inert outdoor quest foe is not acceptable, and the one-per-scope missing-geometry warning is diagnostics, not a fix.
- **Revisit quest-foe spawn grounding when exterior foes are hosted.** Native `CreateFoe.PlaceFoeFreely` parks a foe a fixed 1.25m above the floor it raycast and only settles it onto that floor afterwards in `FinalizeFoe`, which hook `QUEST-FOE-DYNAMIC-001` pre-empts. As of 2026-09-20 the hook therefore reports `floorHit.point` and DFMP registers the ground contact directly, so dynamic quest foes are feet-grounded at the source instead of depending on the server re-raycasting them. That was done so foes stop floating in contexts with no hosted geometry. Two things to settle when exterior hosting lands: decide whether client-reported ground should remain authoritative or be re-derived server-side once exterior collision exists (it is currently trusted input from the owning client), and restore the flying-height offset outdoors — `DFMPDungeonRosterPolicy.TryGroundObjectivePosition` only applies `GetNativeFlyingHeightOffset` when hosted geometry resolves, so a flying quest foe spawned outside currently sits flat on the ground instead of native's raised position.
- Native enemy AI parity: see the dedicated subsection below. Items 1, 2, and 5 are delivered; items 3, 4, and 6 remain, and item 7 is the deferred corner-sticking pass.
- Record the two-client exterior remote-presentation smoke: grounded named avatars, movement and facing, and presentation-scope culling.
- A short tester-facing setup note and a bug reporting channel. This is not the Phase 2 documentation set.

Out of scope: auto-update, server browser, mod provisioning, in-game admin commands, metrics dashboards.

#### Native Enemy AI Parity

Progress: items 1, 2, and 5 delivered; items 3, 4, and 6 open. Item 7 is a deferred polish pass on a corner-sticking defect left open by item 5, scheduled for the end of M9 and gated on Tony comparing against native DFU first.

M8 shipped a deliberately simplified server AI: beeline pursuit and attack-on-cooldown inside a flat range. M9 replaces it so enemies read as native Daggerfall enemies to any player who has played single-player DFU. Detection, give-up, pursuit destination, and obstacle/ledge detours now match native DFU; combat movement patterns, attack cadence, and flying/swim motor distinctions are still outstanding.

The authority boundary does not change: all of this runs server-side on dungeon-local coordinates against hosted geometry. Clients keep presentation only.

1. **Replace the pursuit leash with the native give-up model.** **Delivered (2026-09-18).** Deleted `PursuitLeashRange` and the home-position return. Ported `EnemyMotor.GiveUpTimer` semantics: the timer refreshes while the target is detected, decays only while undetected, and on expiry the enemy stops where it stands rather than walking home. Detection is generalized to any occupant of the context via a classic spawn/despawn envelope, FOV sight, hearing, and `StealthCheck` (`DFMPDynamicEnemySensesPolicy`). **Documented approximation:** the server uses persisted base Stealth skill and Speed attribute from the character record where native DFU uses `GetLiveSkillValue` / `LiveSpeed`, so magic effects on those stats do not shift detection odds. Aligning live values is deferred until server-side effect simulation or a trusted client report exists (Phase 2 trust hardening / P-SCALE).
2. **Port native pursuit destination selection.** **Delivered (2026-09-19).** `DFMPDynamicEnemyPursuitPolicy` reproduces `EnemyMotor.GetDestination`: clear-path (or ranged/magic with target in sight) uses last-known/predicted position; otherwise search `LastKnownTargetPos + LastPositionDiff.normalized * searchMult` (0–10). Stuck-target-release was removed. **Documented approximations:** classic `PredictedTargetPos = LastKnownTargetPos` (no EnhancedCombatAI intercept solver); server spherecasts have no player capsule, so a clear path cannot “hit the target first” the way native `ClearPathToPosition` can.
3. **Port native combat movement patterns.** Reproduce DFU's per-enemy approach/retreat behavior rather than a constant-velocity approach: `stopDistance` derived from attack reach, the lunge-and-withdraw pattern (`backAway`, `retreat`, `strafe`, `pauseUntil`, `changeStateTimer`), ranged/caster standoff behavior, and the `EnhancedCombatAI` variations. The rat's observed lunge -> jump back -> pause -> lunge cycle is the acceptance example, but every mobile type must be checked against its native behavior, not just melee monsters.
4. **Port native attack timing.** Replace the flat `AttackCooldownSeconds` with native attack cadence sourced from mobile data and classic update timing, including windup/recovery so the replicated attack state lines up with the native animation rather than a server-side interval.
5. **Port native obstacle and ledge handling.** **Delivered (2026-09-19), with one open defect deferred to item 7.** Hosted-geometry `ObstacleCheck` / `FallCheck` / `FindDetour` (±45° sweep, clockwise stickiness, 0.75s detour) run before the capsule clip, and the clip now deflects leftover motion along the blocking surface the way `CharacterController.Move` does. Action doors and loot are not movement obstacles; `CanOpenDoors` enemies open unlocked hosted doors and the server broadcasts `DFMPActionDoorSyncMessage`. Hearing rays ignore action doors. Three parity requirements that a first pass got wrong and that any rework must preserve: probes originate at the capsule center rather than the grounded descriptor position; the detour waypoint is exempt from stop distance (it sits two units out, inside melee range); and the `searchMult` ramp and the move decision must share one stop distance. The upward-slope recast must reach at least as far as the original obstacle cast (native's same-distance recast false-clears vertical walls). `FallCheck` must confirm its one-unit-ahead ray origin is reachable, because native's ray outruns the quarter-unit obstacle check and otherwise reports phantom ledges on the far side of walls, which made the detour sweep discard good headings at 90 degree corners. A zero-progress collision also arms a detour for the next tick. **Tick-rate adaptation:** native's `AttemptMove` re-probes and, when blocked, calls `FindDetour` *instead of* moving that frame; at 60-144 fps that costs a few milliseconds and is invisible, but our AI tick is 100ms, so a pursuer that re-detours against a wall burned most of its ticks deciding and visibly stalled at corners while the log filled with detour starts. We therefore spend the same tick's step on the heading the sweep just cleared, which reproduces what native does on its very next frame; a failed sweep still skips, because it found nothing safe. `ClearPathToPosition` must sphere-cast the full span to the position under test: native sizes that cast from its previous destination, which is normally the target itself, but ours is the two-unit detour waypoint right after a detour, so a short cast cleared the corner wall, reset the `searchMult` ramp every few ticks, and left the enemy grinding at a stale last-known position that sat inside its own stop distance while the player was twenty units away. **Documented approximations:** knockback/paralyze gravity falls and full fly/levitate/swim locomotion remain item 6; flying only uses the native up/down mix inside `FindDetour`, and the mover still flattens vertical steps. Locked-door bash remains with attack-cadence work (item 4).
6. **Preserve flying, levitating, and swimming movement distinctions** from `EnemyMotor` rather than the current single flying-height offset.
7. **TONY: corner-sticking polish pass — compare against native DFU before any further code changes.** Deferred to the end of M9 by Tony's call on 2026-09-19 after four unsuccessful fix attempts. **Do not resume this by reasoning about the port in isolation; the next step is Tony observing the same corner in single-player DFU and deciding what correct looks like.**

   *Symptom:* pursuing enemies intermittently stall at 90-degree corners in Privateer's Hold. Not every time, and not every enemy — two rats chasing the same player around the same corner, one rounds it and the other sticks. The stuck enemy keeps its target and keeps logging rate-limited `Detour started` with `obstacle=True, fall=False`, so it is deciding to detour continuously while making little or no headway.

   *Already fixed along the way; each was a real defect, none resolved the symptom.* Probes originating at the grounded descriptor position instead of the capsule center. The detour waypoint being swallowed by attack-range stop distance. The `searchMult` ramp and move decision using different stop distances. Blocked motion stopping dead instead of deflecting along the surface. Phantom ledges from `FallCheck`'s one-unit ray outrunning the quarter-unit obstacle check. The upward-slope recast false-clearing vertical walls. `ClearPathToPosition` sizing its cast from the stale two-unit detour waypoint and so resetting the search ramp. Spending the detour-decision tick standing still, which costs 100ms here versus a few milliseconds at native frame rate.

   *Open questions for the native comparison:* does native actually round this corner cleanly, or does it also mill about and we are chasing behavior that vanilla never had? How long does a native enemy take to clear an equivalent corner? Is the residual difference explained by AI tick rate (10Hz here against native's per-frame `Update`), in which case raising the enemy AI tick rate is the honest fix rather than more compensation inside the port? Re-validate the tick-rate adaptation recorded in item 5 at the same time, since it is a deliberate structural divergence from native's literal `AttemptMove` control flow.

Verification:

- EditMode fixtures for give-up timer decay/refresh, last-known-position search extrapolation, approach/retreat state transitions, and detour selection.
- Side-by-side comparison against single-player DFU for a melee monster (rat), a humanoid fighter, a ranged attacker, and a caster: pursuit distance, approach/retreat rhythm, attack cadence, and ledge behavior must be indistinguishable to a player.
- Confirm no enemy exhibits behavior with no single-player analogue, and that no enemy stops pursuing for a reason that does not exist in native DFU.

Verification:

- A tester who has never run DFMP can install the client, connect, create a character, play, disconnect, and return to the same character.
- Multi-day soak run with no unbounded resource growth and no manual intervention required to keep the server up.

## Phase 2 Milestones: Public Release

Phase 2 turns a server the author can run into a product other people can run. Nothing here changes the core gameplay loop; it changes who is capable of operating it.

Phase 2 does not begin until Phase 1 has been running a live beta long enough to know which knobs owners will actually want. Guessing the configuration and scripting surface before the beta produces the wrong surface.

Phase 2 carries the deferred disease-state work that M6 deliberately leaves outside its acceptance boundary:

- Persist active vampirism state across reconnect, restart, and character restore, including vampire clan, transformed state, vampire spells, satiation, and related effect data.
- Design and implement the complete lycanthropy lifecycle, including infection, timers, transformation, transformed state, relocation if required, cure behavior, and persistence.
- Add focused lifecycle and persistence tests before exposing disease controls to public server owners or GM tooling.

### R0: Public Master Server List

Status: Planned.

Per-server Discord authentication and ID/role whitelist shipped in M8.5. DFMP does not operate a central identity service, OAuth callback, or join-token signer in this milestone or as a required public-release path. Discord remains the identity provider; each game server keeps validating connects itself.

A public master *list* of servers is optional later work and is listing only. It must not become a hosted login dependency. Private servers can opt out of listing. Heartbeats, if they exist, should record the source address rather than trusting a self-reported one.

Username/password (`server_local`) as a first-class player flow (TES3MP-style per-server accounts, with launcher or in-game prompt) stays deferred. The hash/store/throttle path already exists.

Verification, if a listing service is ever built:

- Listings cannot be poisoned by a self-reported address.
- A server that opts out of listing does not appear.
- Players can still connect by LAN discovery and direct address without the list.

### R1: Full Server Configuration Surface

Status: Planned.

- Promote every hard-coded Phase 1 constant that a server owner would reasonably want to change into `dfmp-server.json`, organized into coherent sections: identity and listing, connection and whitelist, chat, world and time, rest and travel, PvP and combat, enemies and loot, quests, and persistence.
- Schema versioning and migration for the config document itself, matching the approach used for character records.
- Startup validation that rejects malformed values with a specific, actionable message and exits, rather than silently falling back to defaults.
- Config reload for the subset of values that are safe to change on a running server, with the rest clearly marked restart-only.
- A documented, fully commented reference config shipped with the build.
- **Delivered:** `Rest.Policy = Disabled | ServerManaged`. `ServerManaged` is vanilla rest without clock advance (see M6). Remaining R1 rest/travel work is any extra owner knobs, not re-implementing rest recovery. The in-game admin/GM toggle stays R3.

Verification:

- EditMode tests for parsing, defaults, invalid-value handling, migration, and reload safety classification.
- A server started from the shipped reference config runs without warnings.

### R2: Server-Side Scripting Layer

Status: Planned.

See [SCRIPTING_ARCHITECTURE.md](SCRIPTING_ARCHITECTURE.md) for the complete design rationale, TES3MP comparison, server-driven UI protocol, and `.dfmod` client extensibility model.

The M4 event bus exists precisely so this milestone is a binding exercise rather than a rewrite.

- An embedded scripting runtime loading scripts from a server-side scripts folder.
- Event API: scripts subscribe to bus events (`PlayerConnected`, `PlayerSpawned`, `ChatMessageReceived`, `LocationEntered`, `PlayerDamaged`, `PlayerDied`, `PlayerRespawned`, `EnemySpawned`, `EnemyDied`, `LootGenerated`).
- Command API: scripts act on the world through a narrow, validated surface (send chat, teleport a player, adjust vitals, spawn or despawn enemies, grant items or gold, kick or ban, read and write per-character script data).
- The developer command action boundary is the prototype for this API: `dfmp_infect_self` is a server-authorized, config-gated test command today, and R2 should promote its validated action service rather than expose direct client mutation.
- Handlers may veto or modify eligible events, with the veto points defined explicitly rather than every event being interceptable.
- Sandboxing and error isolation: a faulty script is disabled with a logged error and never takes the server down. Execution time budgets prevent a script from stalling the tick.
- Per-character and per-server script key-value storage persisted through the M5 store interface.
- Hot reload of scripts on a running server.

Verification:

- EditMode tests for event dispatch, veto semantics, command validation, error isolation, and time budgeting.
- A sample script set shipped as documentation-by-example, exercised in a smoke test.

### R3: Admin, Moderation, and Chat Commands

Status: Planned.

An early F12 administration prototype now provides a server-authored connected-player roster showing character and account identities, highlights the requesting player as Admin, and supports confirmed kicks. Kicked clients receive an explicit notice and return to the DFMP startup screen after acknowledgement. The protocol has focused EditMode coverage and the two-client kick flow has passed a runtime smoke test. M8.5 adds configuration-driven account roles and enforces them on privileged handlers, which closes the prototype's "every logged-in player is an admin" gap. The permission matrix, command framework, and audit requirements below remain outstanding.

- Role and permission model: owner, admin, moderator, player, with permissions granted per command. M8.5 establishes the player/moderator/admin role source and handler enforcement; R3 extends it to a full per-command permission matrix.
- In-game chat command framework, with commands registerable by both the core and R2 scripts.
- Core moderation commands: kick, ban, unban, mute, whitelist add and remove, teleport, and player lookup.
- GM/world-control commands should expose the same server action service used by R2 scripts, including an `Advance World Time` action, player infection/cure, player teleport, and later enemy spawn/despawn. The client menu is only a request UI; authority, validation, permission checks, confirmation for large jumps, and audit remain server-side.
- Rest policy toggle in the admin/GM menu: `Rest.Policy` `Disabled` / `ServerManaged`, calling the existing `DFMPNetworkServer.SetRestPolicy` API, with role checks and audit. Optional persist back to `dfmp-server.json` can land with that menu; the setter must not require a server restart.
- Audit log of moderation actions, keyed to account identity.
- Additional chat channels beyond the single global channel, at minimum a staff channel and private messages.
- Add bounded, server-persisted global chat history with explicit retention and access policy.

Verification:

- EditMode tests for permission resolution, command parsing, and moderation state transitions.
- Smoke test confirms an unprivileged player cannot invoke privileged commands.

### R4: Client Distribution and Launcher

Status: Planned.

Ship DFMP as a separate client application that reuses the player's existing Daggerfall data instead of replacing or modifying their Daggerfall Unity install. The DFMP client is a sibling of DFU in the same way DFU is a sibling of classic Daggerfall: another engine binary reading the same `arena2` data.

M8.5 already delivers the launcher shell, local profile, game-file location, and Discord-at-connect. R4 completes distribution.

- Launcher locates the player's existing Daggerfall game files and writes `MyDaggerfallPath` into the DFMP client settings, using the same path resolution the dedicated server bootstrap already relies on. **Delivered in M8.5.**
- DFMP client ships as a portable install (`Portable.txt`) so settings, saves, keybinds, and mod settings live in its own `PortableAppdata` folder and never read or write DFU's persistent data folder.
- Mod bundles are already install-local because `ModDirectory` defaults to `StreamingAssets/Mods`, so the DFMP client has its own mod folder independent of the player's DFU install.
- Net effect: separate settings, saves, keybinds, mod list, and mod configs. The only shared resource is the read-only `arena2` game data.
- Players do not inherit their existing DFU mods or keybinds. Keybind import is a possible later launcher convenience. Mod inheritance is explicitly not wanted.
- Server dictates the allowed mod set and the launcher provisions the client's mod folder to match, without touching the player's single-player setup. R4 only has to detect a mismatch and tell the player what is required; fully automatic download, enable, and configuration is P-MOD.
- **Launcher auto-update:** when opened, the launcher checks for a new DFMP release, downloads it, and installs it. Integrity-checked. Not implemented in M8.5.
- The server list remains in the DFMP client, where the advanced options panel lives. Relocating it into the launcher is optional and is not a requirement of this milestone.
- No game files are copied, moved, or patched. The player's vanilla DFU install keeps working side by side.
- Client-release polish: synchronize remote-player bow draw, held-draw, release, and cancellation state so holding the bow without releasing is not presented to other players as an immediate fire. This is presentation-only and does not change ranged hit authority.

Verification:

- Launch a DFMP client against an unmodified Daggerfall install and confirm DFU's persistent data folder is untouched.
- Confirm the DFMP client resolves settings, saves, and mods from its own portable paths while a vanilla DFU install is present.
- Confirm launcher-supplied connect arguments reach the client bootstrap and establish a session.

### R5: Operational Hardening

Status: Planned.

The work that separates "the author babysits it" from "a stranger runs it on a rented box".

- Scheduled character and world state backups with retention, plus a documented restore path.
- Crash and restart recovery: clean shutdown persistence, and recovery of in-flight state after an unclean stop.
- Complete disease-state persistence for active vampirism and the later lycanthropy lifecycle before public release. Live-session vampirism transition behavior remains the M6 baseline; reconnect and restart restoration are Phase 2 work.
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
- Reopen saved building interiors through validated server-issued door assignments for **death-respawn** anchors, retaining the exterior fallback when reopening is impossible. (Reconnect interior restore for buildings and dungeons was delivered as a Phase 1 follow-up to M6.)
- Optional citizen and ambient NPC synchronization, only if it proves to matter in practice.
- Server-owned weather and seasonal events beyond the Phase 1 time and weather baseline.

### P-QUEST-FOES: Quest Enemy Networking Spike

Status: Superseded by M8.7, the Phase 1 client-owned questing milestone.

The owner-scoped server-enemy model is now the implementation baseline. Future work here is limited to `Shared` mode: prove objective equivalence and optionally coalesce equivalent owner-scoped encounters while retaining personal quest machines, personal rewards, and idempotent per-character credit.

The accepted owner-scoped model and remaining shared-mode questions are maintained in [Quest Enemy Networking Spike](QUEST_ENEMY_NETWORKING_SPIKE.md).

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
