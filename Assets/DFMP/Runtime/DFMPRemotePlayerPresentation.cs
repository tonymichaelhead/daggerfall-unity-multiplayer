using System.Collections.Generic;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
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

        public static bool IsAppearanceChanged(int cachedRace, int cachedGender, int cachedOutfitVariant, int cachedFaceVariant, DFMPPlayerSessionState session)
        {
            return cachedRace != session.Race || cachedGender != session.Gender || cachedOutfitVariant != session.OutfitVariant || cachedFaceVariant != session.FaceVariant;
        }

        public static bool GetAvatarIsIdle(bool isMoving)
        {
            return !isMoving;
        }
    }

    public class DFMPRemoteAvatarAppearance : MonoBehaviour
    {
        int race = int.MinValue;
        int gender = int.MinValue;
        int outfitVariant = int.MinValue;
        int faceVariant = int.MinValue;

        public void ApplyIfChanged(DFMPPlayerSessionState session)
        {
            if (!DFMPRemotePlayerPresentation.IsAppearanceChanged(race, gender, outfitVariant, faceVariant, session))
                return;

            var billboard = GetComponent<MobilePersonBillboard>();
            Races displayRace = (Races)DFMPPositionProtocol.GetDisplayRace(session.Race);
            Genders displayGender = (Genders)DFMPPositionProtocol.GetDisplayGender(session.Gender);
            billboard.SetPerson(displayRace, displayGender, session.OutfitVariant, false, session.FaceVariant, 0);
            billboard.IsIdle = DFMPRemotePlayerPresentation.GetAvatarIsIdle(session.IsMoving);
            transform.localPosition = new Vector3(0f, billboard.GetSize().y * 0.5f, 0f);

            race = session.Race;
            gender = session.Gender;
            outfitVariant = session.OutfitVariant;
            faceVariant = session.FaceVariant;
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
            var billboard = avatarGo.AddComponent<MobilePersonBillboard>();
            billboard.SetPerson(Races.Breton, Genders.Male, 0, false, 0, 192);
            avatarGo.transform.localPosition = new Vector3(0f, billboard.GetSize().y * 0.5f, 0f);
            avatarGo.AddComponent<DFMPRemoteAvatarAppearance>();
        }

        static void CreateLabel(Transform proxyTransform, int connectionId)
        {
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(proxyTransform, false);
            MobilePersonBillboard billboard = proxyTransform.GetComponentInChildren<MobilePersonBillboard>();
            float avatarHeight = billboard != null ? billboard.GetSize().y : 2f;
            labelGo.transform.localPosition = new Vector3(0f, avatarHeight + 0.2f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
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
            if (label != null)
                label.text = displayName;
        }

        static void UpdateAvatar(GameObject proxy, DFMPPlayerSessionState session)
        {
            DFMPRemoteAvatarAppearance avatar = proxy.GetComponentInChildren<DFMPRemoteAvatarAppearance>();
            if (avatar != null)
                avatar.ApplyIfChanged(session);

            MobilePersonBillboard billboard = proxy.GetComponentInChildren<MobilePersonBillboard>();
            if (billboard != null)
            {
                bool isIdle = DFMPRemotePlayerPresentation.GetAvatarIsIdle(session.IsMoving);
                if (billboard.IsIdle != isIdle)
                    billboard.IsIdle = isIdle;
            }
        }

            static void FaceLabelToCamera(GameObject proxy)
            {
                Camera mainCamera = Camera.main;
                TextMesh label = proxy.GetComponentInChildren<TextMesh>();
                if (mainCamera != null && label != null)
                label.transform.rotation = DFMPRemotePlayerPresentation.GetLabelBillboardRotation(label.transform.position, mainCamera.transform.position);
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