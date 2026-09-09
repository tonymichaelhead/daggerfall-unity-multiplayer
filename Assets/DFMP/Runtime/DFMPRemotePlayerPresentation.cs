using System.Collections.Generic;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPRemotePlayerPresentation
    {
        public const float MaximumVisibleDistance = 150f;
        public const float PositionSmoothingSpeed = 12f;

        public static bool IsRemoteSession(DFMPPlayerSessionState sessionState, int localConnectionId)
        {
            return sessionState != null && sessionState.SpawnConfirmed && localConnectionId >= 0 && sessionState.ConnectionId != localConnectionId;
        }

        public static Vector3 WorldToScenePosition(PlayerGPS localPlayerGPS, Vector3 localPlayerScenePosition, int worldX, int worldZ)
        {
            return localPlayerScenePosition + new Vector3(
                (worldX - localPlayerGPS.WorldX) / StreamingWorld.SceneMapRatio,
                0f,
                (worldZ - localPlayerGPS.WorldZ) / StreamingWorld.SceneMapRatio);
        }

        public static Vector3 GroundScenePosition(StreamingWorld streamingWorld, Vector3 scenePosition)
        {
            GameObject terrainObject = streamingWorld.GetTerrainFromPixel(streamingWorld.LocalPlayerGPS.CurrentMapPixel);
            if (terrainObject == null)
                return scenePosition;

            Terrain terrain = terrainObject.GetComponent<Terrain>();
            if (terrain == null)
                return scenePosition;

            scenePosition.y = terrain.SampleHeight(scenePosition + terrain.transform.position) + streamingWorld.WorldCompensation.y;
            return scenePosition;
        }

        public static Vector3 ApplySceneHeightOffset(Vector3 groundPosition, float sceneHeightOffset)
        {
            groundPosition.y += sceneHeightOffset;
            return groundPosition;
        }

        public static bool IsVisibleInLocalMapPixel(Vector3 localScenePosition, Vector3 remoteScenePosition)
        {
            return Vector3.SqrMagnitude(remoteScenePosition - localScenePosition) <= MaximumVisibleDistance * MaximumVisibleDistance;
        }

        public static Vector3 InterpolatePosition(Vector3 currentPosition, Vector3 targetPosition, float deltaTime)
        {
            float interpolationFactor = 1f - Mathf.Exp(-PositionSmoothingSpeed * Mathf.Max(0f, deltaTime));
            return Vector3.Lerp(currentPosition, targetPosition, interpolationFactor);
        }

        public static Quaternion GetLabelBillboardRotation(Vector3 labelPosition, Vector3 cameraPosition)
        {
            Vector3 directionToCamera = labelPosition - cameraPosition;
            directionToCamera.y = 0f;
            return directionToCamera.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(directionToCamera) : Quaternion.identity;
        }

        public static bool IsAppearanceChanged(int cachedMobileType, int cachedGender, DFMPPlayerSessionState session)
        {
            return cachedMobileType != session.AvatarMobileType || cachedGender != session.Gender;
        }

        public static MobileStates GetMovementState(bool isMoving)
        {
            return isMoving ? MobileStates.Move : MobileStates.Idle;
        }

        public static MobileStates GetActionState(DFMPPlayerActionKind action)
        {
            switch (action)
            {
                case DFMPPlayerActionKind.RangedAttack:
                    return MobileStates.RangedAttack1;
                case DFMPPlayerActionKind.Spell:
                    return MobileStates.Spell;
                default:
                    return MobileStates.PrimaryAttack;
            }
        }
    }

    public class DFMPRemoteAvatarAppearance : MonoBehaviour
    {
        int mobileType = int.MinValue;
        int gender = int.MinValue;
        uint actionSequence;

        public void ApplyIfChanged(DFMPPlayerSessionState session)
        {
            var mobile = GetComponentInChildren<DaggerfallMobileUnit>();
            if (DFMPRemotePlayerPresentation.IsAppearanceChanged(mobileType, gender, session))
            {
                MobileEnemy enemy;
                if (EnemyBasics.GetEnemy((MobileTypes)session.AvatarMobileType, out enemy))
                {
                    enemy.Gender = session.Gender == 1 ? MobileGender.Female : MobileGender.Male;
                    mobile.SetEnemy(DaggerfallUnity.Instance, enemy, MobileReactions.Custom, 0);
                    mobile.ChangeEnemyState(DFMPRemotePlayerPresentation.GetMovementState(session.IsMoving));
                    transform.localPosition = new Vector3(0f, mobile.GetSize().y * 0.5f, 0f);
                }

                mobileType = session.AvatarMobileType;
                gender = session.Gender;
                actionSequence = session.ActionSequence;
            }

            if (actionSequence != session.ActionSequence)
            {
                actionSequence = session.ActionSequence;
                mobile.ChangeEnemyState(DFMPRemotePlayerPresentation.GetActionState(session.ActionKind));
            }

            if (!mobile.IsPlayingOneShot())
                mobile.ChangeEnemyState(DFMPRemotePlayerPresentation.GetMovementState(session.IsMoving));
        }
    }

    public class DFMPRemotePlayerPresentationController : MonoBehaviour
    {
        static DFMPRemotePlayerPresentationController instance;
        readonly Dictionary<int, GameObject> proxies = new Dictionary<int, GameObject>();

        public static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject controllerGo = new GameObject("DFMP_RemotePlayerPresentationController");
            Object.DontDestroyOnLoad(controllerGo);
            instance = controllerGo.AddComponent<DFMPRemotePlayerPresentationController>();
        }

        public static void Reset()
        {
            if (instance != null)
                Destroy(instance.gameObject);

            instance = null;
        }

        void OnDestroy()
        {
            foreach (GameObject proxy in proxies.Values)
            {
                if (proxy != null)
                    Destroy(proxy);
            }

            proxies.Clear();
            if (instance == this)
                instance = null;
        }

        void Update()
        {
            if (!NetworkClient.isConnected)
                return;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null || !streamingWorld.IsReady || streamingWorld.LocalPlayerGPS == null)
                return;

            var liveProxyIds = new HashSet<int>();
            DFMPPlayerSessionState[] sessions = FindObjectsOfType<DFMPPlayerSessionState>();
            foreach (DFMPPlayerSessionState session in sessions)
            {
                if (!DFMPRemotePlayerPresentation.IsRemoteSession(session, DFMPSpawnAssignmentController.LocalConnectionId))
                    continue;

                int sessionId = session.GetInstanceID();
                liveProxyIds.Add(sessionId);
                GameObject proxy = GetOrCreateProxy(sessionId, session.ConnectionId);
                UpdateLabel(proxy, session.DisplayName);
                UpdateAvatar(proxy, session);
                DFPosition remoteMapPixel = DaggerfallConnect.Arena2.MapsFile.WorldCoordToMapPixel(session.WorldX, session.WorldZ);
                bool isInLocalMapPixel = remoteMapPixel.X == streamingWorld.LocalPlayerGPS.CurrentMapPixel.X && remoteMapPixel.Y == streamingWorld.LocalPlayerGPS.CurrentMapPixel.Y;
                Vector3 localPosition = streamingWorld.LocalPlayerGPS.transform.position;
                Vector3 targetPosition = DFMPRemotePlayerPresentation.WorldToScenePosition(streamingWorld.LocalPlayerGPS, localPosition, session.WorldX, session.WorldZ);
                targetPosition = DFMPRemotePlayerPresentation.GroundScenePosition(streamingWorld, targetPosition);
                targetPosition = DFMPRemotePlayerPresentation.ApplySceneHeightOffset(targetPosition, session.SceneHeightOffset);
                bool isVisible = isInLocalMapPixel && DFMPRemotePlayerPresentation.IsVisibleInLocalMapPixel(localPosition, targetPosition);

                if (isVisible)
                {
                    if (!proxy.activeSelf)
                        proxy.transform.position = targetPosition;
                    else
                        proxy.transform.position = DFMPRemotePlayerPresentation.InterpolatePosition(proxy.transform.position, targetPosition, Time.unscaledDeltaTime);

                    proxy.transform.rotation = Quaternion.Euler(0f, session.FacingYaw, 0f);
                    FaceLabelToCamera(proxy);
                }

                proxy.SetActive(isVisible);
            }

            RemoveStaleProxies(liveProxyIds);
        }

        GameObject GetOrCreateProxy(int sessionId, int connectionId)
        {
            GameObject proxy;
            if (proxies.TryGetValue(sessionId, out proxy))
                return proxy;

            proxy = new GameObject($"DFMP_RemotePlayer_{connectionId}");
            proxy.name = $"DFMP_RemotePlayer_{connectionId}";
            CreateDaggerfallAvatar(proxy.transform);
            CreateLabel(proxy.transform, connectionId);
            proxy.SetActive(false);
            Object.DontDestroyOnLoad(proxy);
            proxies.Add(sessionId, proxy);
            Debug.Log($"[DFMP Remote] Created player proxy: connectionId={connectionId}.");
            return proxy;
        }

        static void CreateDaggerfallAvatar(Transform parent)
        {
            GameObject avatarGo = new GameObject("DaggerfallAvatar");
            avatarGo.transform.SetParent(parent, false);
            avatarGo.AddComponent<DFMPRemoteAvatarAppearance>();

            GameObject mobileGo = new GameObject("Mobile");
            mobileGo.transform.SetParent(avatarGo.transform, false);
            mobileGo.AddComponent<DaggerfallMobileUnit>();
        }

        static void CreateLabel(Transform proxyTransform, int connectionId)
        {
            GameObject labelAnchorGo = new GameObject("Label");
            labelAnchorGo.transform.SetParent(proxyTransform, false);
            labelAnchorGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);

            GameObject labelTextGo = new GameObject("Text");
            labelTextGo.transform.SetParent(labelAnchorGo.transform, false);
            var label = labelTextGo.AddComponent<TextMesh>();
            label.text = $"Player {connectionId}";
            label.characterSize = 0.05f;
            label.fontSize = 32;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
        }

        static void UpdateLabel(GameObject proxy, string displayName)
        {
            TextMesh label = proxy.GetComponentInChildren<TextMesh>();
            if (label != null && label.text != displayName)
                label.text = displayName;
        }

        static void UpdateAvatar(GameObject proxy, DFMPPlayerSessionState session)
        {
            DFMPRemoteAvatarAppearance avatar = proxy.GetComponentInChildren<DFMPRemoteAvatarAppearance>();
            if (avatar != null)
                avatar.ApplyIfChanged(session);
        }

        static void FaceLabelToCamera(GameObject proxy)
        {
            Camera mainCamera = Camera.main;
            TextMesh label = proxy.GetComponentInChildren<TextMesh>();
            if (mainCamera == null || label == null)
                return;

            Transform anchor = label.transform.parent;
            anchor.rotation = DFMPRemotePlayerPresentation.GetLabelBillboardRotation(anchor.position, mainCamera.transform.position);
            label.transform.localPosition = Vector3.zero;
            Renderer labelRenderer = label.GetComponent<Renderer>();
            if (labelRenderer != null)
            {
                Vector3 correction = anchor.position - labelRenderer.bounds.center;
                correction.y = 0f;
                label.transform.position += correction;
            }
        }

        void RemoveStaleProxies(HashSet<int> liveProxyIds)
        {
            var staleIds = new List<int>();
            foreach (int sessionId in proxies.Keys)
            {
                if (!liveProxyIds.Contains(sessionId))
                    staleIds.Add(sessionId);
            }

            foreach (int sessionId in staleIds)
            {
                Destroy(proxies[sessionId]);
                proxies.Remove(sessionId);
            }
        }
    }
}