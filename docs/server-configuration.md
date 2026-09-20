# Server configuration

Dedicated servers load `dfmp-server.json` from the **current working directory** (`Directory.GetCurrentDirectory()`). If the file is missing, DFMP writes one with built-in defaults.

Ship and copy [`dfmp-server.example.json`](../dfmp-server.example.json). Keep your real `dfmp-server.json` out of git.

CLI flags override JSON **only when the parsed CLI value is not the parser default** (for example `-port` only wins if it is not `7777`). Prefer editing JSON for anything you care about.

Restart the server after changing JSON.

## Identity and authentication

`Identity.Mode` (string):

| Value | Meaning |
| --- | --- |
| `open` | No credential. Fine for LAN and development. The server logs a warning at boot. Default for a missing/blank mode. |
| `discord` | Per-server Discord OAuth2 loopback flow. Requires `DFMP_DISCORD_CLIENT_ID` and `DFMP_DISCORD_CLIENT_SECRET`. |
| `server_local` | PBKDF2 local account store. Editor Development Session can exercise it; there is no full player-facing login UI yet. |
| `dfmp` | Reserved / unsupported. |
| anything else | Fails closed to `server_local` so a typo does not disable auth. |

Other `Identity` fields:

| Field | Meaning |
| --- | --- |
| `ServerWorldId` | World id stored with characters (default `default`). |
| `AllowSelfRegistration` | Local-account registration policy. |
| `WhitelistEnabled` | When true, only `AllowedAccountIds` may join. |
| `AllowedAccountIds` | Account ids (`discord:<snowflake>` in Discord mode). |
| `AdminAccountIds` / `ModeratorAccountIds` | Role assignment. |
| `DiscordGuildId` | Optional guild for role checks (needs bot token). |
| `DiscordAllowedRoleIds` | If non-empty, the player must have one of these Discord roles. |
| `DiscordRedirectUri` | Loopback redirect with explicit port. Default `http://127.0.0.1:53682/dfmp-auth`. |
| `CharacterSelectEnabled` | Character select at join. |
| `MaxCharactersPerAccount` | 1–16, default 4. |
| `AllowCharacterDelete` | Whether players may delete characters. |

Secrets never belong in this file. Use environment variables for Discord.

## Top-level server identity

| Field | Default | Meaning |
| --- | --- | --- |
| `ServerName` | `My DFMP Server` | Shown in discovery and join notices. |
| `Port` | `7777` | KCP game port. |
| `DiscoveryPort` | `7778` | UDP LAN discovery (`DFMP_PING`). |
| `MaxConnections` | `16` | Cap 128. |
| `TickRate` | `30` | 10–120. |
| `HeartbeatInterval` | `5` | Seconds. |
| `LanDiscoveryEnabled` | `true` | Forced true on normalize today. |
| `Motd` | Welcome string | Message of the day. |
| `Chat.MaxMessageLength` | `256` | |
| `Chat.MinIntervalSeconds` | `1` | Chat rate limit. |

## Gameplay, combat, rest, start location

| Section | Notes |
| --- | --- |
| `Gameplay.EnableBeginnerTutorial` | Default false. |
| `Combat.PvpEnabled` | Player vs player. |
| `Combat.MaximumDamagePerHit` | Clamp for validated hits. |
| `Combat.MaximumRangedPvpRange` | |
| `Combat.DamageCooldownSeconds` / `DamageRateWindowSeconds` / `MaximumDamageRequestsPerWindow` | Anti-spam on damage intents. |
| `Rest.Policy` | `ServerManaged` or `Disabled`. |
| `StartingLocation.Mode` | `LocationCenter` (default), `ExplicitWorldCoordinates`, `NamedStartMarker`, `Scripted`. |
| `StartingLocation.RegionName` / `LocationName` / `MarkerName` | Used with location-center / named marker. |
| `StartingLocation.WorldX` / `WorldY` / `WorldZ` | Used with explicit coordinates. |

## Enemies

`Enemies.EnemyRosterMode`: `NativeParity` (default path) or `ConfiguredRoster`. Other values normalize to `ConfiguredRoster`.

| Field | Default | Meaning |
| --- | --- | --- |
| `WorldSeed` | `default` | Shared roster seed. |
| `NativeRandomSelectionMode` | `Classic` | Normalize currently forces Classic. |
| `NativeMonsterPower` / `NativeMonsterVariance` | `0` / `4` | Native roster knobs. |
| `DungeonRosterSize` | code default `8` | Size of dungeon enemy roster. |
| `DespawnDelaySeconds` | `0` | |
| `AwarenessRange` / `AttackRange` / `RangedAttackRange` / `MagicAttackRange` | ranges | AI distances. |
| `MoveSpeed` / `AiTickIntervalSeconds` / `AttackCooldownSeconds` / `AttackDamage` | movement and attacks | |
| `PlayerMeleeDamageRange` / `PlayerRangedDamageRange` | player hit ranges | |
| `RequireLineOfSight` | `true` | |

## Quests

`Quests.Mode` must be `ClientOwned` on current builds. `Shared` is reserved and rejected at validate.

Other fields cap how many objectives/foes a character can register, payload size, autosave interval, owner cues, and proximity activation/despawn for quest enemies. See [quest-enemy-networking-spike.md](quest-enemy-networking-spike.md).

## Developer commands

`Developer.CommandsEnabled` (default **false**). When true, console commands such as `dfmp_infect_self` work. Leave this off on any server with untrusted players. See [developer-commands.md](developer-commands.md).

## Command-line flags

Parsed by `ServerCommandLineArgs`. Dedicated-server detection:

- `-server`, `-dedicated` (and `--` variants)
- `-batchmode` with neither `-client` nor `-server` still starts a dedicated server
- `-client` / `-connect` starts a client instead

Client-oriented: `-address` / `-host`, `-account` / `-identity`, `-dfmp-session <file>`.

Spike-only: `-dfmp-dungeon-spike`, `-dfmp-dungeon-spike-count`.

## Persistence and logs

- Characters: `Application.persistentDataPath/DFMP/Characters/`
- Local accounts (`server_local`): `.../DFMP/Accounts/`
- Server log: `Logs/DFMP/server.log`
