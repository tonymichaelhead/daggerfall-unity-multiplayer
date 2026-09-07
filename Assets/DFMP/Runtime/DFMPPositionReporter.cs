using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPPositionReporter : MonoBehaviour
    {
        static DFMPPositionReporter instance;
        float nextReportTime;
        float nextIdentityReportTime;

        public static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject reporterGo = new GameObject("DFMP_PositionReporter");
            Object.DontDestroyOnLoad(reporterGo);
            instance = reporterGo.AddComponent<DFMPPositionReporter>();
        }

        public static void Reset()
        {
            if (instance != null)
                Destroy(instance.gameObject);

            instance = null;
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void Update()
        {
            if (!NetworkClient.isConnected || Time.unscaledTime < nextReportTime)
                return;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null || !streamingWorld.IsReady || streamingWorld.LocalPlayerGPS == null)
                return;

            nextReportTime = Time.unscaledTime + DFMPPositionProtocol.MinimumReportInterval;
            SendIdentityReport();
            NetworkClient.Send(new DFMPPlayerPositionReport
            {
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ,
                FacingYaw = streamingWorld.LocalPlayerGPS.transform.eulerAngles.y
            }, Channels.Unreliable);
        }

        void SendIdentityReport()
        {
            if (Time.unscaledTime < nextIdentityReportTime || GameManager.Instance == null || GameManager.Instance.PlayerEntity == null)
                return;

            if (DFMPClientJoinFlowController.Instance != null &&
                !DFMPClientJoinFlowController.Instance.ShouldReportLocalIdentity())
                return;

            nextIdentityReportTime = Time.unscaledTime + 5f;
            var playerEntity = GameManager.Instance.PlayerEntity;
            NetworkClient.Send(new DFMPPlayerIdentityReport
            {
                DisplayName = playerEntity.Name,
                Race = (int)playerEntity.Race,
                Gender = (int)playerEntity.Gender,
                OutfitVariant = DFMPPositionProtocol.GetInitialOutfitVariant(playerEntity.FaceIndex),
                FaceVariant = playerEntity.FaceIndex
            });
        }
    }
}