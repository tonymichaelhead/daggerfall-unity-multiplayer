using DaggerfallWorkshop;
using Mirror;
using UnityEngine;

namespace DFCoop.Runtime
{
    public class DFCoopTimeState : NetworkBehaviour
    {
        const float ServerPublishInterval = 1f;
        public const uint AssetId = 0xdfc001u;

        [SyncVar(hook = nameof(OnClassicMinutesChanged))]
        uint classicMinutes;

        [SyncVar(hook = nameof(OnTimeScaleChanged))]
        float timeScale = 12f;

        float nextServerPublishTime;
        bool clientApplyPending;

        public static void RegisterClientSpawnHandler()
        {
            NetworkClient.RegisterSpawnHandler(AssetId, SpawnClientTimeState, UnspawnClientTimeState);
        }

        public uint ClassicMinutes
        {
            get { return classicMinutes; }
        }

        public float TimeScale
        {
            get { return timeScale; }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            PublishServerTime(true);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            clientApplyPending = true;
            Debug.Log($"[DFCoop Time] Client received time state: classicMinutes={classicMinutes}, timeScale={timeScale:F2}.");
        }

        void Update()
        {
            if (netIdentity == null || netIdentity.netId == 0)
                return;

            if (NetworkServer.active)
                PublishServerTime(false);

            if (NetworkClient.active && !NetworkServer.active && clientApplyPending)
                ApplyClientTime();
        }

        void PublishServerTime(bool force)
        {
            if (!force && Time.unscaledTime < nextServerPublishTime)
                return;

            nextServerPublishTime = Time.unscaledTime + ServerPublishInterval;

            var worldTime = GetWorldTime();
            if (worldTime == null)
                return;

            var snapshot = DFCoopTimeSnapshot.FromWorldTime(worldTime);
            bool changed = classicMinutes != snapshot.ClassicMinutes || !Mathf.Approximately(timeScale, snapshot.TimeScale);
            classicMinutes = snapshot.ClassicMinutes;
            timeScale = snapshot.TimeScale;

            if (force || changed)
                Debug.Log($"[DFCoop Time] Server published time: classicMinutes={classicMinutes}, timeScale={timeScale:F2}.");
        }

        void ApplyClientTime()
        {
            var worldTime = GetWorldTime();
            if (worldTime == null)
                return;

            var snapshot = new DFCoopTimeSnapshot
            {
                ClassicMinutes = classicMinutes,
                TimeScale = timeScale
            };

            snapshot.ApplyToWorldTime(worldTime);
            clientApplyPending = false;

            Debug.Log($"[DFCoop Time] Client applied time: classicMinutes={classicMinutes}, timeScale={timeScale:F2}.");
        }

        void OnClassicMinutesChanged(uint oldValue, uint newValue)
        {
            clientApplyPending = true;
            Debug.Log($"[DFCoop Time] Client time update: classicMinutes={newValue}.");
        }

        void OnTimeScaleChanged(float oldValue, float newValue)
        {
            clientApplyPending = true;
            Debug.Log($"[DFCoop Time] Client time scale update: timeScale={newValue:F2}.");
        }

        static WorldTime GetWorldTime()
        {
            if (DaggerfallUnity.Instance == null)
                return null;

            return DaggerfallUnity.Instance.WorldTime;
        }

        static GameObject SpawnClientTimeState(SpawnMessage message)
        {
            GameObject timeGo = new GameObject("DFCoop_TimeState");
            timeGo.SetActive(false);

            timeGo.AddComponent<NetworkIdentity>();
            timeGo.AddComponent<DFCoopTimeState>();
            Object.DontDestroyOnLoad(timeGo);
            timeGo.SetActive(true);

            Debug.Log($"[DFCoop Time] Client spawned time state: netId={message.netId}.");
            return timeGo;
        }

        static void UnspawnClientTimeState(GameObject spawned)
        {
            if (spawned != null)
                Destroy(spawned);
        }
    }
}