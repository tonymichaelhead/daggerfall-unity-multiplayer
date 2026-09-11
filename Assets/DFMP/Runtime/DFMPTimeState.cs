using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPTimeState : NetworkBehaviour
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

        public bool TryAdvanceServerTime(uint targetClassicMinutes)
        {
            if (!NetworkServer.active)
                return false;

            var worldTime = GetWorldTime();
            if (worldTime == null || targetClassicMinutes < worldTime.DaggerfallDateTime.ToClassicDaggerfallTime())
                return false;

            worldTime.DaggerfallDateTime.FromClassicDaggerfallTime(targetClassicMinutes);
            classicMinutes = targetClassicMinutes;
            timeScale = worldTime.TimeScale;
            Debug.Log($"[DFMP Time] Server advanced time: classicMinutes={targetClassicMinutes}, timeScale={timeScale:F2}.");
            return true;
        }

        public bool TryAdvanceServerTimeByMinutes(int minutes, out uint targetClassicMinutes)
        {
            targetClassicMinutes = 0;
            if (minutes <= 0)
                return false;

            var worldTime = GetWorldTime();
            if (worldTime == null)
                return false;

            ulong target = (ulong)worldTime.DaggerfallDateTime.ToClassicDaggerfallTime() + (ulong)minutes;
            if (target > uint.MaxValue)
                return false;

            targetClassicMinutes = (uint)target;
            return TryAdvanceServerTime(targetClassicMinutes);
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
            Debug.Log($"[DFMP Time] Client received time state: classicMinutes={classicMinutes}, timeScale={timeScale:F2}.");
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

            var snapshot = DFMPTimeSnapshot.FromWorldTime(worldTime);
            bool changed = classicMinutes != snapshot.ClassicMinutes || !Mathf.Approximately(timeScale, snapshot.TimeScale);
            classicMinutes = snapshot.ClassicMinutes;
            timeScale = snapshot.TimeScale;

            if (force || changed)
                Debug.Log($"[DFMP Time] Server published time: classicMinutes={classicMinutes}, timeScale={timeScale:F2}.");
        }

        void ApplyClientTime()
        {
            var worldTime = GetWorldTime();
            if (worldTime == null)
                return;

            var snapshot = new DFMPTimeSnapshot
            {
                ClassicMinutes = classicMinutes,
                TimeScale = timeScale
            };

            snapshot.ApplyToWorldTime(worldTime);
            uint previousPlayerMinutes;
            if (TryClampPlayerLastGameMinutes(classicMinutes, out previousPlayerMinutes))
                Debug.Log($"[DFMP Time] Client corrected player tracked time after server rollback: lastGameMinutes={previousPlayerMinutes}, classicMinutes={classicMinutes}.");
            clientApplyPending = false;

            Debug.Log($"[DFMP Time] Client applied time: classicMinutes={classicMinutes}, timeScale={timeScale:F2}.");
        }

        public static bool ShouldClampPlayerLastGameMinutes(uint playerLastGameMinutes, uint serverClassicMinutes)
        {
            return playerLastGameMinutes > serverClassicMinutes;
        }

        static bool TryClampPlayerLastGameMinutes(uint serverClassicMinutes, out uint previousPlayerMinutes)
        {
            previousPlayerMinutes = 0;
            if (GameManager.Instance == null || GameManager.Instance.PlayerEntity == null)
                return false;

            previousPlayerMinutes = GameManager.Instance.PlayerEntity.LastGameMinutes;
            if (!ShouldClampPlayerLastGameMinutes(previousPlayerMinutes, serverClassicMinutes))
                return false;

            GameManager.Instance.PlayerEntity.LastGameMinutes = serverClassicMinutes;
            return true;
        }

        void OnClassicMinutesChanged(uint oldValue, uint newValue)
        {
            clientApplyPending = true;
            Debug.Log($"[DFMP Time] Client time update: classicMinutes={newValue}.");
        }

        void OnTimeScaleChanged(float oldValue, float newValue)
        {
            clientApplyPending = true;
            Debug.Log($"[DFMP Time] Client time scale update: timeScale={newValue:F2}.");
        }

        static WorldTime GetWorldTime()
        {
            if (DaggerfallUnity.Instance == null)
                return null;

            return DaggerfallUnity.Instance.WorldTime;
        }

        static GameObject SpawnClientTimeState(SpawnMessage message)
        {
            GameObject timeGo = new GameObject("DFMP_TimeState");
            timeGo.SetActive(false);

            timeGo.AddComponent<NetworkIdentity>();
            timeGo.AddComponent<DFMPTimeState>();
            Object.DontDestroyOnLoad(timeGo);
            timeGo.SetActive(true);

            Debug.Log($"[DFMP Time] Client spawned time state: netId={message.netId}.");
            return timeGo;
        }

        static void UnspawnClientTimeState(GameObject spawned)
        {
            if (spawned != null)
                Destroy(spawned);
        }
    }
}