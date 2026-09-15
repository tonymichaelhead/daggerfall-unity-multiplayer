using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPDynamicEnemyPresentation
    {
        public const float MaximumVisibleDistance = 150f;

        public static Vector3 DungeonLocalToScenePosition(Vector3 dungeonRootPosition, Vector3 dungeonLocalPosition)
        {
            return dungeonRootPosition + dungeonLocalPosition;
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
            float nearestDistance = float.MaxValue;
            Vector3 groundPosition = scenePosition;
            bool foundGround = false;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(dungeonTransform))
                    continue;

                if (hits[index].distance < nearestDistance)
                {
                    nearestDistance = hits[index].distance;
                    groundPosition.y = hits[index].point.y;
                    foundGround = true;
                }
            }

            return foundGround ? groundPosition : scenePosition;
        }

        public static Vector3 GetAvatarLocalPosition()
        {
            return Vector3.zero;
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
            if (state == null || !isPlayerInsideDungeon)
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
            if (state == null || !isPlayerInsideDungeon)
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
                    transform.localPosition = DFMPDynamicEnemyPresentation.GetAvatarLocalPosition();
                }

                mobileType = targetMobileType;
            }

            if (!isMoving.HasValue || isMoving.Value != targetIsMoving)
            {
                mobile.ChangeEnemyState(targetIsMoving ? MobileStates.Move : MobileStates.Idle);
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
        readonly Dictionary<int, GameObject> corpses = new Dictionary<int, GameObject>();

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
            if (instance == this)
                instance = null;
        }

        void Update()
        {
            if (!NetworkClient.isConnected)
                return;

            // DaggerfallUnity is a per-scene singleton, so this must be re-asserted rather than set once at connect.
            if (DaggerfallUnity.Instance != null && DaggerfallUnity.Instance.Option_ImportEnemyPrefabs)
            {
                DaggerfallUnity.Instance.Option_ImportEnemyPrefabs = false;
                Debug.Log("[DFMP Enemy] Suppressed client-local native enemy import for multiplayer session.");
            }

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null || !streamingWorld.IsReady || streamingWorld.LocalPlayerGPS == null)
                return;

            if (!GameManager.HasInstance || GameObject.FindGameObjectWithTag("Player") == null)
                return;

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

                    Vector3 targetPosition = DFMPDynamicEnemyPresentation.DungeonLocalToScenePosition(
                        playerEnterExit.Dungeon.transform.position,
                        state.DungeonLocalPosition);

                    proxy.transform.position = targetPosition;
                    proxy.transform.rotation = Quaternion.Euler(0f, state.FacingYaw, 0f);

                    bool isVisible = DFMPDynamicEnemyPresentation.IsVisibleInLocalDungeon(
                        playerEnterExit.transform.position,
                        targetPosition);

                    proxy.SetActive(isVisible);
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
                        var hitTarget = proxy.GetComponent<DFMPDynamicEnemyHitTarget>();
                        if (hitTarget != null)
                            hitTarget.SetDamageable(false);
                    }
                }

                bool isCorpseEligible = DFMPDynamicEnemyPresentation.IsCorpseEligible(state, isInsideDungeon, playerContext);
                if (isCorpseEligible)
                {
                    GameObject corpse = GetOrCreateCorpse(stateId, state, playerEnterExit.Dungeon.transform);
                    if (corpse != null)
                    {
                        Vector3 targetPosition = DFMPDynamicEnemyPresentation.DungeonLocalToScenePosition(
                            playerEnterExit.Dungeon.transform.position,
                            state.DungeonLocalPosition);
                        targetPosition = DFMPDynamicEnemyPresentation.ResolveCorpseGroundPosition(playerEnterExit.Dungeon.transform, targetPosition);

                        bool isVisible = DFMPDynamicEnemyPresentation.IsVisibleInLocalDungeon(
                            playerEnterExit.transform.position,
                            targetPosition);

                        corpse.SetActive(isVisible);
                    }
                }
                else
                {
                    GameObject corpse;
                    if (corpses.TryGetValue(stateId, out corpse) && corpse != null)
                        corpse.SetActive(false);
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
            Debug.Log($"[DFMP Enemy] Created client enemy visual proxy: enemyId={enemyId}.");
            return proxy;
        }

        GameObject GetOrCreateCorpse(int stateId, DFMPDynamicEnemyState state, Transform dungeonTransform)
        {
            GameObject corpse;
            if (corpses.TryGetValue(stateId, out corpse) && corpse != null)
                return corpse;

            Vector3 targetPosition = DFMPDynamicEnemyPresentation.DungeonLocalToScenePosition(
                dungeonTransform.position,
                state.DungeonLocalPosition);
            targetPosition = DFMPDynamicEnemyPresentation.ResolveCorpseGroundPosition(dungeonTransform, targetPosition);

            DaggerfallLoot loot;
            if (DFMPDynamicEnemyPresentation.TryCreateCorpseLootContainer(state.MobileType, targetPosition, dungeonTransform, out loot) && loot != null)
            {
                corpse = loot.gameObject;
            }
            else
            {
                corpse = new GameObject($"DFMP_DynamicEnemyCorpse_{state.EnemyId}");
                corpse.transform.position = targetPosition;
                corpse.transform.SetParent(dungeonTransform, true);
            }

            Object.DontDestroyOnLoad(corpse);
            corpses[stateId] = corpse;
            Debug.Log($"[DFMP Enemy] Created client corpse loot proxy: enemyId={state.EnemyId}.");
            return corpse;
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
            }
        }
    }
}
