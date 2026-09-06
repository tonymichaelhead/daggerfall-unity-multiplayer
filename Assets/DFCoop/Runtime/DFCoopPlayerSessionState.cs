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

        [SyncVar]
        bool spawnConfirmed;

        [SyncVar]
        string displayName = "Player";

        [SyncVar]
        float facingYaw;

        [SyncVar]
        int race;

        [SyncVar]
        int gender;

        [SyncVar]
        int outfitVariant;

        [SyncVar]
        int faceVariant;

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

        public bool SpawnConfirmed
        {
            get { return spawnConfirmed; }
        }

        public string DisplayName
        {
            get { return displayName; }
        }

        public float FacingYaw
        {
            get { return facingYaw; }
        }

        public int Race
        {
            get { return race; }
        }

        public int Gender
        {
            get { return gender; }
        }

        public int OutfitVariant
        {
            get { return outfitVariant; }
        }

        public int FaceVariant
        {
            get { return faceVariant; }
        }

        public void Initialize(int ownerConnectionId, int initialWorldX, float initialWorldY, int initialWorldZ)
        {
            connectionId = ownerConnectionId;
            worldX = initialWorldX;
            worldY = initialWorldY;
            worldZ = initialWorldZ;
        }

        public void ConfirmSpawn()
        {
            spawnConfirmed = true;
        }

        public void SetPosition(int newWorldX, float newWorldY, int newWorldZ)
        {
            worldX = newWorldX;
            worldY = newWorldY;
            worldZ = newWorldZ;
        }

        public void SetDisplayName(string newDisplayName)
        {
            displayName = newDisplayName;
        }

        public void SetFacingYaw(float newFacingYaw)
        {
            facingYaw = newFacingYaw;
        }

        public void SetAppearance(int newRace, int newGender, int newOutfitVariant, int newFaceVariant)
        {
            race = newRace;
            gender = newGender;
            outfitVariant = newOutfitVariant;
            faceVariant = newFaceVariant;
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