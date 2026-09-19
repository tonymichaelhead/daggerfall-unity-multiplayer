using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using Mirror;
using System;
using UnityEngine;
using Wenzil.Console;

namespace DFMP.Runtime
{
    public static class DFMPDeveloperCommandController
    {
        static bool registered;

        public static void RegisterClient()
        {
            if (registered)
                return;

            try
            {
                NetworkClient.RegisterHandler<DFMPDeveloperInfectSelfResponse>(OnInfectSelfResponse);
                ConsoleCommandsDatabase.RegisterCommand(
                    "dfmp_infect_self",
                    "Ask the DFMP server to infect this player with vampirism.",
                    "dfmp_infect_self",
                    InfectSelf);
                NetworkClient.RegisterHandler<DFMPDeveloperAdvanceTimeResponse>(OnAdvanceTimeResponse);
                ConsoleCommandsDatabase.RegisterCommand(
                    "dfmp_advance_time",
                    "Ask the DFMP server to advance shared time by minutes.",
                    "dfmp_advance_time <minutes>",
                    AdvanceTime);
                NetworkClient.RegisterHandler<DFMPDeveloperDamagePlayerResponse>(OnDamagePlayerResponse);
                ConsoleCommandsDatabase.RegisterCommand(
                    "dfmp_damage_player",
                    "Ask the DFMP server to apply bounded PvP damage to another player.",
                    "dfmp_damage_player <connectionId> <amount>",
                    DamagePlayer);
                ConsoleCommandsDatabase.RegisterCommand(
                    "dfmp_damage_self",
                    "Ask the DFMP server to apply bounded local quest PvE damage to this player.",
                    "dfmp_damage_self <amount>",
                    DamageSelf);
                NetworkClient.RegisterHandler<DFMPDeveloperGodModeResponse>(OnGodModeResponse);
                ConsoleCommandsDatabase.RegisterCommand(
                    "dfmp_godmode",
                    "Toggle server-authorized invulnerability for this player.",
                    "dfmp_godmode <on|off>",
                    GodMode);
                NetworkClient.RegisterHandler<DFMPDeveloperTeleportResponse>(OnTeleportResponse);
                ConsoleCommandsDatabase.RegisterCommand(
                    "dfmp_teleport",
                    "Teleport this player to a named location using the same exterior destination as the V travel menu.",
                    "dfmp_teleport <region> <location>",
                    Teleport);
                NetworkClient.RegisterHandler<DFMPDeveloperTeleportDungeonResponse>(OnTeleportDungeonResponse);
                ConsoleCommandsDatabase.RegisterCommand(
                    "dfmp_teleport_dungeon",
                    "Teleport this player into a named dungeon interior for testing (not the V travel exterior).",
                    "dfmp_teleport_dungeon <region> <location>",
                    TeleportDungeon);
                registered = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Developer] Could not register developer commands: {ex.Message}.");
            }
        }

        static string InfectSelf(params string[] args)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return "DFMP client is not connected and ready.";

