using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPWorldSettings : NetworkBehaviour
    {
        public const uint AssetId = 0xdfc004u;

        [SyncVar(hook = nameof(OnRestPolicyChanged))]
        string restPolicy = DFMPRestPolicies.Disabled;

        [SyncVar(hook = nameof(OnFailureDeadlinesEnabledChanged))]
        bool failureDeadlinesEnabled;

        [SyncVar]
        float questAutosaveIntervalSeconds = DFMPServerQuestConfig.DefaultAutosaveIntervalSeconds;

        [SyncVar]
        bool showQuestEnemyOwnerCue = true;

        public static DFMPWorldSettings Local { get; private set; }

        public string RestPolicy
        {
            get { return DFMPServerRestConfig.NormalizePolicy(restPolicy); }
        }

        public static string CurrentRestPolicy
        {
            get
            {
                return Local != null
                    ? Local.RestPolicy
                    : DFMPRestPolicies.Disabled;
            }
        }

        public bool FailureDeadlinesEnabled
        {
            get { return failureDeadlinesEnabled; }
        }

        public static bool CurrentFailureDeadlinesEnabled
        {
            get { return Local != null && Local.FailureDeadlinesEnabled; }
        }

        public static float CurrentQuestAutosaveIntervalSeconds
        {
            get
            {
                return Local != null
                    ? Mathf.Max(10f, Local.questAutosaveIntervalSeconds)
                    : DFMPServerQuestConfig.DefaultAutosaveIntervalSeconds;
            }
        }

        public static bool CurrentShowQuestEnemyOwnerCue
        {
            get { return Local == null || Local.showQuestEnemyOwnerCue; }
        }

        public static void RegisterClientSpawnHandler()
        {
            NetworkClient.RegisterSpawnHandler(AssetId, SpawnClientWorldSettings, UnspawnClientWorldSettings);
        }

        public void SetRestPolicy(string policy)
        {
            if (!NetworkServer.active)
                return;

            string normalized = DFMPServerRestConfig.NormalizePolicy(policy);
            if (restPolicy == normalized)
                return;

            restPolicy = normalized;
            Debug.Log($"[DFMP Rest] World rest policy set: policy={restPolicy}.");
        }

        public void SetFailureDeadlinesEnabled(bool enabled)
        {
            if (!NetworkServer.active)
                return;

            failureDeadlinesEnabled = enabled;
            Debug.Log($"[DFMP Quest] World failure deadline policy set: enabled={enabled}.");
        }

        public void SetQuestAutosaveInterval(float intervalSeconds)
        {
            if (!NetworkServer.active)
                return;
            questAutosaveIntervalSeconds = Mathf.Clamp(intervalSeconds, 10f, 3600f);
        }

        public void SetShowQuestEnemyOwnerCue(bool enabled)
        {
            if (NetworkServer.active)
                showQuestEnemyOwnerCue = enabled;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Local = this;
            Debug.Log(
                $"[DFMP World] Client received world settings: restPolicy={RestPolicy}, " +
                $"failureDeadlinesEnabled={FailureDeadlinesEnabled}, " +
                $"questAutosaveIntervalSeconds={questAutosaveIntervalSeconds:0.##}.");
        }

        public override void OnStopClient()
        {
            if (Local == this)
                Local = null;

            base.OnStopClient();
        }

        void OnRestPolicyChanged(string oldValue, string newValue)
        {
            Debug.Log($"[DFMP Rest] Client rest policy update: policy={DFMPServerRestConfig.NormalizePolicy(newValue)}.");
        }

        void OnFailureDeadlinesEnabledChanged(bool oldValue, bool newValue)
        {
            Debug.Log($"[DFMP Quest] Client failure deadline policy update: enabled={newValue}.");
        }

        static GameObject SpawnClientWorldSettings(SpawnMessage message)
        {
            GameObject settingsGo = new GameObject("DFMP_WorldSettings");
            settingsGo.SetActive(false);
            settingsGo.AddComponent<NetworkIdentity>();
            settingsGo.AddComponent<DFMPWorldSettings>();
            Object.DontDestroyOnLoad(settingsGo);
            settingsGo.SetActive(true);
            Debug.Log($"[DFMP Rest] Client spawned world settings: netId={message.netId}.");
            return settingsGo;
        }

        static void UnspawnClientWorldSettings(GameObject spawned)
        {
            if (spawned != null)
                Destroy(spawned);
        }
    }
}
