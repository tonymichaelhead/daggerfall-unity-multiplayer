using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPDynamicEnemyState : NetworkBehaviour
    {
        public const uint AssetId = 0xdfc003u;

        [SyncVar]
        int providerKind;

        [SyncVar]
        string encounterId;

        [SyncVar]
        int rosterIndex;

        [SyncVar]
        string enemyId;

        [SyncVar]
        int lifecycleState;

        public DFMPDynamicEnemyProviderKind ProviderKind { get { return (DFMPDynamicEnemyProviderKind)providerKind; } }
        public string EncounterId { get { return encounterId; } }
        public int RosterIndex { get { return rosterIndex; } }
        public string EnemyId { get { return enemyId; } }
        public DFMPDynamicEnemyLifecycleState LifecycleState { get { return (DFMPDynamicEnemyLifecycleState)lifecycleState; } }

        public void Initialize(DFMPDynamicEnemyRecord record)
        {
            providerKind = (int)record.Identity.Encounter.ProviderKind;
            encounterId = record.Identity.Encounter.EncounterId ?? string.Empty;
            rosterIndex = record.Identity.RosterIndex;
            enemyId = record.Identity.EnemyId ?? string.Empty;
            lifecycleState = (int)record.LifecycleState;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"[DFMP Enemy] Client received state: enemyId={EnemyId}, encounterId={EncounterId}, lifecycle={LifecycleState}.");
        }

        public static void RegisterClientSpawnHandler()
        {
            NetworkClient.RegisterSpawnHandler(AssetId, SpawnClientState, UnspawnClientState);
        }

        static GameObject SpawnClientState(SpawnMessage message)
        {
            GameObject enemyGo = new GameObject("DFMP_DynamicEnemyState");
            enemyGo.SetActive(false);
            enemyGo.AddComponent<NetworkIdentity>();
            enemyGo.AddComponent<DFMPWorldContextCarrier>();
            enemyGo.AddComponent<DFMPDynamicEnemyState>();
            Object.DontDestroyOnLoad(enemyGo);
            enemyGo.SetActive(true);
            return enemyGo;
        }

        static void UnspawnClientState(GameObject spawned)
        {
            if (spawned != null)
            {
                DFMPDynamicEnemyState state = spawned.GetComponent<DFMPDynamicEnemyState>();
                Debug.Log($"[DFMP Enemy] Client removed state: enemyId={(state != null ? state.EnemyId : string.Empty)}.");
                Destroy(spawned);
            }
        }
    }
}
