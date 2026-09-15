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

        [SyncVar]
        Vector3 dungeonLocalPosition;

        [SyncVar]
        float facingYaw;

        [SyncVar]
        int mobileType;

        [SyncVar]
        int gender;

        [SyncVar]
        int reaction;

        [SyncVar]
        int classicSpawnDistanceType;

        [SyncVar]
        int targetConnectionId;

        [SyncVar]
        bool isMoving;

        [SyncVar]
        int attackSequence;

        [SyncVar]
        int attackKind;

        public DFMPDynamicEnemyProviderKind ProviderKind { get { return (DFMPDynamicEnemyProviderKind)providerKind; } }
        public string EncounterId { get { return encounterId; } }
        public int RosterIndex { get { return rosterIndex; } }
        public string EnemyId { get { return enemyId; } }
        public DFMPDynamicEnemyLifecycleState LifecycleState { get { return (DFMPDynamicEnemyLifecycleState)lifecycleState; } }
        public Vector3 DungeonLocalPosition { get { return dungeonLocalPosition; } }
        public float FacingYaw { get { return facingYaw; } }
        public int MobileType { get { return mobileType; } }
        public int Gender { get { return gender; } }
        public int Reaction { get { return reaction; } }
        public int ClassicSpawnDistanceType { get { return classicSpawnDistanceType; } }
        public int TargetConnectionId { get { return targetConnectionId; } }
        public bool IsMoving { get { return isMoving; } }
        public int AttackSequence { get { return attackSequence; } }
        public DFMPDynamicEnemyAttackKind AttackKind { get { return (DFMPDynamicEnemyAttackKind)attackKind; } }
        public DFMPDynamicEnemyDescriptor Descriptor { get { return new DFMPDynamicEnemyDescriptor { DungeonLocalPosition = dungeonLocalPosition, FacingYaw = facingYaw, MobileType = mobileType, Gender = gender, Reaction = reaction, ClassicSpawnDistanceType = classicSpawnDistanceType }; } }

        public void SetLifecycleState(DFMPDynamicEnemyLifecycleState state)
        {
            lifecycleState = (int)state;
            if (state != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
            {
                targetConnectionId = -1;
                isMoving = false;
            }
        }

        public void SetAiState(DFMPDynamicEnemyDescriptor descriptor, int newTargetConnectionId, bool newIsMoving)
        {
            dungeonLocalPosition = descriptor.DungeonLocalPosition;
            facingYaw = descriptor.FacingYaw;
            mobileType = descriptor.MobileType;
            gender = descriptor.Gender;
            reaction = descriptor.Reaction;
            classicSpawnDistanceType = descriptor.ClassicSpawnDistanceType;
            targetConnectionId = newTargetConnectionId;
            isMoving = newIsMoving;
        }

        public void SetAttackState(DFMPDynamicEnemyAttackKind newAttackKind)
        {
            attackKind = (int)newAttackKind;
            attackSequence++;
        }

        public void Initialize(DFMPDynamicEnemyRecord record)
        {
            providerKind = (int)record.Identity.Encounter.ProviderKind;
            encounterId = record.Identity.Encounter.EncounterId ?? string.Empty;
            rosterIndex = record.Identity.RosterIndex;
            enemyId = record.Identity.EnemyId ?? string.Empty;
            lifecycleState = (int)record.LifecycleState;
            dungeonLocalPosition = record.Descriptor.DungeonLocalPosition;
            facingYaw = record.Descriptor.FacingYaw;
            mobileType = record.Descriptor.MobileType;
            gender = record.Descriptor.Gender;
            reaction = record.Descriptor.Reaction;
            classicSpawnDistanceType = record.Descriptor.ClassicSpawnDistanceType;
            targetConnectionId = record.TargetConnectionId;
            isMoving = record.IsMoving;
            attackSequence = 0;
            attackKind = (int)DFMPDynamicEnemyAttackKind.Melee;
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
