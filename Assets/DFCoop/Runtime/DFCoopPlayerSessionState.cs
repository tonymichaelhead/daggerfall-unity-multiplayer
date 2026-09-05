using Mirror;
using UnityEngine;

namespace DFCoop.Runtime
{
    public class DFCoopPlayerSessionState : NetworkBehaviour
    {
        public const uint AssetId = 0xdfc002u;

        [SyncVar]
        int connectionId;

        [SyncVar]
        int worldX;

        [SyncVar]
        float worldY;

        [SyncVar]
        int worldZ;

        public int ConnectionId
        {
            get { return connectionId; }
        }

        public int WorldX
        {
            get { return worldX; }
        }

        public float WorldY
        {
            get { return worldY; }
        }

        public int WorldZ
        {
            get { return worldZ; }
        }

        public void Initialize(int ownerConnectionId, int initialWorldX, float initialWorldY, int initialWorldZ)
        {
            connectionId = ownerConnectionId;
            worldX = initialWorldX;
            worldY = initialWorldY;
            worldZ = initialWorldZ;
        }

        public static void RegisterClientSpawnHandler()
        {
            NetworkClient.RegisterSpawnHandler(AssetId, SpawnClientSessionState, UnspawnClientSessionState);
        }

        static GameObject SpawnClientSessionState(SpawnMessage message)
        {
            GameObject sessionGo = new GameObject("DFCoop_PlayerSessionState");
            sessionGo.SetActive(false);

            sessionGo.AddComponent<NetworkIdentity>();
            sessionGo.AddComponent<DFCoopPlayerSessionState>();
            Object.DontDestroyOnLoad(sessionGo);
            sessionGo.SetActive(true);

            Debug.Log($"[DFCoop Session] Client spawned player session state: netId={message.netId}.");
            return sessionGo;
        }

        static void UnspawnClientSessionState(GameObject spawned)
        {
            if (spawned != null)
                Destroy(spawned);
        }
    }
}