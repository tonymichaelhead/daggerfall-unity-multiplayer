using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
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

        static DFMPSpawnAssignmentController instance;
        public static int LocalConnectionId { get; private set; } = -1;
        readonly DFMPSpawnAssignmentState assignmentState = new DFMPSpawnAssignmentState();
        readonly DFMPTransitionAssignmentState transitionState = new DFMPTransitionAssignmentState();
        float fixedSpawnWaitDeadline;
        bool sharedTestSpawnApplied;
        bool transitionApplied;

        public static void RegisterClientHandler()
        {
            NetworkClient.RegisterHandler<DFMPSpawnAssignment>(OnSpawnAssignment);
            NetworkClient.RegisterHandler<DFMPTransitionAssignment>(OnTransitionAssignment);
            EnsureInstance();
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
                Destroy(instance.gameObject);

            instance = null;
            LocalConnectionId = -1;
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
            Debug.Log($"[DFMP Session] Client received spawn assignment: mapPixel={assignment.MapPixelX}/{assignment.MapPixelY}.");
        }

        void SetPendingTransitionAssignment(DFMPTransitionAssignment assignment)
        {
            transitionState.Receive(assignment);
            if (assignment.ConnectionId >= 0)
                LocalConnectionId = assignment.ConnectionId;
            transitionApplied = false;
            Debug.Log($"[DFMP Transition] Client received transition assignment: assignmentId={assignment.AssignmentId}, kind={assignment.Kind}, mapPixel={assignment.MapPixelX}/{assignment.MapPixelY}.");
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
                Debug.Log($"[DFMP Transition] Client reapplied pending transition assignment after {reason}: assignmentId={transitionState.Assignment.AssignmentId}, kind={transitionState.Assignment.Kind}, mapPixel={transitionState.Assignment.MapPixelX}/{transitionState.Assignment.MapPixelY}.");
            }
        }

        void Update()
        {
            if ((!assignmentState.HasPendingAssignment && !transitionState.HasPendingAssignment) || !NetworkClient.isConnected)
                return;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null || !streamingWorld.IsReady)
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

            Debug.Log($"[DFMP Session] Client acknowledged grounded spawn: world={streamingWorld.LocalPlayerGPS.WorldX}/0/{streamingWorld.LocalPlayerGPS.WorldZ}.");
        }

        void UpdateTransitionAssignment(StreamingWorld streamingWorld)
        {
            if (transitionState.TryRequestTeleport())
            {
                fixedSpawnWaitDeadline = Time.realtimeSinceStartup + FixedSpawnLocationWaitSeconds;
                transitionApplied = false;
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
            Debug.Log($"[DFMP Transition] Client acknowledged assigned transition: assignmentId={acknowledgement.AssignmentId}, world={acknowledgement.WorldX}/0/{acknowledgement.WorldZ}.");
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