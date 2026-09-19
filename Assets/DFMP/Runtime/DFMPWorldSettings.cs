using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPWorldSettings : NetworkBehaviour
    {
        public const uint AssetId = 0xdfc004u;

        [SyncVar(hook = nameof(OnRestPolicyChanged))]
        string restPolicy = DFMPRestPolicies.Disabled;

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

        public override void OnStartClient()
        {
            base.OnStartClient();
            Local = this;
            Debug.Log($"[DFMP Rest] Client received world settings: restPolicy={RestPolicy}.");
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
