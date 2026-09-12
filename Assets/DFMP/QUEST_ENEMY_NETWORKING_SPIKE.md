# Quest Enemy Networking Spike

## Status

Deferred investigation. Phase 1 keeps quest-spawned enemies client-local along with the rest of each character's personal quest simulation.

This document records the problem space and candidate designs so a later spike can make an evidence-based decision without reopening the entire discussion. It is not an implementation specification.

## Context

DFMP's MVP keeps one native Daggerfall quest state machine per character. Quest NPCs, dialogue, journal state, placed items, rewards, and faction effects are personal client state. This avoids synchronizing arbitrary quest scripts and the large, fragile quest graph replication that naive full-graph replication would require.

Quest enemies are the difficult boundary. They are physical actors that players reasonably expect to see and fight together, but native DFU binds each physical quest resource to one quest instance UID and one resource symbol. Independently instantiated copies of the same quest have different UIDs and can also resolve random branches, sites, buildings, dungeons, and markers differently.

The initial idea was to register each quest foe as an owner-tagged server enemy. That is implementable, but it creates ambiguous and potentially misleading encounters when several players have similar objectives in one location. A player can kill the visually identical enemy belonging to someone else, advance no personal objective, and receive no clear explanation. Independent owner-tagged spawns can also crowd a fixed quest location, especially at higher population.

For reliability and understandable behavior, MVP accepts reduced co-op fidelity and leaves all quest enemies client-local. They are visible and combat-relevant only to the character whose quest created them. Ordinary dungeon enemies remain server-owned under M8.

## MVP Decision

- Every client runs its own native quest machine.
- All quest resources remain client-local, including placed foes and dynamic `CreateFoe` encounters.
- Quest enemies are not registered with, spawned by, replicated through, or credited by the server.
- Other players cannot see, damage, tank, or help kill another character's quest enemies.
- Quest enemies can damage only the character whose local quest created them. They can never target or damage another player.
- The owner client reports local quest-enemy damage through a dedicated M7 PvE intent. The server still owns and applies the character's health change, but treats the unverifiable local enemy source as an explicit beta trust exception and enforces strict numeric bounds, rate limits, source-session ownership, and an owner-only target.
- Quest-enemy death, kill credit, carried quest loot, and all other quest effects remain local to the owning character.
- Canceling or completing a quest follows native local cleanup behavior.
- The limitation is accepted for the private beta and should be documented for testers.
- Quest patterns proven incompatible with this boundary may be placed on a narrow beta blacklist rather than receiving one-off networking behavior.
- Server-owned quest enemies and shared quest progression remain deferred until this spike is completed.

This decision favors a coherent limitation over a partially shared system whose ownership is invisible to players.

## MVP Compatibility And Trust Boundary

The local quest PvE damage path is deliberately narrower than general client-reported damage:

- The submitting session is also the only legal damage target.
- The intent cannot name or affect another player, a server-owned enemy, or a world entity.
- The server clamps damage to configured per-hit bounds and rejects reports that exceed rate or cooldown limits.
- The intent passes through the same server damage application chokepoint as other damage, so death, persistence, and event publication remain server-owned.
- The server records the source category as local quest PvE for diagnostics and later abuse analysis.
- The beta accepts that a modified owner client can fabricate this damage source. This exception does not grant the client authority to set health directly.

The client must keep local quest combat isolated from shared combat. A local quest enemy cannot select another player as a target, collide with or obstruct another player's simulation, damage a server-owned enemy, or generate shared loot and kill events. Its death and carried items are resolved only by the owning quest machine.

The tester setup note must state that quest combat is personal: party members do not see the enemy, cannot assist with it, and may observe the owner reacting to an entity absent from their client. If a quest action cannot remain inside this boundary reliably, beta may blacklist that specific pattern or quest. Blacklisting is a compatibility fallback, not a general quest curation system.

## What Still Must Persist

Client-local simulation does not mean local-only storage. DFMP suppresses native save/load in multiplayer, so active quest progress must be restored through server-side character persistence.

A quest persistence design must account for:

