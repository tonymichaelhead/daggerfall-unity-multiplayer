using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using Mirror;
using UnityEngine;

namespace DFCoop.Runtime
{
    public class DFCoopPositionReporter : MonoBehaviour
    {
        static DFCoopPositionReporter instance;
        float nextReportTime;
        float nextIdentityReportTime;

        public static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject reporterGo = new GameObject("DFCoop_PositionReporter");
            Object.DontDestroyOnLoad(reporterGo);
            instance = reporterGo.AddComponent<DFCoopPositionReporter>();
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

            nextReportTime = Time.unscaledTime + DFCoopPositionProtocol.MinimumReportInterval;
            SendIdentityReport();
            NetworkClient.Send(new DFCoopPlayerPositionReport
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

            nextIdentityReportTime = Time.unscaledTime + 5f;
            var playerEntity = GameManager.Instance.PlayerEntity;
            NetworkClient.Send(new DFCoopPlayerIdentityReport
            {
                DisplayName = playerEntity.Name,
                Race = (int)playerEntity.Race,
                Gender = (int)playerEntity.Gender,
                OutfitVariant = DFCoopPositionProtocol.GetInitialOutfitVariant(playerEntity.FaceIndex),
                FaceVariant = playerEntity.FaceIndex
            });
        }
    }
}