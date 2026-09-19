using System.Collections.Generic;
using DFMP.Hooks;
using DaggerfallWorkshop.Game.Questing;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPQuestClockController
    {
        static readonly HashSet<string> warnedUnknownClocks = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            DaggerfallHooks.ShouldSuppressQuestClockExpiry = ShouldSuppressQuestClockExpiry;
        }

        static bool ShouldSuppressQuestClockExpiry(object clockObject)
        {
            Clock clock = clockObject as Clock;
            if (clock == null || clock.ParentQuest == null || clock.Symbol == null)
                return false;

            Quest quest = clock.ParentQuest;
            Task task = quest.GetTask(clock.Symbol);
            var actionTypeNames = new List<string>();
            if (task != null)
            {
                foreach (IQuestAction action in task.Actions)
                {
                    if (action != null)
                        actionTypeNames.Add(action.GetType().Name);
                }
            }

            DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                quest.QuestName,
                clock.Symbol.Original,
                actionTypeNames);

            if (kind == DFMPQuestClockKind.Unknown)
            {
                string identity = quest.QuestName + ":" + clock.Symbol.Original;
                if (warnedUnknownClocks.Add(identity))
                {
                    Debug.LogWarning(
                        $"[DFMP Quest] Unknown or altered quest clock '{identity}'. " +
                        "Suppressing expiry because deadline suppression cannot classify it safely; " +
                        "this quest may require a compatibility manifest entry.");
                }
                return NetworkClient.isConnected &&
                    !DFMPWorldSettings.CurrentFailureDeadlinesEnabled;
            }

            bool suppress = DFMPQuestClockPolicy.ShouldSuppress(
                NetworkClient.isConnected,
                DFMPWorldSettings.CurrentFailureDeadlinesEnabled,
                kind);
            if (suppress)
            {
                Debug.Log(
                    $"[DFMP Quest] Suppressed failure deadline: quest={quest.QuestName}, " +
                    $"clock={clock.Symbol.Original}.");
            }

            return suppress;
        }
    }
}
