using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using DFMP.Hooks;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPQuestEnemyClientController : MonoBehaviour
    {
        sealed class PendingRegistration
        {
            public Foe Foe;
        }

        static DFMPQuestEnemyClientController instance;
        readonly Dictionary<ulong, PendingRegistration> pendingByRequestId =
            new Dictionary<ulong, PendingRegistration>();
        readonly Dictionary<string, Foe> foesByObjectiveId =
            new Dictionary<string, Foe>(System.StringComparer.Ordinal);
        ulong nextRequestId = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            EnsureInstance();
        }

        public static void EnsureInstance()
        {
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer || instance != null)
                return;

            GameObject go = new GameObject("DFMP_QuestEnemyClientController");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DFMPQuestEnemyClientController>();
        }

        public static void RegisterClientHandlers()
        {
            EnsureInstance();
            if (instance == null)
                return;

            NetworkClient.RegisterHandler<DFMPQuestObjectiveResponseMessage>(instance.OnObjectiveResponse);
            NetworkClient.RegisterHandler<DFMPQuestObjectiveResultMessage>(instance.OnObjectiveResult);
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
            DaggerfallHooks.TryHandlePlacedQuestFoe = TryHandlePlacedQuestFoe;
            DaggerfallHooks.TryHandleDynamicQuestFoePlacement = TryHandleDynamicQuestFoePlacement;
            DaggerfallHooks.TryHandleQuestFoeCommand = TryHandleQuestFoeCommand;
            QuestMachine.OnQuestEnded += OnQuestEnded;
        }

        void OnDestroy()
        {
            if (DaggerfallHooks.TryHandlePlacedQuestFoe == TryHandlePlacedQuestFoe)
                DaggerfallHooks.TryHandlePlacedQuestFoe = null;
            if (DaggerfallHooks.TryHandleDynamicQuestFoePlacement == TryHandleDynamicQuestFoePlacement)
                DaggerfallHooks.TryHandleDynamicQuestFoePlacement = null;
            if (DaggerfallHooks.TryHandleQuestFoeCommand == TryHandleQuestFoeCommand)
                DaggerfallHooks.TryHandleQuestFoeCommand = null;
            QuestMachine.OnQuestEnded -= OnQuestEnded;
            if (instance == this)
                instance = null;
        }

        bool TryHandlePlacedQuestFoe(object siteTypeObject, object questObject, object markerObject, object foeObject)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return false;

            Quest quest = questObject as Quest;
            Foe foe = foeObject as Foe;
            if (quest == null || foe == null || !(markerObject is QuestMarker))
                return false;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            DFMPWorldContextReport contextReport;
            DFMPWorldContextKey context;
            if (streamingWorld == null ||
                !DFMPPositionReporter.TryBuildWorldContextReport(streamingWorld, out contextReport) ||
                !DFMPWorldContextProtocol.TryCreateKey(contextReport, out context))
                return false;

            QuestMarker marker = (QuestMarker)markerObject;
            Vector3 objectivePosition = new Vector3(
                marker.dungeonX * RDBLayout.RDBSide + marker.flatPosition.x,
                marker.flatPosition.y,
                marker.dungeonZ * RDBLayout.RDBSide + marker.flatPosition.z);

            ulong requestId = nextRequestId++;
            pendingByRequestId[requestId] = new PendingRegistration { Foe = foe };
            NetworkClient.Send(new DFMPQuestObjectiveRegistrationMessage
            {
                RequestId = requestId,
                QuestUid = quest.UID,
                FoeSymbol = foe.Symbol != null ? foe.Symbol.Original : string.Empty,
                ActionGeneration = 0,
                ObjectiveKind = (int)DFMPQuestObjectiveKind.MarkerBound,
                Context = context,
                ObjectivePosition = objectivePosition,
                FacingYaw = 0f,
                MobileType = (int)foe.FoeType,
                Gender = (int)foe.Gender,
                Reaction = (int)DFBlock.EnemyReactionTypes.Hostile,
                SpawnCount = foe.SpawnCount,
                QuestLootJson = BuildQuestLootJson(foe)
            });
            Debug.Log(
                $"[DFMP Quest] Sent marker-bound foe registration: requestId={requestId}, questUid={quest.UID}, " +
                $"foe='{foe.Symbol?.Original ?? string.Empty}', context={context}, objectivePosition={objectivePosition}.");
            return true;
        }

        /// <summary>
        /// Native <c>PlaceFoeFreely</c> parks the foe a fixed 1.25m above the floor it found and only settles it onto
        /// that floor later in <c>FinalizeFoe</c>, which this hook pre-empts. DFMP descriptors are feet-grounded, so
        /// register the native ground contact rather than the raised placement point; otherwise small mobiles visibly
        /// float wherever the server has no hosted geometry to re-ground them against.
        /// </summary>
        bool TryHandleDynamicQuestFoePlacement(
            object actionObject,
            object foeObject,
            Vector3 scenePosition,
            Vector3 sceneGroundPosition,
            int spawnGeneration,
            int spawnIndex)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return false;

            Foe foe = foeObject as Foe;
            if (foe == null || foe.ParentQuest == null)
                return false;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            DFMPWorldContextReport contextReport;
            DFMPWorldContextKey context;
            if (streamingWorld == null ||
                !DFMPPositionReporter.TryBuildWorldContextReport(streamingWorld, out contextReport) ||
                !DFMPWorldContextProtocol.TryCreateKey(contextReport, out context))
                return false;

            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
            Vector3 objectivePosition;
            if (context.Kind == DFMPWorldContextKind.Dungeon &&
                playerEnterExit != null &&
                playerEnterExit.Dungeon != null)
            {
                objectivePosition = sceneGroundPosition - playerEnterExit.Dungeon.transform.position;
            }
            else if (context.Kind == DFMPWorldContextKind.BuildingInterior &&
                     playerEnterExit != null &&
                     playerEnterExit.Interior != null)
            {
                objectivePosition = sceneGroundPosition - playerEnterExit.Interior.transform.position;
            }
            else
            {
                Vector3 localScenePosition = streamingWorld.LocalPlayerGPS.transform.position;
                objectivePosition = new Vector3(
                    streamingWorld.LocalPlayerGPS.WorldX +
                        (sceneGroundPosition.x - localScenePosition.x) * StreamingWorld.SceneMapRatio,
                    sceneGroundPosition.y,
                    streamingWorld.LocalPlayerGPS.WorldZ +
                        (sceneGroundPosition.z - localScenePosition.z) * StreamingWorld.SceneMapRatio);
            }

            Vector3 lookDirection = GameManager.Instance.PlayerObject.transform.position - scenePosition;
            lookDirection.y = 0f;
            float facingYaw = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDirection).eulerAngles.y
                : 0f;
            int actionGeneration = unchecked((spawnGeneration * 16 + spawnIndex + 1) & int.MaxValue);
            ulong requestId = nextRequestId++;
            pendingByRequestId[requestId] = new PendingRegistration { Foe = foe };
            NetworkClient.Send(new DFMPQuestObjectiveRegistrationMessage
            {
                RequestId = requestId,
                QuestUid = foe.ParentQuest.UID,
                FoeSymbol = foe.Symbol != null ? foe.Symbol.Original : string.Empty,
                ActionGeneration = actionGeneration,
                ObjectiveKind = (int)DFMPQuestObjectiveKind.Dynamic,
                Context = context,
                ObjectivePosition = objectivePosition,
                FacingYaw = facingYaw,
                MobileType = (int)foe.FoeType,
                Gender = (int)foe.Gender,
                Reaction = (int)DFBlock.EnemyReactionTypes.Hostile,
                SpawnCount = 1,
                QuestLootJson = BuildQuestLootJson(foe)
            });
            Debug.Log(
                $"[DFMP Quest] Sent dynamic foe registration: requestId={requestId}, questUid={foe.ParentQuest.UID}, " +
                $"foe='{foe.Symbol?.Original ?? string.Empty}', generation={actionGeneration}, context={context}, " +
                $"objectivePosition={objectivePosition}.");
            return true;
        }

        static string BuildQuestLootJson(Foe foe)
        {
            DaggerfallUnityItem[] queuedItems = foe != null ? foe.GetClonedItemQueue() : null;
            if (queuedItems == null || queuedItems.Length == 0)
                return string.Empty;

            var collection = new ItemCollection();
            for (int index = 0; index < queuedItems.Length; index++)
            {
                if (queuedItems[index] != null && queuedItems[index].IsQuestItem)
                    collection.AddItem(queuedItems[index]);
            }
            if (collection.Count == 0)
                return string.Empty;

            return DFMPInventorySnapshotCodec.Encode(
                DFMPCharacterPersistence.CaptureItems(collection.SerializeItems()),
                new DFMPCharacterEquipmentRecord[0]);
        }

        void OnObjectiveResponse(DFMPQuestObjectiveResponseMessage response)
        {
            PendingRegistration pending;
            if (!pendingByRequestId.TryGetValue(response.RequestId, out pending))
                return;

            pendingByRequestId.Remove(response.RequestId);
            if (!response.Accepted || pending.Foe == null || string.IsNullOrWhiteSpace(response.ObjectiveId))
            {
                Debug.LogWarning($"[DFMP Quest] Quest foe registration rejected: requestId={response.RequestId}, reason={response.Reason ?? string.Empty}.");
                return;
            }

            foesByObjectiveId[response.ObjectiveId] = pending.Foe;
            Debug.Log(
                $"[DFMP Quest] Quest foe registration accepted: requestId={response.RequestId}, " +
                $"objectiveId={response.ObjectiveId}.");
        }

        void OnObjectiveResult(DFMPQuestObjectiveResultMessage result)
        {
            Foe foe;
            if (!foesByObjectiveId.TryGetValue(result.ObjectiveId ?? string.Empty, out foe) || foe == null)
            {
                Debug.LogWarning($"[DFMP Quest] Deferred quest foe result without local objective: objectiveId={result.ObjectiveId ?? string.Empty}, resultId={result.ResultId}.");
                return;
            }

            DFMPQuestObjectiveCreditKind creditKind = (DFMPQuestObjectiveCreditKind)result.CreditKind;
            if (creditKind == DFMPQuestObjectiveCreditKind.Injured)
            {
                foe.SetInjured();
                return;
            }

            if (creditKind == DFMPQuestObjectiveCreditKind.Clicked)
            {
                foe.SetPlayerClicked();
                return;
            }

            foe.IncrementKills(Mathf.Max(1, result.KillCount));
            DFMPPositionReporter.RequestQuestSave();
            NetworkClient.Send(new DFMPQuestObjectiveResultAckMessage
            {
                ResultId = result.ResultId,
                ObjectiveId = result.ObjectiveId
            });
            Debug.Log($"[DFMP Quest] Applied quest foe result: objectiveId={result.ObjectiveId}, resultId={result.ResultId}, killCount={result.KillCount}.");
        }

        bool TryHandleQuestFoeCommand(
            object actionObject,
            object foeObject,
            int commandKind,
            int intPayload,
            string stringPayload)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return false;

            Foe foe = foeObject as Foe;
            if (foe == null || foe.ParentQuest == null)
                return false;

            if (commandKind == (int)DFMPQuestFoeCommandKind.GiveItem)
                stringPayload = BuildQuestLootJson(foe);

            NetworkClient.Send(new DFMPQuestFoeCommandMessage
            {
                RequestId = nextRequestId++,
                QuestUid = foe.ParentQuest.UID,
                FoeSymbol = foe.Symbol != null ? foe.Symbol.Original : string.Empty,
                CommandKind = commandKind,
                IntPayload = intPayload,
                StringPayload = stringPayload ?? string.Empty
            });
            return true;
        }

        void OnQuestEnded(Quest quest)
        {
            if (quest == null || !NetworkClient.isConnected)
                return;

            var cancelled = new System.Collections.Generic.List<string>();
            foreach (var pair in foesByObjectiveId)
            {
                if (pair.Value != null && pair.Value.ParentQuest == quest)
                    cancelled.Add(pair.Key);
            }

            for (int index = 0; index < cancelled.Count; index++)
            {
                NetworkClient.Send(new DFMPQuestObjectiveCancellationMessage
                {
                    RequestId = nextRequestId++,
                    ObjectiveId = cancelled[index]
                });
                foesByObjectiveId.Remove(cancelled[index]);
            }
        }

        public static void NotifyOwnerClickedObjective(string objectiveId)
        {
            if (instance == null || string.IsNullOrWhiteSpace(objectiveId) || !NetworkClient.isConnected)
                return;

            Foe foe;
            if (!instance.foesByObjectiveId.TryGetValue(objectiveId, out foe) || foe == null || foe.ParentQuest == null)
                return;

            instance.TryHandleQuestFoeCommand(null, foe, (int)DFMPQuestFoeCommandKind.Click, 0, null);
        }
    }
}