- The native quest-machine state, including active and tombstoned quests, tasks, actions, resources, site links, journal state, clocks, and foe kill counters.
- Quest-item identity. `IsQuestItem` alone is insufficient; DFU also matches the quest UID and resource symbol.
- Character state changed by quests, including faction reputation, guild membership, and legal reputation where applicable.
- Restore ordering so the quest machine exists before quest-linked inventory or scene resources are restored.
- Schema versioning, size limits, malformed payload handling, removed quest mods, and atomic character saves.
- Quest deadlines under server-owned world time. Blanket clock suppression may break timed sequencing that is not a failure deadline, so deadline policy requires separate verification.

The server may store an opaque, versioned DFU quest payload for beta while continuing to keep authoritative character fields structured. The server does not need to execute the quest machine merely to persist it, but it must validate payload bounds and ownership.

## Goals Of The Spike

The spike should determine whether quest enemies can become server-owned without synchronizing complete quest machines and without producing confusing ownership behavior.

It must answer:

1. Can the server identify equivalent quest objectives reliably enough to share one physical encounter?
2. What player-facing rule determines who receives objective credit?
3. What happens when an objective holder is absent when the enemy dies?
4. When should an unsatisfied objective respawn or re-arm an encounter?
5. How do cancellation, disconnect, quest completion, and location unload affect enemy lifecycle?
6. How are quest-carried items and personal loot delivered without duplication or loss?
7. Which quest action patterns can be supported safely, and which must remain local?
8. Can the design remain bounded and understandable from 8-16 players toward approximately 100 players?

## Candidate A: Keep Quest Enemies Client-Local

This is the MVP baseline and remains a valid permanent mode.

### Advantages

- Uses native DFU quest behavior with the fewest hooks.
- No ambiguous enemy ownership or wrong-target kills.
- No duplicate server encounters or quest-specific server AI lifecycle.
- Cancellation, branching, spawning, and kill counters remain internally consistent.
- Modded quests are less likely to require special server support.

### Disadvantages

- Other players cannot see or help fight quest enemies.
- A party can see different combat occurring in the same physical space.
- Quest bosses and ambushes do not participate in authoritative server combat.
- The server cannot verify the existence, attack timing, or damage output of a local quest enemy. Numeric and rate validation limits accidents and abuse but cannot make the source authoritative.
- A modified client can manipulate its personal quest combat and report resulting character progression during the beta trust phase.
- Some quest patterns may need a beta blacklist if they cannot be isolated from shared actors or the bounded owner-only damage path.

## Candidate B: One Server Enemy Per Personal Objective

Each active quest objective registers an owner-scoped server enemy. The server owns combat and routes confirmed death back to that objective's character.

### Advantages

- Straightforward mapping from one objective to one enemy.
- Other players can see and help fight the enemy.
- Quest enemy combat uses the same authority path as ordinary M8 enemies.
- Independent quests do not require proving semantic equivalence.

### Disadvantages

- Equivalent objectives can spawn several visually identical enemies at one marker.
- A player can kill another character's enemy and receive no personal quest credit.
- Ownership is difficult to communicate without intrusive labels or other non-native presentation.
- Population at fixed quest sites can grow with the number of objective holders.
- Presence, retry, cancellation, and offline-result policies add substantial lifecycle state.

If investigated, quest progression must reference a stable objective ID rather than an enemy entity ID. That keeps the model open to later encounter sharing.

## Candidate C: Shared Physical Encounter With Personal Subscriptions

Personal quest objectives subscribe to one server-owned encounter when their fully resolved encounter descriptors are equivalent. Quest progression remains independent; only the physical enemy is shared.

This is the preferred post-MVP design hypothesis:

- Objectives that resolve to different locations create different encounters, even when they came from the same quest template.
- Equivalent objectives at the same resolved location and marker subscribe to one encounter rather than spawning one enemy per owner.
- An encounter is active only while at least one unsatisfied objective owner is within its server-authoritative activation area.
- The first eligible owner to arrive activates the server-owned enemy. Once active, every player who can observe it may fight and kill it, whether or not they hold the quest.
- At authoritative death, every subscribed owner who is within the configured credit area receives one idempotent objective result regardless of who dealt damage or the killing blow.
- Subscribed owners outside the credit area receive no result and remain unsatisfied.
- When no unsatisfied owner remains in the activation area, a live encounter despawns after a short safe grace period. It does not remain available solely for players who do not hold the objective.
- After death, the encounter re-arms only for remaining unsatisfied owners. A later eligible arrival activates a new generation after any configured cooldown.
- Owners who already received credit are removed from the active subscription set, so their continued presence cannot cause another spawn.

