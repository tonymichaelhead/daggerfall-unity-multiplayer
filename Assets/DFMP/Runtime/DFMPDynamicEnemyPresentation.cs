using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPDynamicEnemyPresentation
    {
        public const float QuestOwnerCueDistance = 12f;
        public const float QuestOwnerCueMinimumViewDot = 0.96f;

        public static bool ShouldShowQuestOwnerCue(
            bool isQuestEnemy,
            string ownerDisplayName,
            Vector3 cameraPosition,
            Vector3 cameraForward,
            Vector3 enemyPosition)
        {
            if (!isQuestEnemy || string.IsNullOrWhiteSpace(ownerDisplayName))
                return false;

            Vector3 offset = enemyPosition - cameraPosition;
            if (offset.sqrMagnitude > QuestOwnerCueDistance * QuestOwnerCueDistance ||
                offset.sqrMagnitude < 0.0001f)
                return false;

            return Vector3.Dot(cameraForward.normalized, offset.normalized) >= QuestOwnerCueMinimumViewDot;
        }
        public const float MaximumVisibleDistance = 150f;
        public const float PositionSmoothingSpeed = 12f;

        public static Vector3 DungeonLocalToScenePosition(Vector3 dungeonRootPosition, Vector3 dungeonLocalPosition)
        {
            return dungeonRootPosition + dungeonLocalPosition;
        }

        public static Vector3 InterpolatePosition(Vector3 currentPosition, Vector3 targetPosition, float deltaTime)
        {
            float interpolationFactor = 1f - Mathf.Exp(-PositionSmoothingSpeed * Mathf.Max(0f, deltaTime));
            return Vector3.Lerp(currentPosition, targetPosition, interpolationFactor);
        }

        public static Vector3 ResolveCorpseGroundPosition(Transform dungeonTransform, Vector3 scenePosition)
        {
            if (dungeonTransform == null)
                return scenePosition;

            RaycastHit[] hits = Physics.RaycastAll(
                scenePosition + Vector3.up * 8f,
                Vector3.down,
                16f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float highestGroundY = float.NegativeInfinity;
            Vector3 groundPosition = scenePosition;
            bool foundGround = false;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(dungeonTransform))
                    continue;

                if (hits[index].point.y <= scenePosition.y + 0.05f && hits[index].point.y > highestGroundY)
                {
                    highestGroundY = hits[index].point.y;
                    groundPosition.y = hits[index].point.y;
                    foundGround = true;
                }
            }

            return foundGround ? groundPosition : scenePosition;
        }

        public static Vector3 GetAvatarLocalPosition(float spriteHalfHeight = 0f)
        {
            return new Vector3(0f, Mathf.Max(0f, spriteHalfHeight), 0f);
        }

        public static bool IsVisibleInLocalDungeon(Vector3 playerScenePosition, Vector3 enemyScenePosition, float maxDistance = MaximumVisibleDistance)
        {
            return Vector3.SqrMagnitude(enemyScenePosition - playerScenePosition) <= maxDistance * maxDistance;
        }

        public static bool IsVisualEligible(
            DFMPDynamicEnemyState state,
            bool isPlayerInsideDungeon,
            DFMPWorldContextKey playerContext)
        {
            if (state == null || (!isPlayerInsideDungeon && !state.IsQuestEnemy))
                return false;

            if (state.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                return false;

            var carrier = state.GetComponent<DFMPWorldContextCarrier>();
            if (carrier == null)
                return true;

            return carrier.Context.Equals(playerContext);
        }

        public static bool IsCorpseEligible(
            DFMPDynamicEnemyState state,
            bool isPlayerInsideDungeon,
            DFMPWorldContextKey playerContext)
        {
            if (state == null || (!isPlayerInsideDungeon && !state.IsQuestEnemy))
                return false;

            if (state.LifecycleState != DFMPDynamicEnemyLifecycleState.Dead)
                return false;

            var carrier = state.GetComponent<DFMPWorldContextCarrier>();
            if (carrier == null)
                return true;

            return carrier.Context.Equals(playerContext);
        }

        public static bool TryCreateCorpseLootContainer(
            int mobileType,
            Vector3 scenePosition,
            Transform parent,
            out DaggerfallLoot loot)
        {
            loot = null;
            if (DaggerfallUnity.Instance == null || DaggerfallUnity.Instance.Option_LootContainerPrefab == null)
                return false;

            MobileEnemy enemy;
            if (!EnemyBasics.GetEnemy((MobileTypes)mobileType, out enemy))
                return false;

            int archive, record;
            EnemyBasics.ReverseCorpseTexture(enemy.CorpseTexture, out archive, out record);
            if (archive <= 0)
                return false;

            loot = GameObjectHelper.CreateLootContainer(
                LootContainerTypes.CorpseMarker,
                InventoryContainerImages.Corpse2,
                scenePosition,
                parent,
                archive,
                record);

            if (loot != null)
            {
                if (!string.IsNullOrWhiteSpace(enemy.LootTableKey))
                    DaggerfallLoot.GenerateItems(enemy.LootTableKey, loot.Items);

                Debug.Log($"[DFMP Enemy] Created personal corpse loot: mobileType={(MobileTypes)mobileType}, lootTable={enemy.LootTableKey ?? string.Empty}, itemCount={loot.Items.Count}.");

                loot.entityName = TextManager.Instance != null
                    ? TextManager.Instance.GetLocalizedEnemyName(enemy.ID)
                    : enemy.ID.ToString();
                return true;
            }

            return false;
        }
    }

    public class DFMPDynamicEnemyHitTarget : MonoBehaviour
    {
        public string EnemyId { get; private set; }
        public bool IsDamageable { get; private set; }

        public void Initialize(string enemyId)
        {
            EnemyId = enemyId ?? string.Empty;
            IsDamageable = false;
        }

        public void SetDamageable(bool isDamageable)
        {
            IsDamageable = isDamageable;
        }
    }

    public class DFMPDynamicEnemyAppearance : MonoBehaviour
    {
        int mobileType = int.MinValue;
        bool? isMoving;
        int attackSequence = -1;

        public void ApplyIfChanged(int targetMobileType, int targetGender, int targetReaction, bool targetIsMoving, int targetAttackSequence, DFMPDynamicEnemyAttackKind targetAttackKind)
        {
            var mobile = GetComponentInChildren<DaggerfallMobileUnit>();
            if (mobile == null)
                return;

            if (mobileType != targetMobileType)
            {
                MobileEnemy enemy;
                if (EnemyBasics.GetEnemy((MobileTypes)targetMobileType, out enemy))
                {
                    byte nativeGender = (byte)Mathf.Clamp(targetGender, 0, 2);
                    MobileReactions nativeReaction = targetReaction == (int)DFBlock.EnemyReactionTypes.Passive
                        ? MobileReactions.Passive
                        : MobileReactions.Hostile;
                    mobile.SetEnemy(DaggerfallUnity.Instance, enemy, nativeReaction, nativeGender);
                    mobile.ChangeEnemyState(MobileStates.Idle);
                    transform.localPosition = DFMPDynamicEnemyPresentation.GetAvatarLocalPosition(mobile.GetSize().y * 0.5f);
                }

                mobileType = targetMobileType;
            }

            if (!isMoving.HasValue || isMoving.Value != targetIsMoving)
            {
                MobileStates movementState = targetIsMoving ? MobileStates.Move : MobileStates.Idle;
                mobile.ChangeEnemyState(movementState);
                isMoving = targetIsMoving;
            }

            if (attackSequence != targetAttackSequence)
            {
                MobileStates attackState = targetAttackKind == DFMPDynamicEnemyAttackKind.Magic
                    ? MobileStates.Spell
                    : targetAttackKind == DFMPDynamicEnemyAttackKind.Ranged
                        ? MobileStates.RangedAttack1
                        : MobileStates.PrimaryAttack;
                mobile.ChangeEnemyState(attackState);
                attackSequence = targetAttackSequence;
            }
        }
    }

    public class DFMPDynamicEnemyPresentationController : MonoBehaviour
    {
        static DFMPDynamicEnemyPresentationController instance;
        readonly Dictionary<int, GameObject> proxies = new Dictionary<int, GameObject>();
        readonly Dictionary<int, bool> proxyVisibilityByStateId = new Dictionary<int, bool>();
        readonly Dictionary<int, Vector3> lastGroundedMeleePositionByStateId = new Dictionary<int, Vector3>();
        readonly Dictionary<int, GameObject> corpses = new Dictionary<int, GameObject>();
        readonly Dictionary<int, bool> corpseVisibilityByStateId = new Dictionary<int, bool>();
        readonly HashSet<int> questLootAppliedStateIds = new HashSet<int>();

        public static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject controllerGo = new GameObject("DFMP_DynamicEnemyPresentationController");
            Object.DontDestroyOnLoad(controllerGo);
            instance = controllerGo.AddComponent<DFMPDynamicEnemyPresentationController>();
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

            foreach (GameObject corpse in corpses.Values)
            {
                if (corpse != null)
                    Destroy(corpse);
            }

            proxies.Clear();
            corpses.Clear();
            proxyVisibilityByStateId.Clear();
            lastGroundedMeleePositionByStateId.Clear();
            corpseVisibilityByStateId.Clear();
            questLootAppliedStateIds.Clear();
            if (instance == this)
                instance = null;
        }

        void Update()
        {
            if (!NetworkClient.isConnected)
            {
                HideAllPresentations();
                return;
            }

            // DaggerfallUnity is a per-scene singleton, so this must be re-asserted rather than set once at connect.
            if (DaggerfallUnity.Instance != null && DaggerfallUnity.Instance.Option_ImportEnemyPrefabs)
            {
                DaggerfallUnity.Instance.Option_ImportEnemyPrefabs = false;
                Debug.Log("[DFMP Enemy] Suppressed client-local native enemy import for multiplayer session.");
            }

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null || !streamingWorld.IsReady || streamingWorld.LocalPlayerGPS == null)
            {
                HideAllPresentations();
                return;
            }

            if (!GameManager.HasInstance || GameObject.FindGameObjectWithTag("Player") == null)
            {
                HideAllPresentations();
                return;
            }

            if (GameManager.Instance.PlayerEntity != null)
                GameManager.Instance.PlayerEntity.PreventEnemySpawns = true;

            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
            bool isInsideDungeon = playerEnterExit != null && playerEnterExit.IsPlayerInsideDungeon && playerEnterExit.Dungeon != null;
            DFMPWorldContextKey playerContext = new DFMPWorldContextKey();
            DFMPWorldContextReport contextReport;
            if (DFMPPositionReporter.TryBuildWorldContextReport(streamingWorld, out contextReport))
                DFMPWorldContextProtocol.TryCreateKey(contextReport, out playerContext);

            var liveStateIds = new HashSet<int>();
            DFMPDynamicEnemyState[] states = FindObjectsOfType<DFMPDynamicEnemyState>();
            foreach (DFMPDynamicEnemyState state in states)
            {
                if (state == null)
                    continue;

                int stateId = state.GetInstanceID();
                liveStateIds.Add(stateId);

                bool isEligible = DFMPDynamicEnemyPresentation.IsVisualEligible(state, isInsideDungeon, playerContext);
                if (isEligible)
                {
                    GameObject proxy = GetOrCreateProxy(stateId, state.EnemyId);
                    var appearance = proxy.GetComponentInChildren<DFMPDynamicEnemyAppearance>();
                    if (appearance != null)
                        appearance.ApplyIfChanged(state.MobileType, state.Gender, state.Reaction, state.IsMoving, state.AttackSequence, state.AttackKind);

                    Vector3 targetPosition = ResolveScenePosition(
                        state,
                        playerContext,
                        streamingWorld,
                        playerEnterExit);

                    if (!proxy.activeSelf)
                        proxy.transform.position = targetPosition;
                    else
                        proxy.transform.position = DFMPDynamicEnemyPresentation.InterpolatePosition(proxy.transform.position, targetPosition, Time.unscaledDeltaTime);

                    proxy.transform.rotation = Quaternion.Euler(0f, state.FacingYaw, 0f);
                    UpdateQuestOwnerCue(proxy, state);

                    if (state.MobileType != (int)MobileTypes.GiantBat && state.MobileType != (int)MobileTypes.Harpy)
                        LogGroundedMeleePositionChange(stateId, state, proxy, targetPosition);

                    bool isVisible = DFMPDynamicEnemyPresentation.IsVisibleInLocalDungeon(
                        playerEnterExit.transform.position,
                        targetPosition);

                    proxy.SetActive(isVisible);
                    bool wasVisible;
                    if (!proxyVisibilityByStateId.TryGetValue(stateId, out wasVisible) || wasVisible != isVisible)
                    {
                        proxyVisibilityByStateId[stateId] = isVisible;
                        Debug.Log($"[DFMP Enemy] Proxy visibility changed: stateId={stateId}, enemyId={state.EnemyId}, mobileType={state.MobileType}, visible={isVisible}, eligible={isEligible}, targetPosition={targetPosition}, dungeonLocalPosition={state.DungeonLocalPosition}, localPlayer={(playerEnterExit != null ? playerEnterExit.transform.position : Vector3.zero)}.");
                    }
                    var hitTarget = proxy.GetComponent<DFMPDynamicEnemyHitTarget>();
                    if (hitTarget != null)
                        hitTarget.SetDamageable(isVisible);
                }
                else
                {
                    GameObject proxy;
                    if (proxies.TryGetValue(stateId, out proxy) && proxy != null)
                    {
                        proxy.SetActive(false);
                        bool wasVisible;
                        if (proxyVisibilityByStateId.TryGetValue(stateId, out wasVisible) && wasVisible)
                        {
                            proxyVisibilityByStateId[stateId] = false;
                            Debug.Log($"[DFMP Enemy] Proxy hidden: stateId={stateId}, enemyId={state.EnemyId}, mobileType={state.MobileType}, eligible={isEligible}, playerInsideDungeon={isInsideDungeon}, playerContext={playerContext}, dungeonLocalPosition={state.DungeonLocalPosition}.");
                        }
                        var hitTarget = proxy.GetComponent<DFMPDynamicEnemyHitTarget>();
                        if (hitTarget != null)
                            hitTarget.SetDamageable(false);
                    }
                }

                bool isCorpseEligible = DFMPDynamicEnemyPresentation.IsCorpseEligible(state, isInsideDungeon, playerContext);
                if (isCorpseEligible)
                {
                    Transform contextRoot = GetContextRoot(playerContext, streamingWorld, playerEnterExit);
                    Vector3 targetPosition = GetCorpseScenePosition(
                        stateId,
                        state,
                        playerContext,
                        streamingWorld,
                        playerEnterExit);
                    targetPosition = DFMPDynamicEnemyPresentation.ResolveCorpseGroundPosition(contextRoot, targetPosition);
                    GameObject corpse = GetOrCreateCorpse(stateId, state, contextRoot, targetPosition);
                    if (corpse != null)
                    {
                        bool isVisible = DFMPDynamicEnemyPresentation.IsVisibleInLocalDungeon(
                            playerEnterExit.transform.position,
                            targetPosition);

                        corpse.SetActive(isVisible);
                        bool wasVisible;
                        if (!corpseVisibilityByStateId.TryGetValue(stateId, out wasVisible) || wasVisible != isVisible)
                        {
                            corpseVisibilityByStateId[stateId] = isVisible;
                            Debug.Log($"[DFMP Enemy] Corpse presentation {(isVisible ? "visible" : "hidden")}: enemyId={state.EnemyId}, context={playerContext}.");
                        }
                    }
                }
                else
                {
                    GameObject corpse;
                    if (corpses.TryGetValue(stateId, out corpse) && corpse != null)
                    {
                        corpse.SetActive(false);
                        bool wasVisible;
                        if (corpseVisibilityByStateId.TryGetValue(stateId, out wasVisible) && wasVisible)
                        {
                            corpseVisibilityByStateId[stateId] = false;
                            Debug.Log($"[DFMP Enemy] Corpse presentation hidden: enemyId={state.EnemyId}, lifecycle={state.LifecycleState}, eligible={isCorpseEligible}.");
                        }
                    }
                }
            }

            RemoveStaleProxies(liveStateIds);
        }

        GameObject GetOrCreateProxy(int stateId, string enemyId)
        {
            GameObject proxy;
            if (proxies.TryGetValue(stateId, out proxy) && proxy != null)
                return proxy;

            proxy = new GameObject($"DFMP_DynamicEnemyProxy_{enemyId}");
            proxy.AddComponent<DFMPDynamicEnemyHitTarget>().Initialize(enemyId);
            var collider = proxy.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.9f, 0f);
            collider.radius = 0.4f;
            collider.height = 1.8f;
            CreateAvatarHierarchy(proxy.transform);
            proxy.SetActive(false);
            Object.DontDestroyOnLoad(proxy);
            proxies[stateId] = proxy;
            Debug.Log($"[DFMP Enemy] Created client enemy visual proxy: stateId={stateId}, enemyId={enemyId}.");
            return proxy;
        }

        static Transform GetContextRoot(
            DFMPWorldContextKey context,
            StreamingWorld streamingWorld,
            PlayerEnterExit playerEnterExit)
        {
            if (context.Kind == DFMPWorldContextKind.Dungeon && playerEnterExit != null && playerEnterExit.Dungeon != null)
                return playerEnterExit.Dungeon.transform;
            if (context.Kind == DFMPWorldContextKind.BuildingInterior && playerEnterExit != null && playerEnterExit.Interior != null)
                return playerEnterExit.Interior.transform;
            return streamingWorld != null && streamingWorld.CurrentPlayerLocationObject != null
                ? streamingWorld.CurrentPlayerLocationObject.transform
                : null;
        }

        static Vector3 ResolveScenePosition(
            DFMPDynamicEnemyState state,
            DFMPWorldContextKey context,
            StreamingWorld streamingWorld,
            PlayerEnterExit playerEnterExit)
        {
            Transform root = GetContextRoot(context, streamingWorld, playerEnterExit);
            if (context.Kind != DFMPWorldContextKind.Exterior)
                return root != null
                    ? DFMPDynamicEnemyPresentation.DungeonLocalToScenePosition(root.position, state.DungeonLocalPosition)
                    : state.DungeonLocalPosition;

            Vector3 localPlayerPosition = streamingWorld.LocalPlayerGPS.transform.position;
            Vector3 scenePosition = DFMPRemotePlayerPresentation.WorldToScenePosition(
                streamingWorld.LocalPlayerGPS,
                localPlayerPosition,
                Mathf.RoundToInt(state.DungeonLocalPosition.x),
                Mathf.RoundToInt(state.DungeonLocalPosition.z));
            return DFMPRemotePlayerPresentation.GroundScenePosition(streamingWorld, scenePosition);
        }

        void UpdateQuestOwnerCue(GameObject proxy, DFMPDynamicEnemyState state)
        {
            Transform cueTransform = proxy.transform.Find("QuestOwnerCue");
            if (cueTransform == null)
            {
                GameObject cueObject = new GameObject("QuestOwnerCue");
                cueObject.transform.SetParent(proxy.transform, false);
                cueObject.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                TextMesh text = cueObject.AddComponent<TextMesh>();
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.fontSize = 32;
                text.characterSize = 0.03f;
                text.color = new Color(1f, 0.85f, 0.35f, 1f);
                cueTransform = cueObject.transform;
            }

            Camera camera = GameManager.Instance != null ? GameManager.Instance.MainCamera : null;
            bool visible = camera != null &&
                DFMPWorldSettings.CurrentShowQuestEnemyOwnerCue &&
                DFMPDynamicEnemyPresentation.ShouldShowQuestOwnerCue(
                    state.IsQuestEnemy,
                    state.QuestOwnerDisplayName,
                    camera.transform.position,
                    camera.transform.forward,
                    proxy.transform.position);
            cueTransform.gameObject.SetActive(visible);
            if (!visible)
                return;

            TextMesh cueText = cueTransform.GetComponent<TextMesh>();
            cueText.text = state.QuestOwnerDisplayName + "'s quest";
            cueTransform.rotation = DFMPRemotePlayerPresentation.GetLabelBillboardRotation(
                cueTransform.position,
                camera.transform.position);
        }

        GameObject GetOrCreateCorpse(
            int stateId,
            DFMPDynamicEnemyState state,
            Transform contextRoot,
            Vector3 targetPosition)
        {
            GameObject corpse;
            if (corpses.TryGetValue(stateId, out corpse) && corpse != null)
                return corpse;

            DaggerfallLoot loot;
            if (DFMPDynamicEnemyPresentation.TryCreateCorpseLootContainer(state.MobileType, targetPosition, contextRoot, out loot) && loot != null)
            {
                corpse = loot.gameObject;
            }
            else
            {
                corpse = new GameObject($"DFMP_DynamicEnemyCorpse_{state.EnemyId}");
                corpse.transform.position = targetPosition;
                corpse.transform.SetParent(contextRoot, true);
            }

            Object.DontDestroyOnLoad(corpse);
            corpses[stateId] = corpse;
            AddOwnerQuestLoot(stateId, state, corpse);
            Debug.Log($"[DFMP Enemy] Created client corpse loot proxy: stateId={stateId}, enemyId={state.EnemyId}, mobileType={state.MobileType}, dungeonLocalPosition={state.DungeonLocalPosition}.");
            return corpse;
        }

        void AddOwnerQuestLoot(int stateId, DFMPDynamicEnemyState state, GameObject corpse)
        {
            if (state == null ||
                corpse == null ||
                !state.IsQuestEnemy ||
                state.QuestOwnerConnectionId != DFMPSpawnAssignmentController.LocalConnectionId ||
                string.IsNullOrWhiteSpace(state.QuestLootJson) ||
                !questLootAppliedStateIds.Add(stateId))
                return;

            DaggerfallLoot loot = corpse.GetComponent<DaggerfallLoot>();
            if (loot == null)
                return;

            DFMPCharacterItemRecord[] items;
            DFMPCharacterEquipmentRecord[] equipment;
            string reason;
            if (!DFMPInventorySnapshotCodec.TryDecode(
                state.QuestLootJson,
                out items,
                out equipment,
                out reason))
            {
                Debug.LogWarning($"[DFMP Quest] Rejected owner quest loot: enemyId={state.EnemyId}, reason={reason}.");
                return;
            }

            var questItems = new ItemCollection();
            questItems.DeserializeItems(DFMPCharacterPersistence.RestoreItems(items));
            for (int index = 0; index < questItems.Count; index++)
            {
                DaggerfallUnityItem item = questItems.GetItem(index);
                if (item == null || !item.IsQuestItem)
                    continue;
                loot.Items.AddItem(item);
            }
        }

        Vector3 GetCorpseScenePosition(
            int stateId,
            DFMPDynamicEnemyState state,
            DFMPWorldContextKey context,
            StreamingWorld streamingWorld,
            PlayerEnterExit playerEnterExit)
        {
            GameObject proxy;
            if (proxies.TryGetValue(stateId, out proxy) && proxy != null)
                return proxy.transform.position;

            return ResolveScenePosition(state, context, streamingWorld, playerEnterExit);
        }

        void HideAllPresentations()
        {
            foreach (GameObject proxy in proxies.Values)
            {
                if (proxy != null)
                    proxy.SetActive(false);
            }

            foreach (GameObject corpse in corpses.Values)
            {
                if (corpse != null)
                    corpse.SetActive(false);
            }
        }

        static void CreateAvatarHierarchy(Transform parent)
        {
            GameObject avatarGo = new GameObject("DaggerfallEnemyAvatar");
            avatarGo.transform.SetParent(parent, false);
            avatarGo.AddComponent<DFMPDynamicEnemyAppearance>();

            GameObject mobileGo = new GameObject("Mobile");
            mobileGo.transform.SetParent(avatarGo.transform, false);
            mobileGo.AddComponent<DaggerfallMobileUnit>();
        }

        void RemoveStaleProxies(HashSet<int> liveStateIds)
        {
            var staleKeys = new List<int>();
            foreach (var kvp in proxies)
            {
                if (!liveStateIds.Contains(kvp.Key))
                    staleKeys.Add(kvp.Key);
            }

            for (int index = 0; index < staleKeys.Count; index++)
            {
                int staleKey = staleKeys[index];
                GameObject proxy = proxies[staleKey];
                if (proxy != null)
                    Destroy(proxy);

                proxies.Remove(staleKey);
                proxyVisibilityByStateId.Remove(staleKey);
                lastGroundedMeleePositionByStateId.Remove(staleKey);
                Debug.Log($"[DFMP Enemy] Client removed state: staleStateId={staleKey}.");
            }

            staleKeys.Clear();
            foreach (var kvp in corpses)
            {
                if (!liveStateIds.Contains(kvp.Key))
                    staleKeys.Add(kvp.Key);
            }

            for (int index = 0; index < staleKeys.Count; index++)
            {
                int staleKey = staleKeys[index];
                GameObject corpse = corpses[staleKey];
                if (corpse != null)
                    Destroy(corpse);

                corpses.Remove(staleKey);
                corpseVisibilityByStateId.Remove(staleKey);
                questLootAppliedStateIds.Remove(staleKey);
                Debug.Log($"[DFMP Enemy] Client removed corpse: staleStateId={staleKey}.");
            }
        }

        void LogGroundedMeleePositionChange(int stateId, DFMPDynamicEnemyState state, GameObject proxy, Vector3 targetPosition)
        {
            Vector3 previousPosition;
            if (lastGroundedMeleePositionByStateId.TryGetValue(stateId, out previousPosition) &&
                Vector3.SqrMagnitude(targetPosition - previousPosition) < 0.0625f)
                return;

            lastGroundedMeleePositionByStateId[stateId] = targetPosition;
            DaggerfallMobileUnit mobile = proxy.GetComponentInChildren<DaggerfallMobileUnit>();
            Renderer renderer = proxy.GetComponentInChildren<Renderer>();
            Debug.Log($"[DFMP Enemy] Grounded melee presentation position: enemyId={state.EnemyId}, mobileType={(MobileTypes)state.MobileType}, position={targetPosition}, previousPosition={previousPosition}, state={(mobile != null ? mobile.EnemyState.ToString() : "<null>")}, proxyActive={proxy.activeSelf}, mobileInHierarchy={(mobile != null && mobile.gameObject.activeInHierarchy)}, rendererEnabled={(renderer != null && renderer.enabled)}, rendererBounds={(renderer != null ? renderer.bounds.ToString() : "<none>")}.");
        }
    }
}
