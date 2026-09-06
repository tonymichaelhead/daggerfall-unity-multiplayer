using DaggerfallWorkshop;
using Mirror;
using UnityEngine;

namespace DFCoop.Runtime
{
    public class DFCoopPositionReporter : MonoBehaviour
    {
        static DFCoopPositionReporter instance;
        float nextReportTime;

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
            NetworkClient.Send(new DFCoopPlayerPositionReport
            {
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ
            }, Channels.Unreliable);
        }
    }
}