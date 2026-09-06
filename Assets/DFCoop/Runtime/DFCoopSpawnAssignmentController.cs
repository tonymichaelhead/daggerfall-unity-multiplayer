using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using Mirror;
using UnityEngine;

namespace DFCoop.Runtime
{
    public struct DFCoopSpawnAssignment : NetworkMessage
    {
        public int ConnectionId;
        public int MapPixelX;
        public int MapPixelY;
    }

    public struct DFCoopSpawnAcknowledgement : NetworkMessage
    {
        public int WorldX;
        public float WorldY;
        public int WorldZ;
    }

    public class DFCoopSpawnAssignmentController : MonoBehaviour
    {
        static DFCoopSpawnAssignmentController instance;
        public static int LocalConnectionId { get; private set; } = -1;
        readonly DFCoopSpawnAssignmentState assignmentState = new DFCoopSpawnAssignmentState();

        public static void RegisterClientHandler()
        {
            NetworkClient.RegisterHandler<DFCoopSpawnAssignment>(OnSpawnAssignment);
            EnsureInstance();
        }

        static void OnSpawnAssignment(DFCoopSpawnAssignment assignment)
        {
            EnsureInstance().SetPendingAssignment(assignment);
        }

        static DFCoopSpawnAssignmentController EnsureInstance()
        {
            if (instance != null)
                return instance;

            GameObject controllerGo = new GameObject("DFCoop_SpawnAssignmentController");
            Object.DontDestroyOnLoad(controllerGo);
            instance = controllerGo.AddComponent<DFCoopSpawnAssignmentController>();
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

        void SetPendingAssignment(DFCoopSpawnAssignment assignment)
        {
            assignmentState.Receive(assignment);
            LocalConnectionId = assignment.ConnectionId;
            Debug.Log($"[DFCoop Session] Client received spawn assignment: mapPixel={assignment.MapPixelX}/{assignment.MapPixelY}.");
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
            if (!assignmentState.HasAssignment)
                return;

            assignmentState.Requeue();
            Debug.Log($"[DFCoop Session] Client reapplied pending spawn assignment after {reason}: mapPixel={assignmentState.Assignment.MapPixelX}/{assignmentState.Assignment.MapPixelY}.");
        }

        void Update()
        {
            if (!assignmentState.HasPendingAssignment || !NetworkClient.isConnected)
                return;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null || !streamingWorld.IsReady)
                return;

            if (assignmentState.TryRequestTeleport())
            {
                streamingWorld.TeleportToCoordinates(assignmentState.Assignment.MapPixelX, assignmentState.Assignment.MapPixelY, StreamingWorld.RepositionMethods.RandomStartMarker);
                Debug.Log($"[DFCoop Session] Client requested grounded city spawn: mapPixel={assignmentState.Assignment.MapPixelX}/{assignmentState.Assignment.MapPixelY}.");
                return;
            }

            if (!assignmentState.TryAcknowledge(streamingWorld.IsRepositioningPlayer, streamingWorld.LocalPlayerGPS.CurrentMapPixel.X, streamingWorld.LocalPlayerGPS.CurrentMapPixel.Y))
                return;

            NetworkClient.Send(new DFCoopSpawnAcknowledgement
            {
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ
            });

            Debug.Log($"[DFCoop Session] Client acknowledged grounded spawn: world={streamingWorld.LocalPlayerGPS.WorldX}/0/{streamingWorld.LocalPlayerGPS.WorldZ}.");
        }
    }
}