            NetworkClient.Send(new DFMPDeveloperInfectSelfRequest());
            return "Requested server vampirism infection.";
        }

        static void OnInfectSelfResponse(DFMPDeveloperInfectSelfResponse response)
        {
            if (!response.Accepted)
            {
                Debug.LogWarning($"[DFMP Developer] Server rejected infection command: {response.Reason}.");
                return;
            }

            if (!GameManager.HasInstance || GameObject.FindGameObjectWithTag("Player") == null)
            {
                Debug.LogWarning("[DFMP Developer] Cannot apply infection because the local player is unavailable.");
                return;
            }

            EntityEffectBundle bundle = GameManager.Instance.PlayerEffectManager.CreateDisease("Vampirism-Infection");
            GameManager.Instance.PlayerEffectManager.AssignBundle(bundle, AssignBundleFlags.SpecialInfection);
            Debug.Log("[DFMP Developer] Applied server-authorized vampirism infection.");
        }

        static string AdvanceTime(params string[] args)
        {
            int minutes;
            if (args == null || args.Length != 1 || !int.TryParse(args[0], out minutes))
                return "Usage: dfmp_advance_time <minutes>";
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return "DFMP client is not connected and ready.";

            NetworkClient.Send(new DFMPDeveloperAdvanceTimeRequest { Minutes = minutes });
            return $"Requested server time advance: minutes={minutes}.";
        }

        static void OnAdvanceTimeResponse(DFMPDeveloperAdvanceTimeResponse response)
        {
            if (response.Accepted)
                Debug.Log($"[DFMP Developer] Server advanced shared time: minutes={response.Minutes}.");
            else
                Debug.LogWarning($"[DFMP Developer] Server rejected time advance: reason={response.Reason}.");
        }

        static string DamagePlayer(params string[] args)
        {
            int targetConnectionId;
            int amount;
            if (args == null || args.Length != 2 || !int.TryParse(args[0], out targetConnectionId) || !int.TryParse(args[1], out amount))
                return "Usage: dfmp_damage_player <connectionId> <amount>";
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return "DFMP client is not connected and ready.";

            NetworkClient.Send(new DFMPDeveloperDamagePlayerRequest
            {
                TargetConnectionId = targetConnectionId,
                Amount = amount
            });
            return $"Requested PvP damage: target={targetConnectionId}, amount={amount}.";
        }

        static void OnDamagePlayerResponse(DFMPDeveloperDamagePlayerResponse response)
        {
            if (response.Accepted)
                Debug.Log($"[DFMP Developer] Server accepted PvP damage request: target={response.TargetConnectionId}, amount={response.Amount}. Check the server combat log for application or policy rejection.");
            else
                Debug.LogWarning($"[DFMP Developer] Server rejected PvP damage: target={response.TargetConnectionId}, amount={response.Amount}, reason={response.Reason}.");
        }

        static string DamageSelf(params string[] args)
        {
            int amount;
            if (args == null || args.Length != 1 || !int.TryParse(args[0], out amount))
                return "Usage: dfmp_damage_self <amount>";
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return "DFMP client is not connected and ready.";

            NetworkClient.Send(new DFMPDeveloperDamageSelfRequest { Amount = amount });
            return $"Requested local quest PvE damage: amount={amount}.";
        }


        static string GodMode(params string[] args)
        {
            if (args == null || args.Length != 1 || (!string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase) && !string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase)))
                return "Usage: dfmp_godmode <on|off>";
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return "DFMP client is not connected and ready.";

            bool enabled = string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase);
            NetworkClient.Send(new DFMPDeveloperGodModeRequest { Enabled = enabled });
            return $"Requested server godmode: enabled={enabled}.";
        }

        static void OnGodModeResponse(DFMPDeveloperGodModeResponse response)
        {
            if (response.Accepted)
                Debug.Log($"[DFMP Developer] Godmode {(response.Enabled ? "enabled" : "disabled") }.");
            else
                Debug.LogWarning($"[DFMP Developer] Server rejected godmode: reason={response.Reason}.");
        }

        static string Teleport(params string[] args)
        {
            string locationName;
            string usageError;
            if (!TryParseRegionLocationArgs(args, "dfmp_teleport", out locationName, out usageError))
                return usageError;
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return "DFMP client is not connected and ready.";

            NetworkClient.Send(new DFMPDeveloperTeleportRequest
            {
                RegionName = args[0],
                LocationName = locationName
            });
            return $"Requested location teleport: region='{args[0]}', location='{locationName}'.";
        }

        static void OnTeleportResponse(DFMPDeveloperTeleportResponse response)
        {
            if (response.Accepted)
                Debug.Log($"[DFMP Developer] Location teleport accepted: region='{response.RegionName}', location='{response.LocationName}'.");
            else
                Debug.LogWarning($"[DFMP Developer] Location teleport rejected: region='{response.RegionName}', location='{response.LocationName}', reason={response.Reason}.");
        }

        static string TeleportDungeon(params string[] args)
        {
            string locationName;
            string usageError;
            if (!TryParseRegionLocationArgs(args, "dfmp_teleport_dungeon", out locationName, out usageError))
                return usageError;
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return "DFMP client is not connected and ready.";

            NetworkClient.Send(new DFMPDeveloperTeleportDungeonRequest
            {
                RegionName = args[0],
                LocationName = locationName
            });
            return $"Requested dungeon teleport: region='{args[0]}', location='{locationName}'.";
        }

        static void OnTeleportDungeonResponse(DFMPDeveloperTeleportDungeonResponse response)
        {
            if (response.Accepted)
                Debug.Log($"[DFMP Developer] Dungeon teleport accepted: region='{response.RegionName}', location='{response.LocationName}'.");
            else
                Debug.LogWarning($"[DFMP Developer] Dungeon teleport rejected: region='{response.RegionName}', location='{response.LocationName}', reason={response.Reason}.");
        }

        static bool TryParseRegionLocationArgs(string[] args, string commandName, out string locationName, out string usageError)
        {
            locationName = null;
            usageError = $"Usage: {commandName} <region> <location>";
            if (args == null || args.Length < 2 || string.IsNullOrWhiteSpace(args[0]))
                return false;

            locationName = string.Join(" ", args, 1, args.Length - 1).Trim('"');
            if (string.IsNullOrWhiteSpace(locationName))
                return false;

            usageError = null;
            return true;
        }
    }
}