Example: Alice and Bob have equivalent werewolf objectives at the same dungeon marker. Alice arrives first and activates one werewolf. Any player can help kill it. If Bob arrives before it dies and is within the credit area, both Alice and Bob advance. If Bob is elsewhere, only Alice advances; Bob's objective remains armed and creates a later generation when Bob reaches the marker. If their quests selected different dungeons, each dungeon has its own encounter.

### Advantages

- Players with equivalent objectives fight one understandable target.
- Every eligible subscriber can receive personal credit from the same death.
- Physical population scales with active resolved encounters rather than objective holders.
- Preserves personal dialogue, journal, rewards, and quest branching.

### Disadvantages

- Determining equivalence is complex and must not rely on quest template name alone.
- Independent quest instances can choose different random branches, locations, buildings, dungeons, and markers.
- Late subscribers, partial kill counts, re-armed actions, and quest script updates require encounter generations.
- Dynamic ambushes do not have a stable marker and may not be safely coalesced.
- Incorrect merging can advance unrelated objectives or corrupt quest progression.

A candidate encounter descriptor may need:

- Quest content or version hash.
- Stable action identity and generation.
- Resolved location, building, dungeon, and instance identity.
- Exact selected marker identity or quantized position.
- Foe resource symbol, foe type, and required count.

The spike must prove which of these values can be obtained and validated without teaching the server to execute the whole quest script.

The terms **same quest** and **same location** are not sufficient server keys. Coalescing applies only when the spike proves that the resolved objective descriptors identify the same physical quest action and encounter generation. Ambiguous matches must remain separate or unsupported rather than risk cross-crediting unrelated quests.

## Credit Policies To Evaluate

Credit must be based on server-authoritative state at the moment of death. Candidate requirements include:

- Objective is active and subscribed before death.
- Owner is connected.
- Owner is in the same authoritative world context or dungeon instance.
- Owner is within a configurable native-coordinate distance or dungeon-block neighborhood.
- Owner has been present for a minimum dwell time.
- Owner contributed valid damage, healing, or another participation signal.

The preferred starting policy for the author's server is presence-based credit: the objective holder must be in the same context and sufficiently near the encounter. Requiring the killing blow is undesirable because it discourages cooperation and enables kill stealing. Damage participation may be an optional stricter rule later.

Non-owners need no quest eligibility to attack the server-owned enemy. Owner eligibility gates spawning and quest credit, not ordinary combat interaction.

The spike must also define behavior for a subscriber who joins an encounter already in combat. Granting immediate credit for arriving just before death may be exploitable; waiting for the next generation, requiring dwell time, or requiring participation are possible policies.

## Death, Retry, And Respawn

The design must distinguish physical unload from confirmed death:

- **Despawned alive:** Location occupancy or interest changed. Preserve logical encounter state and recreate the live entity when appropriate.
- **Confirmed dead with eligible subscribers:** Deliver one idempotent result to each eligible objective.
- **Confirmed dead with absent or ineligible subscribers:** Keep those objectives unsatisfied. Decide whether and when the encounter re-arms for them.
- **Delivery pending:** Persist a death result until the owning character applies and acknowledges it.

Blind periodic boss respawn is not required. Re-arm only for an unsatisfied objective holder who becomes eligible after a cooldown. Owners credited by the previous death are removed before re-arm evaluation. Dead encounter generations need tombstones so duplicate messages or reconnects cannot award credit twice.

For objectives requiring several kills, each character retains an independent count. A shared encounter may need successive generations until every active subscriber reaches its own requirement.

## Cancellation And Disconnect

- Canceling, completing, or tombstoning a quest removes only that objective's registration or subscription.
- If other subscriptions remain, the shared encounter stays alive.
- If no subscriptions remain, an idle enemy can despawn immediately or after a short grace period.
- An enemy already observed or in combat should retire at a safe boundary rather than visibly disappear without explanation.
- Delayed deaths must never credit canceled or superseded objective generations.
- Short server tombstones should reject replayed registrations and result messages.
- Disconnect behavior must be explicit: retain the objective, suspend eligibility, and either retain or unload the physical enemy according to normal occupancy lifetime.

## Quest Items And Loot

Placed quest items should remain personal unless the spike demonstrates a reliable shared-item model. A single shared pickup creates stealing, loss, and respawn problems.

For quest items carried by a server-owned foe, evaluate personal loot delivery:

