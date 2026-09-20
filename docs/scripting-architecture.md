# DFMP Scripting and Customization Architecture

This document outlines the architectural strategy for DFMP server-side scripting, client interaction, and modding extensibility planned for **Phase 2 (Milestone R2)**.

---

## 1. Core Architectural Decision

**DFMP uses pure server-side scripting with a server-driven UI protocol and native `.dfmod` support.**
Arbitrary networked client scripting (e.g., streaming unverified scripts across the network to execute on player clients) is explicitly rejected.

### Summary of Extensibility Tiers

| Extensibility Tier | Mechanism | Target Use Cases |
| :--- | :--- | :--- |
| **Server Logic & State** | Embedded Server Scripting Engine (e.g. Lua) | Game modes, rules, event hooks, custom mechanics, admin tools, economy, teleportation, persistence. |
| **Dynamic Player Interaction** | Server-Driven Generic UI Protocol | Login/registration popups, confirmation dialogs, list selections, banking menus, fast-travel selectors. |
| **Heavy Client Customization** | Upstream DFU `.dfmod` packages | Custom 3D models, audio replacements, custom shaders, texture packs, bespoke client-side HUD additions. |

---

## 2. Why Pure Server-Side Scripting?

1. **Security & Sandboxing:**
   Executing arbitrary networked code on player clients introduces significant vulnerabilities and complex security requirements. Keeping user code on the server keeps the client runtime secure, stable, and deterministic.
2. **Single Source of Truth:**
   World state, economy, stats, and rules remain 100% authoritative on the server.
3. **Rebasability & Simplicity:**
   Avoids creating a bespoke networked client virtual machine, keeping the core client footprint minimal and aligned with upstream DFU.
4. **Precedent (TES3MP Model):**
   TES3MP demonstrated that an authoritative server-side scripting layer paired with generic UI dialog packets can support complex RPG servers (RP rules, banking, property ownership, custom factions) without dynamic client-side scripting.

---

## 3. Tier 1: Server-Side Scripting Layer (Milestone R2)

The server loads scripts from a dedicated server-side folder (e.g. `Server/Scripts/`).

### Responsibilities
- **Event Bus Subscriptions:** Subscribes to lifecycle, player, and world events dispatched by the DFMP server event bus (`PlayerConnected`, `PlayerSpawned`, `LocationEntered`, `PlayerDamaged`, `PlayerDied`, `EnemyDied`, `ChatMessageReceived`, etc.).
- **Server Action API:** Executes world actions through a strictly validated server boundary (teleport player, grant gold/items, spawn encounter, adjust game time, kick/ban).
- **Persistence Storage:** Accesses per-player and global key-value data backed by the M5 persistence store.
- **Error Sandboxing:** Faulty scripts log errors and are safely disabled without crashing the server tick. Hot-reloading allows server owners to iterate without restarts.

---

## 4. Tier 2: Server-Driven UI Protocol

To allow server scripts to display custom interactive menus without requiring players to download custom client code, DFMP provides generic UI message types:

1. **Message Box / Alert:** Server sends title, message text, and button options (e.g. `[OK]`, `[Cancel]`, `[Accept]`, `[Decline]`).
2. **List / Selection Box:** Server sends an array of string choices (e.g. fast travel destinations, shop categories, faction ranks).
3. **Input Prompt:** Server requests freeform text or numeric input (e.g. password prompt, gold transfer amount).

### Workflow

```
[ Server Script ]
       │
       ▼ (Send ShowGenericUIMessage { id: 42, type: ListBox, options: [...] })
[ DFMP Client ] ──► Renders native DFU UI Window (no mod required)
       │
       ▼ (Player selects item index 2)
[ Server EventBus ] ──► Raises OnPlayerGUIResponse(playerId, 42, selectedIndex: 2)
       │
       ▼
[ Server Script ] ──► Processes player selection
```

---

## 5. Tier 3: Client Extensibility via `.dfmod`

When a server requires assets or complex client-side rendering changes:
- Server owners build and distribute standard DFU `.dfmod` packages.
- Client `.dfmod`s can listen to and send custom Mirror network messages via a registered channel if two-way communication is needed.
- In Phase 2, optional server configuration can declare required mod names and checksums to ensure client asset compatibility upon joining.
