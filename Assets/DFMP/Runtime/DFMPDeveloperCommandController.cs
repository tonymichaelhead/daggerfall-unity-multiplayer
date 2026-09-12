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
    }
}
