using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game.Questing;
using DFMP.Hooks;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPQuestActionClientController : MonoBehaviour
    {
        sealed class PendingTeleport
        {
            public ulong RequestId;
            public bool Completed;
            public bool Failed;
        }

        static DFMPQuestActionClientController instance;
        PendingTeleport pendingTeleport;
        ulong nextRequestId = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer || instance != null)
                return;

            GameObject go = new GameObject("DFMP_QuestActionClientController");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DFMPQuestActionClientController>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            DaggerfallHooks.TryHandleQuestTeleport = TryHandleQuestTeleport;
            DaggerfallHooks.TryHandleUnsupportedQuestSceneAction = TryHandleUnsupportedQuestSceneAction;
            NetworkClient.RegisterHandler<DFMPQuestTeleportResponse>(OnTeleportResponse);
        }

        void OnDestroy()
        {
            if (DaggerfallHooks.TryHandleQuestTeleport == TryHandleQuestTeleport)
                DaggerfallHooks.TryHandleQuestTeleport = null;
            if (DaggerfallHooks.TryHandleUnsupportedQuestSceneAction == TryHandleUnsupportedQuestSceneAction)
                DaggerfallHooks.TryHandleUnsupportedQuestSceneAction = null;
            if (instance == this)
                instance = null;
        }

        int TryHandleQuestTeleport(
            object actionObject,
            object placeObject,
            object locationObject,
            Vector3 markerLocalPosition,
            int markerIndex)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return 0;

            if (pendingTeleport != null)
            {
                if (pendingTeleport.Failed || pendingTeleport.Completed)
                {
                    bool completed = pendingTeleport.Completed;
                    pendingTeleport = null;
                    return completed ? 2 : 2;
                }

                return 1;
            }

            Place place = placeObject as Place;
            if (place == null || !(locationObject is DFLocation))
                return 0;

            DFLocation location = (DFLocation)locationObject;
            TeleportPc action = actionObject as TeleportPc;
            ulong requestId = nextRequestId++;
            pendingTeleport = new PendingTeleport { RequestId = requestId };
            NetworkClient.Send(new DFMPQuestTeleportRequest
            {
                RequestId = requestId,
                QuestUid = action != null && action.ParentQuest != null ? action.ParentQuest.UID : 0,
                PlaceSymbol = place.Symbol != null ? place.Symbol.Original : string.Empty,
                RegionName = location.RegionName,
                LocationName = location.Name,
                MarkerIndex = markerIndex,
                MarkerLocalPosition = markerLocalPosition
            });
            return 1;
        }

        static bool TryHandleUnsupportedQuestSceneAction(string actionName)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return false;

            Debug.LogWarning(
                "[DFMP Quest] Consumed unsupported scene-wide quest action '" +
                (actionName ?? string.Empty) +
                "' so it cannot mutate server-owned enemies or spawn local guards.");
            return true;
        }

        void OnTeleportResponse(DFMPQuestTeleportResponse response)
        {
            if (pendingTeleport == null || pendingTeleport.RequestId != response.RequestId)
                return;

            pendingTeleport.Failed = !response.Accepted;
            if (!response.Accepted)
            {
                Debug.LogWarning($"[DFMP Quest] Quest teleport rejected: reason={response.Reason ?? string.Empty}.");
                pendingTeleport.Completed = true;
            }
        }

        public static void NotifyQuestTeleportAcknowledged()
        {
            if (instance == null || instance.pendingTeleport == null)
                return;
            instance.pendingTeleport.Completed = true;
        }
    }
}
