using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DFMP.Hooks;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public struct DFMPSpawnAssignment : NetworkMessage
    {
        public int ConnectionId;
        public int MapPixelX;
        public int MapPixelY;
        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public string StartMarkerName;
    }

    public struct DFMPSpawnAcknowledgement : NetworkMessage
    {
        public int WorldX;
        public float WorldY;
        public int WorldZ;
    }

    public class DFMPSpawnAssignmentController : MonoBehaviour
    {
        const float FixedSpawnLocationWaitSeconds = 10f;
        const float GroundProbeHeight = 10f;
        const float GroundProbeDepth = 60f;
        const float PostAssignmentReportSuppressionSeconds = 1f;

        static DFMPSpawnAssignmentController instance;
        public static int LocalConnectionId { get; private set; } = -1;
        public static bool HasPendingServerAssignment
        {
            get { return instance != null && (instance.assignmentState.HasPendingAssignment || instance.transitionState.HasPendingAssignment || Time.unscaledTime < instance.reportSuppressionDeadline); }
        }

        public static bool HasImmediateWorldContextReport
        {
            get { return instance != null && instance.hasImmediateWorldContextReport; }
        }

        readonly DFMPSpawnAssignmentState assignmentState = new DFMPSpawnAssignmentState();
        readonly DFMPTransitionAssignmentState transitionState = new DFMPTransitionAssignmentState();
        float fixedSpawnWaitDeadline;
        float reportSuppressionDeadline;
        bool hasImmediateWorldContextReport;
        bool sharedTestSpawnApplied;
        bool transitionApplied;
        bool doorHookBypass;
        bool reconnectInteriorOpened;
        bool reconnectHudCovered;
        bool hasPendingDoorTransition;
        bool pendingDoorEnterInterior;
        bool pendingDoorDoFade;
        PlayerEnterExit pendingDoorPlayerEnterExit;
        Transform pendingDoorOwner;
        StaticDoor pendingDoor;
        bool hasPendingDungeonTransition;
        bool pendingDungeonEnter;
        bool pendingDungeonDoFade;
        PlayerEnterExit pendingDungeonPlayerEnterExit;
        Transform pendingDungeonDoorOwner;
        StaticDoor pendingDungeonDoor;
        DFLocation pendingDungeonLocation;

        public static void RegisterClientHandler()
        {
            NetworkClient.RegisterHandler<DFMPSpawnAssignment>(OnSpawnAssignment);
            NetworkClient.RegisterHandler<DFMPTransitionAssignment>(OnTransitionAssignment);
            DaggerfallHooks.TryHandleBuildingInteriorTransition = TryHandleBuildingInteriorTransition;
            DaggerfallHooks.TryHandleBuildingExteriorTransition = TryHandleBuildingExteriorTransition;
            DaggerfallHooks.TryHandleDungeonInteriorTransition = TryHandleDungeonInteriorTransition;
            DaggerfallHooks.TryHandleDungeonExteriorTransition = TryHandleDungeonExteriorTransition;
            DaggerfallHooks.TryHandleVampirismTransformation = TryHandleVampirismTransformation;
            DaggerfallHooks.TryHandlePlayerDeath = TryHandlePlayerDeath;
            EnsureInstance();
        }

        static bool TryHandleVampirismTransformation()
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return false;
            if (instance != null && instance.transitionState.HasPendingAssignment)
                return true;

            DFMPNetworkClient.RequestVampirismTransformation();
            Debug.Log("[DFMP Transition] Blocked local vampirism transformation and requested server assignment.");
            return true;
        }

        static bool TryHandlePlayerDeath()
        {
            if (!DFMPDeathRespawnPolicy.ShouldInterceptClientDeath(
                NetworkClient.isConnected,
                NetworkClient.ready,
                instance != null && instance.assignmentState.HasPendingAssignment,
                instance != null && instance.transitionState.HasPendingAssignment))
                return false;

            DFMPNetworkClient.ReportPlayerDeath();
            Debug.Log("[DFMP Respawn] Blocked local death-to-title flow and requested server respawn assignment.");
            return true;
        }

        static bool TryHandleBuildingInteriorTransition(object playerEnterExitObject, object doorOwnerObject, object doorObject, bool doFade, bool start)
        {
            return EnsureInstance().TryHandleBuildingInteriorTransitionInstance(playerEnterExitObject, doorOwnerObject, doorObject, doFade, start);
        }

        static bool TryHandleBuildingExteriorTransition(object playerEnterExitObject, bool doFade)
        {
            return EnsureInstance().TryHandleBuildingExteriorTransitionInstance(playerEnterExitObject, doFade);
        }

        static bool TryHandleDungeonInteriorTransition(object playerEnterExitObject, object doorOwnerObject, object doorObject, object locationObject, bool doFade)
        {
            return EnsureInstance().TryHandleDungeonInteriorTransitionInstance(playerEnterExitObject, doorOwnerObject, doorObject, locationObject, doFade);
        }

        static bool TryHandleDungeonExteriorTransition(object playerEnterExitObject, bool doFade)
        {
            return EnsureInstance().TryHandleDungeonExteriorTransitionInstance(playerEnterExitObject, doFade);
        }

        static void OnSpawnAssignment(DFMPSpawnAssignment assignment)
        {
            EnsureInstance().SetPendingAssignment(assignment);
        }

        static void OnTransitionAssignment(DFMPTransitionAssignment assignment)
        {
            EnsureInstance().SetPendingTransitionAssignment(assignment);
        }

        static DFMPSpawnAssignmentController EnsureInstance()
        {
            if (instance != null)
                return instance;

            GameObject controllerGo = new GameObject("DFMP_SpawnAssignmentController");
            Object.DontDestroyOnLoad(controllerGo);
            instance = controllerGo.AddComponent<DFMPSpawnAssignmentController>();
            StartGameBehaviour.OnStartGame += instance.OnStartGame;
            SaveLoadManager.OnLoad += instance.OnLoad;
            return instance;
        }

        public static void Reset()
        {
            if (instance != null)
            {
                if (instance.reconnectHudCovered &&
                    DaggerfallUI.Instance != null &&
                    DaggerfallUI.Instance.FadeBehaviour != null)
                {
                    DaggerfallUI.Instance.FadeBehaviour.FadeHUDFromBlack(0.1f);
                }

                Destroy(instance.gameObject);
            }

            instance = null;
            LocalConnectionId = -1;
        }

        public static void CompleteImmediateWorldContextReport()
        {
            if (instance != null)
                instance.hasImmediateWorldContextReport = false;
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;

            StartGameBehaviour.OnStartGame -= OnStartGame;
            SaveLoadManager.OnLoad -= OnLoad;
        }

        void SetPendingAssignment(DFMPSpawnAssignment assignment)
        {
            assignmentState.Receive(assignment);
            LocalConnectionId = assignment.ConnectionId;
            sharedTestSpawnApplied = false;
            SuppressPositionReportsBriefly();
            Debug.Log($"[DFMP Session] Client received spawn assignment: mapPixel={assignment.MapPixelX}/{assignment.MapPixelY}.");
        }

        void SetPendingTransitionAssignment(DFMPTransitionAssignment assignment)
        {
            transitionState.Receive(assignment);
            if (assignment.ConnectionId >= 0)
                LocalConnectionId = assignment.ConnectionId;
            transitionApplied = false;
            reconnectInteriorOpened = false;
            reconnectHudCovered = false;
            SuppressPositionReportsBriefly();
            Debug.Log($"[DFMP Transition] Client received transition assignment: assignmentId={assignment.AssignmentId}, kind={assignment.Kind}, mapPixel={assignment.MapPixelX}/{assignment.MapPixelY}.");
        }

        bool TryHandleBuildingInteriorTransitionInstance(object playerEnterExitObject, object doorOwnerObject, object doorObject, bool doFade, bool start)
        {
            if (doorHookBypass || start || !NetworkClient.isConnected || !NetworkClient.ready)
                return false;
            if (hasPendingDoorTransition || hasPendingDungeonTransition || transitionState.HasPendingAssignment)
                return true;

            var playerEnterExit = playerEnterExitObject as PlayerEnterExit;
            var doorOwner = doorOwnerObject as Transform;
            if (playerEnterExit == null || !(doorObject is StaticDoor))
                return false;

            StaticDoor door = (StaticDoor)doorObject;
            DFMPDoorTransitionRequest request;
            if (!TryBuildDoorTransitionRequest(playerEnterExit, door.buildingKey, true, door, true, out request))
                return false;

            pendingDoorPlayerEnterExit = playerEnterExit;
            pendingDoorOwner = doorOwner;
            pendingDoor = door;
            pendingDoorEnterInterior = true;
            pendingDoorDoFade = doFade;
            hasPendingDoorTransition = true;
            SuppressPositionReportsBriefly();
            NetworkClient.Send(request);
            Debug.Log($"[DFMP Transition] Client requested server building entry: mapPixel={request.MapPixelX}/{request.MapPixelY}, buildingKey={request.BuildingKey}.");
            return true;
        }

        bool TryHandleBuildingExteriorTransitionInstance(object playerEnterExitObject, bool doFade)
        {
            if (doorHookBypass || !NetworkClient.isConnected || !NetworkClient.ready)
                return false;
            if (hasPendingDoorTransition || hasPendingDungeonTransition || transitionState.HasPendingAssignment)
                return true;

            var playerEnterExit = playerEnterExitObject as PlayerEnterExit;
            if (playerEnterExit == null || !playerEnterExit.IsPlayerInsideBuilding)
                return false;

            DFMPDoorTransitionRequest request;
            if (!TryBuildDoorTransitionRequest(playerEnterExit, playerEnterExit.BuildingDiscoveryData.buildingKey, false, default(StaticDoor), false, out request))
                return false;

            pendingDoorPlayerEnterExit = playerEnterExit;
            pendingDoorOwner = null;
            pendingDoor = default(StaticDoor);
            pendingDoorEnterInterior = false;
            pendingDoorDoFade = doFade;
            hasPendingDoorTransition = true;
            SuppressPositionReportsBriefly();
            NetworkClient.Send(request);
            Debug.Log($"[DFMP Transition] Client requested server building exit: mapPixel={request.MapPixelX}/{request.MapPixelY}, buildingKey={request.BuildingKey}.");
            return true;
        }

        bool TryHandleDungeonInteriorTransitionInstance(object playerEnterExitObject, object doorOwnerObject, object doorObject, object locationObject, bool doFade)
        {
            if (doorHookBypass || !NetworkClient.isConnected || !NetworkClient.ready)
                return false;
            if (hasPendingDoorTransition || hasPendingDungeonTransition || transitionState.HasPendingAssignment)
                return true;

            var playerEnterExit = playerEnterExitObject as PlayerEnterExit;
            var doorOwner = doorOwnerObject as Transform;
            if (playerEnterExit == null || !(doorObject is StaticDoor) || !(locationObject is DFLocation))
                return false;

            StaticDoor door = (StaticDoor)doorObject;
            DFLocation location = (DFLocation)locationObject;
            DFMPDungeonTransitionRequest request;
            if (!TryBuildDungeonTransitionRequest(location, true, out request))
                return false;

            pendingDungeonPlayerEnterExit = playerEnterExit;
            pendingDungeonDoorOwner = doorOwner;
            pendingDungeonDoor = door;
            pendingDungeonLocation = location;
            pendingDungeonEnter = true;
            pendingDungeonDoFade = doFade;
            hasPendingDungeonTransition = true;
            SuppressPositionReportsBriefly();
            NetworkClient.Send(request);
            Debug.Log($"[DFMP Transition] Client requested server dungeon entry: mapPixel={request.MapPixelX}/{request.MapPixelY}, location='{request.LocationId}'.");
            return true;
        }

        bool TryHandleDungeonExteriorTransitionInstance(object playerEnterExitObject, bool doFade)
        {
            if (doorHookBypass || !NetworkClient.isConnected || !NetworkClient.ready)
                return false;
            if (hasPendingDoorTransition || hasPendingDungeonTransition || transitionState.HasPendingAssignment)
                return true;

            var playerEnterExit = playerEnterExitObject as PlayerEnterExit;
            if (playerEnterExit == null || !playerEnterExit.IsPlayerInsideDungeon || playerEnterExit.Dungeon == null)
                return false;

            DFLocation location = playerEnterExit.Dungeon.Summary.LocationData;
            DFMPDungeonTransitionRequest request;
            if (!TryBuildDungeonTransitionRequest(location, false, out request))
                return false;

            pendingDungeonPlayerEnterExit = playerEnterExit;
            pendingDungeonDoorOwner = null;
            pendingDungeonDoor = default(StaticDoor);
            pendingDungeonLocation = location;
            pendingDungeonEnter = false;
            pendingDungeonDoFade = doFade;
            hasPendingDungeonTransition = true;
            SuppressPositionReportsBriefly();
            NetworkClient.Send(request);
            Debug.Log($"[DFMP Transition] Client requested server dungeon exit: mapPixel={request.MapPixelX}/{request.MapPixelY}, location='{request.LocationId}'.");
            return true;
        }

        bool TryBuildDoorTransitionRequest(PlayerEnterExit playerEnterExit, int buildingKey, bool enterInterior, StaticDoor exteriorDoor, bool hasExteriorDoor, out DFMPDoorTransitionRequest request)
        {
            request = new DFMPDoorTransitionRequest();
            if (playerEnterExit == null || buildingKey <= 0 || GameManager.Instance == null || GameManager.Instance.PlayerGPS == null)
                return false;

            PlayerGPS playerGPS = GameManager.Instance.PlayerGPS;
            var mapPixel = playerGPS.CurrentMapPixel;
            if (!DFMPSpawnProtocol.IsValidMapPixel(mapPixel.X, mapPixel.Y) || !playerGPS.HasCurrentLocation)
                return false;

            request = new DFMPDoorTransitionRequest
            {
                EnterInterior = enterInterior,
                MapPixelX = mapPixel.X,
                MapPixelY = mapPixel.Y,
                RegionIndex = playerGPS.CurrentRegionIndex,
                LocationIndex = playerGPS.CurrentLocationIndex,
                LocationId = playerGPS.CurrentLocation.Name,
                BuildingKey = buildingKey,
                BuildingType = enterInterior ? (int)playerEnterExit.BuildingType : -1,
                HasExteriorDoor = enterInterior && hasExteriorDoor,
                ExteriorDoor = enterInterior && hasExteriorDoor
                    ? DFMPNetworkStaticDoor.FromStaticDoor(exteriorDoor)
                    : default(DFMPNetworkStaticDoor)
            };
            return true;
        }

        bool TryBuildDungeonTransitionRequest(DFLocation location, bool enterDungeon, out DFMPDungeonTransitionRequest request)
        {
            request = new DFMPDungeonTransitionRequest();
            if (!location.Loaded || !location.HasDungeon)
                return false;

            DFPosition mapPixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            if (!DFMPSpawnProtocol.IsValidMapPixel(mapPixel.X, mapPixel.Y))
                return false;

            request = new DFMPDungeonTransitionRequest
            {
                EnterDungeon = enterDungeon,
                MapPixelX = mapPixel.X,
                MapPixelY = mapPixel.Y,
                RegionIndex = location.RegionIndex,
                LocationIndex = location.LocationIndex,
                LocationId = location.Name
            };
            return true;
        }

        void SuppressPositionReportsBriefly()
        {
            reportSuppressionDeadline = Time.unscaledTime + PostAssignmentReportSuppressionSeconds;
        }

        void OnStartGame(object sender, System.EventArgs e)
        {
            RequeueAssignment("game start");
        }

        void OnLoad(SaveData_v1 saveData)
        {
            RequeueAssignment("save load");
        }

        void RequeueAssignment(string reason)
        {
            if (!assignmentState.HasAssignment && !transitionState.HasAssignment)
                return;

            if (assignmentState.HasAssignment)
            {
                assignmentState.Requeue();
                sharedTestSpawnApplied = false;
                Debug.Log($"[DFMP Session] Client reapplied pending spawn assignment after {reason}: mapPixel={assignmentState.Assignment.MapPixelX}/{assignmentState.Assignment.MapPixelY}.");
            }

            if (transitionState.HasAssignment)
            {
                transitionState.Requeue();
                transitionApplied = false;
                reconnectInteriorOpened = false;
                // Keep HUD covered across requeue so a mid-reconnect reload cannot flash exterior.
                Debug.Log($"[DFMP Transition] Client reapplied pending transition assignment after {reason}: assignmentId={transitionState.Assignment.AssignmentId}, kind={transitionState.Assignment.Kind}, mapPixel={transitionState.Assignment.MapPixelX}/{transitionState.Assignment.MapPixelY}.");
            }
        }

        void Update()
        {
            if ((!assignmentState.HasPendingAssignment && !transitionState.HasPendingAssignment) || !NetworkClient.isConnected)
                return;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null)
                return;

            // Start In Dungeon suppresses StreamingWorld.IsReady, which would otherwise leave
            // the pending server spawn stuck while the client remains in Privateer's Hold.
            if (!streamingWorld.IsReady && ShouldPrepareExteriorWorldForAssignment())
                TryPrepareExteriorWorldForAssignment(streamingWorld);

            if (!streamingWorld.IsReady)
                return;

            if (assignmentState.HasPendingAssignment)
            {
                UpdateSpawnAssignment(streamingWorld);
                return;
            }

            UpdateTransitionAssignment(streamingWorld);
        }

        void UpdateSpawnAssignment(StreamingWorld streamingWorld)
        {
            if (assignmentState.TryRequestTeleport())
            {
                fixedSpawnWaitDeadline = Time.realtimeSinceStartup + FixedSpawnLocationWaitSeconds;
                sharedTestSpawnApplied = false;
                TryPrepareExteriorWorldForAssignment(streamingWorld);
                if (string.IsNullOrWhiteSpace(assignmentState.Assignment.StartMarkerName) &&
                    (assignmentState.Assignment.WorldX != 0 || assignmentState.Assignment.WorldZ != 0))
                {
                    streamingWorld.TeleportToWorldCoordinates(
                        assignmentState.Assignment.WorldX,
                        assignmentState.Assignment.WorldZ);
                    Debug.Log($"[DFMP Session] Client requested persisted world spawn: world={assignmentState.Assignment.WorldX}/{assignmentState.Assignment.WorldY:F2}/{assignmentState.Assignment.WorldZ}.");
                }
                else
                {
                    streamingWorld.TeleportToCoordinates(assignmentState.Assignment.MapPixelX, assignmentState.Assignment.MapPixelY, StreamingWorld.RepositionMethods.RandomStartMarker);
                    Debug.Log($"[DFMP Session] Client requested grounded city spawn: mapPixel={assignmentState.Assignment.MapPixelX}/{assignmentState.Assignment.MapPixelY}.");
                }
                return;
            }

            if (!sharedTestSpawnApplied)
            {
                if (streamingWorld.IsRepositioningPlayer ||
                    streamingWorld.LocalPlayerGPS.CurrentMapPixel.X != assignmentState.Assignment.MapPixelX ||
                    streamingWorld.LocalPlayerGPS.CurrentMapPixel.Y != assignmentState.Assignment.MapPixelY)
                    return;

                if (assignmentState.Assignment.WorldX == 0 && assignmentState.Assignment.WorldZ == 0)
                {
                    if (!TryApplyAssignedStartMarker(streamingWorld, assignmentState.Assignment.MapPixelX, assignmentState.Assignment.MapPixelY, assignmentState.Assignment.StartMarkerName) && Time.realtimeSinceStartup < fixedSpawnWaitDeadline)
                        return;
                }
                else if (!string.IsNullOrWhiteSpace(assignmentState.Assignment.StartMarkerName))
                {
                    if (!TryApplyAssignedStartMarker(streamingWorld, assignmentState.Assignment.MapPixelX, assignmentState.Assignment.MapPixelY, assignmentState.Assignment.StartMarkerName) && Time.realtimeSinceStartup < fixedSpawnWaitDeadline)
                        return;
                }
                else
                {
                    Debug.Log($"[DFMP Session] Preserved persisted world spawn: world={assignmentState.Assignment.WorldX}/{assignmentState.Assignment.WorldY:F2}/{assignmentState.Assignment.WorldZ}.");
                }

                // Acknowledge on a later frame so StreamingWorld resyncs GPS to the new scene position first.
                sharedTestSpawnApplied = true;
                return;
            }

            if (!assignmentState.TryAcknowledge(streamingWorld.IsRepositioningPlayer, streamingWorld.LocalPlayerGPS.CurrentMapPixel.X, streamingWorld.LocalPlayerGPS.CurrentMapPixel.Y))
                return;

            NetworkClient.Send(new DFMPSpawnAcknowledgement
            {
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ
            });
            SuppressPositionReportsBriefly();

            Debug.Log($"[DFMP Session] Client acknowledged grounded spawn: world={streamingWorld.LocalPlayerGPS.WorldX}/0/{streamingWorld.LocalPlayerGPS.WorldZ}.");
        }

        bool ShouldPrepareExteriorWorldForAssignment()
        {
            return assignmentState.HasPendingAssignment ||
                (transitionState.HasPendingAssignment && transitionState.Assignment.ContextKind == DFMPWorldContextKind.Exterior);
        }

        static void TryPrepareExteriorWorldForAssignment(StreamingWorld streamingWorld)
        {
            if (GameManager.HasInstance)
            {
                PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
                if (playerEnterExit != null && playerEnterExit.IsPlayerInside)
                {
                    playerEnterExit.EnableExteriorParent(cleanup: true);
                    Debug.Log("[DFMP Session] Released dungeon/interior parent before applying exterior spawn.");
                    return;
                }
            }

            if (streamingWorld != null && streamingWorld.suppressWorld)
            {
                streamingWorld.suppressWorld = false;
                Debug.Log("[DFMP Session] Unsuppressed streaming world before applying exterior spawn.");
            }
        }

        void UpdateTransitionAssignment(StreamingWorld streamingWorld)
        {
            if (transitionState.Assignment.Kind == DFMPTransitionKind.Door && hasPendingDoorTransition)
            {
                UpdateDoorTransitionAssignment(streamingWorld);
                return;
            }

            if ((transitionState.Assignment.Kind == DFMPTransitionKind.DungeonEntry || transitionState.Assignment.Kind == DFMPTransitionKind.DungeonExit) && hasPendingDungeonTransition)
            {
                UpdateDungeonTransitionAssignment(streamingWorld);
                return;
            }

            if (transitionState.Assignment.Kind == DFMPTransitionKind.Reconnect &&
                (transitionState.Assignment.ContextKind == DFMPWorldContextKind.BuildingInterior ||
                 transitionState.Assignment.ContextKind == DFMPWorldContextKind.Dungeon))
            {
                UpdateInteriorReconnectTransitionAssignment(streamingWorld);
                return;
            }

            if (transitionState.TryRequestTeleport())
            {
                fixedSpawnWaitDeadline = Time.realtimeSinceStartup + FixedSpawnLocationWaitSeconds;
                transitionApplied = false;

                // If teleporting to an exterior context (such as death respawn or fast travel), ensure any active dungeon/interior parent is cleanly torn down
                if (transitionState.Assignment.ContextKind == DFMPWorldContextKind.Exterior && GameManager.HasInstance)
                {
                    PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
                    if (playerEnterExit != null && playerEnterExit.IsPlayerInside)
                    {
                        playerEnterExit.EnableExteriorParent(cleanup: true);
                        Debug.Log("[DFMP Transition] Cleaned up interior/dungeon parent before assigned exterior teleport.");
                    }
                }

                if (string.IsNullOrWhiteSpace(transitionState.Assignment.StartMarkerName) &&
                    (transitionState.Assignment.WorldX != 0 || transitionState.Assignment.WorldZ != 0))
                {
                    streamingWorld.TeleportToWorldCoordinates(
                        transitionState.Assignment.WorldX,
                        transitionState.Assignment.WorldZ);
                    Debug.Log($"[DFMP Transition] Client requested assigned transition: assignmentId={transitionState.Assignment.AssignmentId}, kind={transitionState.Assignment.Kind}, world={transitionState.Assignment.WorldX}/{transitionState.Assignment.WorldY:F2}/{transitionState.Assignment.WorldZ}.");
                }
                else
                {
                    streamingWorld.TeleportToCoordinates(transitionState.Assignment.MapPixelX, transitionState.Assignment.MapPixelY, StreamingWorld.RepositionMethods.RandomStartMarker);
                    Debug.Log($"[DFMP Transition] Client requested assigned transition marker: assignmentId={transitionState.Assignment.AssignmentId}, kind={transitionState.Assignment.Kind}, mapPixel={transitionState.Assignment.MapPixelX}/{transitionState.Assignment.MapPixelY}.");
                }
                return;
            }

            if (!transitionApplied)
            {
                if (streamingWorld.IsRepositioningPlayer ||
                    streamingWorld.LocalPlayerGPS.CurrentMapPixel.X != transitionState.Assignment.MapPixelX ||
                    streamingWorld.LocalPlayerGPS.CurrentMapPixel.Y != transitionState.Assignment.MapPixelY)
                    return;

                if (!string.IsNullOrWhiteSpace(transitionState.Assignment.StartMarkerName))
                {
                    if (!TryApplyAssignedStartMarker(streamingWorld, transitionState.Assignment.MapPixelX, transitionState.Assignment.MapPixelY, transitionState.Assignment.StartMarkerName) && Time.realtimeSinceStartup < fixedSpawnWaitDeadline)
                        return;
                }

                if (transitionState.Assignment.Kind == DFMPTransitionKind.VampirismTransformation && !ApplyVampirismTransformation())
                    return;
                if (transitionState.Assignment.Kind == DFMPTransitionKind.DeathRespawn && !ApplyDeathRespawn())
                    return;

                transitionApplied = true;
                return;
            }

            var acknowledgement = new DFMPTransitionAcknowledgement
            {
                AssignmentId = transitionState.Assignment.AssignmentId,
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ,
                Context = DFMPSpawnProtocol.GetAssignedContextReport(transitionState.Assignment)
            };

            if (!transitionState.TryAcknowledge(acknowledgement, streamingWorld.IsRepositioningPlayer))
                return;

            NetworkClient.Send(acknowledgement);
            SuppressPositionReportsBriefly();
            Debug.Log($"[DFMP Transition] Client acknowledged assigned transition: assignmentId={acknowledgement.AssignmentId}, world={acknowledgement.WorldX}/0/{acknowledgement.WorldZ}.");
        }

        void UpdateInteriorReconnectTransitionAssignment(StreamingWorld streamingWorld)
        {
            DFMPTransitionAssignment assignment = transitionState.Assignment;

            if (transitionState.TryRequestTeleport())
            {
                fixedSpawnWaitDeadline = Time.realtimeSinceStartup + FixedSpawnLocationWaitSeconds;
                transitionApplied = false;
                reconnectInteriorOpened = false;
                SuppressPositionReportsBriefly();
                CoverReconnectHud();

                if (GameManager.HasInstance)
                {
                    PlayerEnterExit existingEnterExit = GameManager.Instance.PlayerEnterExit;
                    if (existingEnterExit != null && existingEnterExit.IsPlayerInside)
                        existingEnterExit.EnableExteriorParent(cleanup: true);
                }

                // Match native Respawner: map-pixel teleport with no exterior start-marker placement,
                // then reopen the interior under a black HUD so the player never sees the exterior.
                if (streamingWorld.LocalPlayerGPS != null)
                {
                    streamingWorld.LocalPlayerGPS.WorldX = assignment.WorldX;
                    streamingWorld.LocalPlayerGPS.WorldZ = assignment.WorldZ;
                }

                streamingWorld.TeleportToCoordinates(
                    assignment.MapPixelX,
                    assignment.MapPixelY,
                    StreamingWorld.RepositionMethods.None);
                Debug.Log($"[DFMP Transition] Client relocating for interior reconnect: assignmentId={assignment.AssignmentId}, kind={assignment.ContextKind}, mapPixel={assignment.MapPixelX}/{assignment.MapPixelY}.");
                return;
            }

            if (!reconnectInteriorOpened)
            {
                // Do not wait for exterior streaming to finish — native StartDungeonInterior /
                // StartBuildingInterior only need the map pixel and location data. Waiting on
                // IsRepositioningPlayer is what left the exterior visible for half a second.
                if (streamingWorld.LocalPlayerGPS == null ||
                    streamingWorld.LocalPlayerGPS.CurrentMapPixel.X != assignment.MapPixelX ||
                    streamingWorld.LocalPlayerGPS.CurrentMapPixel.Y != assignment.MapPixelY)
                {
                    if (Time.realtimeSinceStartup < fixedSpawnWaitDeadline)
                        return;

                    Debug.LogWarning($"[DFMP Transition] Interior reconnect relocate timed out: assignmentId={assignment.AssignmentId}.");
                    transitionApplied = true;
                    reconnectInteriorOpened = true;
                    RevealReconnectHud();
                    return;
                }

                if (!TryOpenAssignedInterior(assignment))
                {
                    if (Time.realtimeSinceStartup < fixedSpawnWaitDeadline)
                        return;

                    Debug.LogWarning($"[DFMP Transition] Interior reconnect reopen failed: assignmentId={assignment.AssignmentId}, kind={assignment.ContextKind}.");
                    transitionApplied = true;
                    reconnectInteriorOpened = true;
                    RevealReconnectHud();
                    return;
                }

                if (assignment.HasInteriorLocalPosition)
                    ApplyInteriorLocalPose(assignment);

                reconnectInteriorOpened = true;
                transitionApplied = true;
                SuppressPositionReportsBriefly();
                RevealReconnectHud();
                Debug.Log($"[DFMP Transition] Client reopened interior on reconnect: assignmentId={assignment.AssignmentId}, kind={assignment.ContextKind}.");
                return;
            }

            if (!transitionApplied)
                return;

            if (!IsAssignedInteriorReady(assignment) && Time.realtimeSinceStartup < fixedSpawnWaitDeadline)
                return;

            var acknowledgement = new DFMPTransitionAcknowledgement
            {
                AssignmentId = assignment.AssignmentId,
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ,
                Context = DFMPSpawnProtocol.GetAssignedContextReport(assignment)
            };

            if (!transitionState.TryAcknowledge(acknowledgement, false))
                return;

            NetworkClient.Send(acknowledgement);
            hasImmediateWorldContextReport = DFMPTransitionReportPolicy.ShouldReportWorldContextImmediatelyAfterAcknowledgement(assignment.Kind);
            SuppressPositionReportsBriefly();
            RevealReconnectHud();
            Debug.Log($"[DFMP Transition] Client acknowledged interior reconnect: assignmentId={acknowledgement.AssignmentId}, world={acknowledgement.WorldX}/0/{acknowledgement.WorldZ}.");
        }

        void CoverReconnectHud()
        {
            if (reconnectHudCovered)
                return;

            if (DaggerfallUI.Instance != null && DaggerfallUI.Instance.FadeBehaviour != null)
            {
                DaggerfallUI.Instance.FadeBehaviour.SmashHUDToBlack();
                reconnectHudCovered = true;
            }
        }

        void RevealReconnectHud()
        {
            if (!reconnectHudCovered)
                return;

            if (DaggerfallUI.Instance != null && DaggerfallUI.Instance.FadeBehaviour != null)
                DaggerfallUI.Instance.FadeBehaviour.FadeHUDFromBlack(0.7f);

            reconnectHudCovered = false;
        }

        bool TryOpenAssignedInterior(DFMPTransitionAssignment assignment)
        {
            if (!GameManager.HasInstance || GameManager.Instance.PlayerEnterExit == null)
                return false;

            DFLocation location;
            if (!TryResolveAssignedLocation(assignment, out location))
                return false;

            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
            doorHookBypass = true;
            try
            {
                if (assignment.ContextKind == DFMPWorldContextKind.BuildingInterior)
                {
                    if (!assignment.HasExteriorDoor)
                        return false;

                    playerEnterExit.StartBuildingInterior(location, assignment.ExteriorDoor.ToStaticDoor(), true);
                    return playerEnterExit.IsPlayerInsideBuilding;
                }

                if (assignment.ContextKind == DFMPWorldContextKind.Dungeon)
                {
                    playerEnterExit.StartDungeonInterior(location, preferEnterMarker: true, importEnemies: false);
                    return playerEnterExit.IsPlayerInsideDungeon;
                }
            }
            finally
            {
                doorHookBypass = false;
            }

            return false;
        }

        bool TryResolveAssignedLocation(DFMPTransitionAssignment assignment, out DFLocation location)
        {
            location = default(DFLocation);
            if (DaggerfallUnity.Instance == null ||
                DaggerfallUnity.Instance.ContentReader == null ||
                DaggerfallUnity.Instance.ContentReader.MapFileReader == null)
                return false;

            MapsFile mapFileReader = DaggerfallUnity.Instance.ContentReader.MapFileReader;
            if (assignment.RegionIndex >= 0 && assignment.LocationIndex >= 0)
            {
                location = mapFileReader.GetLocation(assignment.RegionIndex, assignment.LocationIndex);
                if (location.Loaded)
                    return true;
            }

            if (string.IsNullOrWhiteSpace(assignment.LocationId))
                return false;

            if (assignment.RegionIndex >= 0)
            {
                string regionName = mapFileReader.GetRegionName(assignment.RegionIndex);
                if (!string.IsNullOrWhiteSpace(regionName))
                {
                    location = mapFileReader.GetLocation(regionName, assignment.LocationId);
                    if (location.Loaded)
                        return true;
                }
            }

            for (int region = 0; region < mapFileReader.RegionCount; region++)
            {
                string regionName = mapFileReader.GetRegionName(region);
                if (string.IsNullOrWhiteSpace(regionName))
                    continue;

                location = mapFileReader.GetLocation(regionName, assignment.LocationId);
                if (location.Loaded)
                    return true;
            }

            return false;
        }

        void ApplyInteriorLocalPose(DFMPTransitionAssignment assignment)
        {
            if (!GameManager.HasInstance || GameManager.Instance.PlayerEnterExit == null)
                return;

            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
            Vector3 localPosition = new Vector3(assignment.InteriorLocalX, assignment.InteriorLocalY, assignment.InteriorLocalZ);
            if (!DFMPInteriorReopenProtocol.IsValidInteriorLocalPosition(localPosition))
            {
                Debug.LogWarning($"[DFMP Transition] Ignored invalid interior pose on reconnect: assignmentId={assignment.AssignmentId}, local={localPosition}.");
                return;
            }

            Transform parent = null;
            if (assignment.ContextKind == DFMPWorldContextKind.Dungeon && playerEnterExit.Dungeon != null)
                parent = playerEnterExit.Dungeon.transform;
            else if (assignment.ContextKind == DFMPWorldContextKind.BuildingInterior && playerEnterExit.Interior != null)
                parent = playerEnterExit.Interior.transform;

            if (parent == null)
                return;

            CharacterController controller = GameManager.Instance.PlayerController;
            float controllerHeight = controller != null ? controller.height : PlayerHeightChanger.controllerStandingHeight;
            float controllerCenterY = controller != null ? controller.center.y : 0f;
            float controllerSkinWidth = controller != null ? controller.skinWidth : 0f;

            // The persisted pose is the controller's feet; DFU keeps the player transform at capsule centre.
            Vector3 worldPosition = parent.position + localPosition;
            worldPosition.y = DFMPPositionProtocol.GetControllerCentreY(
                parent.position.y + localPosition.y,
                controllerCenterY,
                controllerHeight,
                controllerSkinWidth);

            playerEnterExit.transform.position = worldPosition;
            SnapPlayerToInteriorGround(playerEnterExit.transform, controllerHeight);
        }

        /// <summary>
        /// Mirrors the ground snap DFU applies in <c>PlayerEnterExit.SetStanding</c> after an interior relocation.
        /// </summary>
        static void SnapPlayerToInteriorGround(Transform playerTransform, float controllerHeight)
        {
            RaycastHit hit;
            var ray = new Ray(playerTransform.position, Vector3.down);
            if (!Physics.Raycast(ray, out hit, PlayerHeightChanger.controllerStandingHeight * 2f))
                return;

            if (GameManager.HasInstance && GameManager.Instance.AcrobatMotor != null)
                GameManager.Instance.AcrobatMotor.ClearFallingDamage();

            Vector3 groundedPosition = hit.point;
            groundedPosition.y += controllerHeight / 2f + 0.25f;
            playerTransform.position = groundedPosition;
        }

        bool IsAssignedInteriorReady(DFMPTransitionAssignment assignment)
        {
            if (!GameManager.HasInstance || GameManager.Instance.PlayerEnterExit == null)
                return false;

            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
            if (assignment.ContextKind == DFMPWorldContextKind.BuildingInterior)
                return playerEnterExit.IsPlayerInsideBuilding;
            if (assignment.ContextKind == DFMPWorldContextKind.Dungeon)
                return playerEnterExit.IsPlayerInsideDungeon;
            return true;
        }

        bool ApplyVampirismTransformation()
        {
            if (!GameManager.HasInstance || GameObject.FindGameObjectWithTag("Player") == null)
                return false;

            VampirismInfection infection = (VampirismInfection)GameManager.Instance.PlayerEffectManager.FindIncumbentEffect<VampirismInfection>();
            VampireClans clan = infection != null ? infection.InfectionVampireClan : VampireClans.Lyrezi;
            GameManager.Instance.PlayerEntity.PreventEnemySpawns = true;
            GameManager.Instance.PlayerEntity.AssignPlayerVampireSpells(clan);
            EntityEffectBundle bundle = GameManager.Instance.PlayerEffectManager.CreateVampirismCurse();
            GameManager.Instance.PlayerEffectManager.AssignBundle(bundle, AssignBundleFlags.BypassSavingThrows);
            Debug.Log($"[DFMP Transition] Applied vampirism transformation after assigned relocation: clan={clan}.");
            return true;
        }

        bool ApplyDeathRespawn()
        {
            if (!GameManager.HasInstance || GameObject.FindGameObjectWithTag("Player") == null)
                return false;

            GameManager.Instance.PlayerEntity.CurrentHealth = GameManager.Instance.PlayerEntity.MaxHealth;
            GameManager.Instance.PlayerEntity.CurrentFatigue = GameManager.Instance.PlayerEntity.MaxFatigue;
            GameManager.Instance.PlayerEntity.CurrentMagicka = GameManager.Instance.PlayerEntity.MaxMagicka;
            Debug.Log("[DFMP Respawn] Restored player vitals after assigned death respawn.");
            return true;
        }

        void UpdateDoorTransitionAssignment(StreamingWorld streamingWorld)
        {
            if (transitionState.TryRequestTeleport())
            {
                transitionApplied = false;
                SuppressPositionReportsBriefly();
                doorHookBypass = true;
                try
                {
                    if (pendingDoorEnterInterior)
                        pendingDoorPlayerEnterExit.TransitionInterior(pendingDoorOwner, pendingDoor, pendingDoorDoFade, false);
                    else
                        pendingDoorPlayerEnterExit.TransitionExterior(pendingDoorDoFade);
                }
                finally
                {
                    doorHookBypass = false;
                }

                transitionApplied = true;
                Debug.Log($"[DFMP Transition] Client executed assigned door transition: assignmentId={transitionState.Assignment.AssignmentId}, context={transitionState.Assignment.ContextKind}, buildingKey={transitionState.Assignment.BuildingKey}.");
                return;
            }

            if (!transitionApplied || streamingWorld.IsRepositioningPlayer)
                return;

            var acknowledgement = new DFMPTransitionAcknowledgement
            {
                AssignmentId = transitionState.Assignment.AssignmentId,
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ,
                Context = DFMPSpawnProtocol.GetAssignedContextReport(transitionState.Assignment)
            };

            if (!transitionState.TryAcknowledge(acknowledgement, false))
                return;

            NetworkClient.Send(acknowledgement);
            ClearPendingDoorTransition();
            SuppressPositionReportsBriefly();
            Debug.Log($"[DFMP Transition] Client acknowledged assigned door transition: assignmentId={acknowledgement.AssignmentId}, world={acknowledgement.WorldX}/0/{acknowledgement.WorldZ}.");
        }

        void UpdateDungeonTransitionAssignment(StreamingWorld streamingWorld)
        {
            if (transitionState.TryRequestTeleport())
            {
                transitionApplied = false;
                SuppressPositionReportsBriefly();
                doorHookBypass = true;
                try
                {
                    if (pendingDungeonEnter)
                        pendingDungeonPlayerEnterExit.TransitionDungeonInterior(pendingDungeonDoorOwner, pendingDungeonDoor, pendingDungeonLocation, pendingDungeonDoFade);
                    else
                        pendingDungeonPlayerEnterExit.TransitionDungeonExterior(pendingDungeonDoFade);
                }
                finally
                {
                    doorHookBypass = false;
                }

                transitionApplied = true;
                Debug.Log($"[DFMP Transition] Client executed assigned dungeon transition: assignmentId={transitionState.Assignment.AssignmentId}, kind={transitionState.Assignment.Kind}, location='{transitionState.Assignment.LocationId}'.");
                return;
            }

            if (!transitionApplied || streamingWorld.IsRepositioningPlayer)
                return;

            var acknowledgement = new DFMPTransitionAcknowledgement
            {
                AssignmentId = transitionState.Assignment.AssignmentId,
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ,
                Context = DFMPSpawnProtocol.GetAssignedContextReport(transitionState.Assignment)
            };

            if (!transitionState.TryAcknowledge(acknowledgement, false))
                return;

            NetworkClient.Send(acknowledgement);
            hasImmediateWorldContextReport = DFMPTransitionReportPolicy.ShouldReportWorldContextImmediatelyAfterAcknowledgement(transitionState.Assignment.Kind);
            ClearPendingDungeonTransition();
            SuppressPositionReportsBriefly();
            Debug.Log($"[DFMP Transition] Client acknowledged assigned dungeon transition: assignmentId={acknowledgement.AssignmentId}, world={acknowledgement.WorldX}/0/{acknowledgement.WorldZ}.");
        }

        void ClearPendingDoorTransition()
        {
            hasPendingDoorTransition = false;
            pendingDoorPlayerEnterExit = null;
            pendingDoorOwner = null;
            pendingDoor = default(StaticDoor);
            pendingDoorEnterInterior = false;
            pendingDoorDoFade = false;
        }

        void ClearPendingDungeonTransition()
        {
            hasPendingDungeonTransition = false;
            pendingDungeonPlayerEnterExit = null;
            pendingDungeonDoorOwner = null;
            pendingDungeonDoor = default(StaticDoor);
            pendingDungeonLocation = default(DFLocation);
            pendingDungeonEnter = false;
            pendingDungeonDoFade = false;
        }

        bool TryGetAssignedStartMarker(StreamingWorld streamingWorld, int mapPixelX, int mapPixelY, string markerName, out Vector3 markerPosition)
        {
            markerPosition = Vector3.zero;

            DaggerfallLocation location = streamingWorld.CurrentPlayerLocationObject;
            if (location == null ||
                location.Summary.MapPixelX != mapPixelX ||
                location.Summary.MapPixelY != mapPixelY)
                return false;

            GameObject[] startMarkers = location.StartMarkers;
            if (startMarkers == null || startMarkers.Length == 0)
                return false;

            if (!string.IsNullOrWhiteSpace(markerName))
            {
                for (int index = 0; index < startMarkers.Length; index++)
                {
                    GameObject startMarker = startMarkers[index];
                    if (startMarker != null && string.Equals(startMarker.name, markerName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    {
                        markerPosition = startMarker.transform.position;
                        return true;
                    }
                }
            }

            var markerPositions = new Vector3[startMarkers.Length];
            for (int index = 0; index < startMarkers.Length; index++)
                markerPositions[index] = startMarkers[index] != null ? startMarkers[index].transform.position : Vector3.zero;

            int markerIndex;
            if (DFMPSpawnProtocol.TrySelectStartMarker(markerPositions, markerName, out markerIndex) && markerIndex >= 0 && markerIndex < startMarkers.Length && startMarkers[markerIndex] != null)
            {
                markerPosition = startMarkers[markerIndex].transform.position;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(markerName))
                return false;

            // Sorting by coordinate keeps the choice identical on every client regardless of enumeration order.
            bool found = false;
            foreach (GameObject startMarker in startMarkers)
            {
                if (startMarker == null)
                    continue;

                Vector3 candidate = startMarker.transform.position;
                if (!found || candidate.x < markerPosition.x || (candidate.x == markerPosition.x && candidate.z < markerPosition.z))
                {
                    markerPosition = candidate;
                    found = true;
                }
            }

            return found;
        }

        bool TryApplyAssignedStartMarker(StreamingWorld streamingWorld, int mapPixelX, int mapPixelY, string markerName)
        {
            Vector3 markerPosition;
            if (!TryGetAssignedStartMarker(streamingWorld, mapPixelX, mapPixelY, markerName, out markerPosition))
                return false;

            Transform playerTransform = streamingWorld.LocalPlayerGPS.transform;
            CharacterController controller = playerTransform.GetComponent<CharacterController>();
            float standingOffset = controller != null ? (controller.height * 0.5f) + 0.15f : 1.0f;

            Vector3 spawnPosition = markerPosition + Vector3.up * standingOffset;
            RaycastHit hit;
            if (Physics.Raycast(markerPosition + Vector3.up * GroundProbeHeight, Vector3.down, out hit, GroundProbeHeight + GroundProbeDepth))
                spawnPosition = hit.point + Vector3.up * standingOffset;

            playerTransform.position = spawnPosition;

            if (GameManager.Instance.AcrobatMotor != null)
                GameManager.Instance.AcrobatMotor.ClearFallingDamage();

            Debug.Log($"[DFMP Session] Client placed at shared test spawn point: scene={spawnPosition.x}/{spawnPosition.y}/{spawnPosition.z}.");
            return true;
        }
    }
}