- The authoritative enemy dies once.
- Eligible owners receive independent quest-item instances linked to their own quest UID and resource symbol.
- Delivery is persisted and idempotent.
- Non-owners cannot consume another character's objective item.

## Distinct Quest Enemy Patterns

The spike should not assume all quest foes behave alike.

### Marker-Bound `PlaceFoe`

These are the strongest candidates for server ownership or encounter sharing because the quest resolves a site and marker. They still require validation of random site selection, marker identity, spawn counts, and action generations.

### Dynamic `CreateFoe`

These actions create timed or random waves near a player and are not attached to a stable marker. Their timing, chance rolls, placement, wave count, and task re-arming are local quest state. Candidate policies are:

- Keep them client-local permanently.
- Support owner-scoped server waves only.
- Coalesce only contemporaneous requests in the same context after a separate protocol is proven.
- Disable unsupported patterns while server-owned quest foes are enabled.

Dynamic encounters should not be folded into marker-bound coalescing during the first implementation.

## Architecture Constraints

Any later implementation must preserve DFMP's layer boundaries:

- Upstream edits are additive hooks only, remain free of networking logic, and are documented in `HOOKS.md`.
- Quest networking, validation, registries, persistence, and authority live under `Assets/DFMP/Runtime/`.
- World state and encounter registries live on server-owned services, never player prefabs.
- Client registrations are untrusted inputs. The server validates ownership, bounds, context, foe type, counts, and lifecycle transitions.
- Objective IDs, encounter IDs, and enemy entity IDs remain distinct concepts.
- Results are sequenced and idempotent across retries, disconnects, and server restarts.
- Interest management uses authoritative world context and occupancy rather than Unity transform distance alone.

The Phase 1 local quest PvE exception must remain a distinct damage source category so a later server-owned design can remove it without changing general PvE or PvP authority rules.

## Scale And Abuse Bounds

Even if the private beta starts at 8-16 players, the chosen model should have explicit limits:

- Maximum active quest-enemy objectives per character.
- Registration and cancellation rate limits.
- Bounded foe counts and dynamic-wave counts.
- Expiry for canceled, completed, dead, and inactive records.
- Context-scoped observer delivery.
- Diagnostic logging for equivalent objectives occupying the same resolved marker.
- Metrics for duplicate encounters, missed credit, retries, orphaned enemies, and pending acknowledgements.

For MVP, duplicate server quest enemies are avoided entirely by keeping them local. If Candidate B is prototyped later, equivalent-descriptor logging should measure whether duplicate physical encounters are common enough to justify Candidate C.

## Spike Experiments

1. Instrument two independent instances of the same quest template and compare their resolved branch, site, marker, foe resource, and action identity.
2. Repeat across fixed quests, random guild quests, building targets, dungeon targets, multi-foe objectives, and re-armed tasks.
3. Inventory every native quest action that creates or places foes and classify it as marker-bound, dynamic, or unsupported.
4. Prototype a narrow marker-bound registration hook without networking the full quest graph.
5. Test two personal objectives against one physical server enemy and apply separate idempotent kill results.
6. Test owner presence, owner absence, a non-owner killing the enemy, late arrival before and after death, disconnect, reconnect, cancellation, location unload, and server restart.
7. Test two owner-scoped identical enemies at one marker and evaluate actual player confusion before rejecting or accepting Candidate B.
8. Measure how often independently instantiated quests resolve to equivalent encounter descriptors during representative beta play.
9. Verify quest-item save and restore identity before testing foe-carried quest loot.
10. Verify that credited owners cannot trigger another generation while an absent unsatisfied owner can trigger one on a later eligible arrival.
11. Record expected headless and graphical log evidence for every supported lifecycle transition.

## Decision Criteria

The spike is complete when it produces:

- A supported quest-enemy action matrix.
- A precise objective and encounter identity model.
- A documented activation, credit radius, despawn, re-arm, cancellation, and disconnect policy.
- A persistence and restore-order contract.
- Evidence that wrong-target kills and duplicate encounters are acceptably rare or structurally prevented.
- Bounds for malformed or abusive client registrations.
- Focused EditMode test cases and a two-client graphical smoke-test plan.
- A recommendation to retain client-local enemies, implement owner-scoped enemies, implement shared encounters, or use a hybrid by quest action type.

Until those criteria are met, client-local quest enemies are the authoritative design decision.
