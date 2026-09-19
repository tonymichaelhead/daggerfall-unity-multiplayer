using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallConnect;
using Mirror;
using System.Text;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPPositionReporter : MonoBehaviour
    {
        const float HeartbeatReportInterval = 5f;
        const float ChangeReportInterval = 0.5f;

        static DFMPPositionReporter instance;
        float nextReportTime;
        float nextIdentityReportTime;
        float nextChangeReportTime;
        float nextQuestStateReportTime;
        int lastContextSignature;
        int lastInventorySignature;
        string lastQuestPayloadChecksum;
        static ulong nextDamageRequestId = 1;
        static uint nextDamageSequence = 1;
        WeaponStates lastWeaponState = WeaponStates.Idle;
        bool wasCastingSpell;

        public static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject reporterGo = new GameObject("DFMP_PositionReporter");
            Object.DontDestroyOnLoad(reporterGo);
            instance = reporterGo.AddComponent<DFMPPositionReporter>();
            DFMP.Hooks.DaggerfallHooks.TryHandlePlayerMissileHit = TryHandlePlayerMissileHit;
            DFMP.Hooks.DaggerfallHooks.TryHandlePlayerWeaponHit = TryHandlePlayerWeaponHit;
            DFMP.Hooks.DaggerfallHooks.OnActionDoorToggled = OnActionDoorToggled;
            DFMP.Hooks.DaggerfallHooks.OnGuildMembershipChanged = RequestQuestSave;
            DFMP.Hooks.DaggerfallHooks.TryHandleExitGame = TryHandleExitGame;
        }

        public static void Reset()
        {
            if (instance != null)
                Destroy(instance.gameObject);

            DFMP.Hooks.DaggerfallHooks.TryHandlePlayerMissileHit = null;
            DFMP.Hooks.DaggerfallHooks.TryHandlePlayerWeaponHit = null;
            DFMP.Hooks.DaggerfallHooks.OnActionDoorToggled = null;
            DFMP.Hooks.DaggerfallHooks.OnGuildMembershipChanged = null;
            DFMP.Hooks.DaggerfallHooks.TryHandleExitGame = null;
            nextDamageRequestId = 1;
            nextDamageSequence = 1;
            instance = null;
        }

        public static void RequestQuestSave()
        {
            if (instance == null)
                return;

            instance.nextQuestStateReportTime = 0f;
            if (NetworkClient.isConnected)
                instance.SendQuestStateReport(true);
        }

        /// <summary>
        /// Sends identity, pose/context, and quest envelope immediately, ignoring autosave timers.
        /// Must run while still connected.
        /// </summary>
        public static bool FlushPersistentState()
        {
            if (instance == null || !NetworkClient.isConnected)
                return false;

            instance.SendIdentityReport(true);
            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld != null && streamingWorld.IsReady && streamingWorld.LocalPlayerGPS != null)
            {
                instance.SendWorldContextReport(streamingWorld, true);
                instance.SendPositionReport(streamingWorld, true);
            }

            instance.SendQuestStateReport(true);
            Debug.Log("[DFMP Character] Flushed persistable character state before disconnect.");
            return true;
        }

        static bool TryHandleExitGame()
        {
            return DFMPClientBootstrap.TryHandleMultiplayerExit();
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void Update()
        {
            if (!NetworkClient.isConnected)
                return;

            bool hasImmediateWorldContextReport = DFMPSpawnAssignmentController.HasImmediateWorldContextReport;
            if (DFMPSpawnAssignmentController.HasPendingServerAssignment && !hasImmediateWorldContextReport)
                return;

            if (!IsLocalPlayerAvailable())
                return;

            if (Time.unscaledTime < nextReportTime)
                return;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            if (streamingWorld == null || !streamingWorld.IsReady || streamingWorld.LocalPlayerGPS == null)
                return;

            if (hasImmediateWorldContextReport)
            {
                if (SendWorldContextReport(streamingWorld, true))
                    DFMPSpawnAssignmentController.CompleteImmediateWorldContextReport();
                return;
            }

            ReportPlayerAction(streamingWorld);
            nextReportTime = Time.unscaledTime + DFMPPositionProtocol.MinimumReportInterval;
            SendIdentityReport(false);
            SendWorldContextReport(streamingWorld, false);
            SendPositionReport(streamingWorld, false);
        }

        void SendPositionReport(StreamingWorld streamingWorld, bool forceReliable)
        {
            Vector3 playerScenePosition = streamingWorld.LocalPlayerGPS.transform.position;
            Vector3 groundScenePosition = DFMPRemotePlayerPresentation.GroundScenePosition(streamingWorld, playerScenePosition);
            CharacterController playerController = GameManager.Instance != null ? GameManager.Instance.PlayerController : null;
            float controllerCenterY = playerController != null ? playerController.center.y : 0f;
            float controllerHeight = playerController != null ? playerController.height : 0f;
            float controllerSkinWidth = playerController != null ? playerController.skinWidth : 0f;
            PlayerEnterExit playerEnterExit = GameManager.Instance != null ? GameManager.Instance.PlayerEnterExit : null;
            bool isInsideDungeon = playerEnterExit != null && playerEnterExit.IsPlayerInsideDungeon && playerEnterExit.Dungeon != null;
            bool isInsideBuilding = playerEnterExit != null && playerEnterExit.IsPlayerInsideBuilding && playerEnterExit.Interior != null;
            Vector3 interiorLocalPosition = Vector3.zero;
            bool hasInteriorLocalPosition = false;
            if (isInsideDungeon)
            {
                hasInteriorLocalPosition = true;
                interiorLocalPosition = playerEnterExit.transform.position - playerEnterExit.Dungeon.transform.position;
                interiorLocalPosition.y = DFMPPositionProtocol.GetControllerFeetY(
                    playerScenePosition.y,
                    controllerCenterY,
                    controllerHeight,
                    controllerSkinWidth) - playerEnterExit.Dungeon.transform.position.y;
            }
            else if (isInsideBuilding)
            {
                hasInteriorLocalPosition = true;
                interiorLocalPosition = playerEnterExit.transform.position - playerEnterExit.Interior.transform.position;
                interiorLocalPosition.y = DFMPPositionProtocol.GetControllerFeetY(
                    playerScenePosition.y,
                    controllerCenterY,
                    controllerHeight,
                    controllerSkinWidth) - playerEnterExit.Interior.transform.position.y;
            }

            int channel = forceReliable ? Channels.Reliable : Channels.Unreliable;
            NetworkClient.Send(new DFMPPlayerPositionReport
            {
                WorldX = streamingWorld.LocalPlayerGPS.WorldX,
                WorldY = 0f,
                WorldZ = streamingWorld.LocalPlayerGPS.WorldZ,
                FacingYaw = streamingWorld.LocalPlayerGPS.transform.eulerAngles.y,
                SceneHeightOffset = DFMPPositionProtocol.GetSceneHeightOffset(
                    playerScenePosition.y,
                    groundScenePosition.y,
                    controllerCenterY,
                        controllerHeight,
                        controllerSkinWidth),
                HasDungeonLocalPosition = hasInteriorLocalPosition,
                DungeonLocalX = interiorLocalPosition.x,
                DungeonLocalY = interiorLocalPosition.y,
                DungeonLocalZ = interiorLocalPosition.z
            }, channel);
        }

        static bool IsLocalPlayerAvailable()
        {
            // GameManager's cached getters throw instead of returning null while the startup scene is active.
            return GameManager.HasInstance && GameObject.FindGameObjectWithTag("Player") != null;
        }

        void ReportPlayerAction(StreamingWorld streamingWorld)
        {
            if (GameManager.Instance == null || GameManager.Instance.WeaponManager == null)
                return;

            FPSWeapon screenWeapon = GameManager.Instance.WeaponManager.ScreenWeapon;
            if (screenWeapon != null)
            {
                WeaponStates weaponState = screenWeapon.WeaponState;
                if (lastWeaponState == WeaponStates.Idle && weaponState != WeaponStates.Idle)
                {
                    DFMPPlayerActionKind action = screenWeapon.WeaponType == WeaponTypes.Bow
                        ? DFMPPlayerActionKind.RangedAttack
                        : DFMPPlayerActionKind.PrimaryAttack;
                    NetworkClient.Send(new DFMPPlayerActionReport { Kind = action });
                }

                lastWeaponState = weaponState;
            }

            bool isCastingSpell = GameManager.Instance.PlayerSpellCasting != null && GameManager.Instance.PlayerSpellCasting.IsPlayingAnim;
            if (!wasCastingSpell && isCastingSpell)
                NetworkClient.Send(new DFMPPlayerActionReport { Kind = DFMPPlayerActionKind.Spell });

            wasCastingSpell = isCastingSpell;
        }

        static bool TryHandlePlayerWeaponHit(object hitTransformObject, object impactPositionObject, object directionObject, bool arrowHit, bool arrowSummoned)
        {
            Transform hitTransform = hitTransformObject as Transform;
            if (hitTransform == null || !NetworkClient.isConnected || !NetworkClient.ready)
                return false;

            DFMPRemotePlayerHitTarget hitTarget = hitTransform.GetComponentInParent<DFMPRemotePlayerHitTarget>();
            if (hitTarget != null && hitTarget.ConnectionId != DFMPSpawnAssignmentController.LocalConnectionId)
            {
                NetworkClient.Send(new DFMPDamageIntent
                {
                    RequestId = nextDamageRequestId++,
                    Sequence = nextDamageSequence++,
                    SourceKind = DFMPDamageSourceKind.Player,
                    VitalKind = DFMPVitalKind.Health,
                    AttackKind = arrowHit ? DFMPCombatAttackKind.Ranged : DFMPCombatAttackKind.Melee,
                    TargetConnectionId = hitTarget.ConnectionId,
                    Amount = 5
                });
                string hitKind = arrowHit ? "ranged" : "melee";
                Debug.Log($"[DFMP Combat] Submitted {hitKind} weapon-hit intent: target={hitTarget.ConnectionId}, amount=5, summonedArrow={arrowSummoned}.");
                return true;
            }

            DFMPDynamicEnemyHitTarget enemyTarget = hitTransform.GetComponentInParent<DFMPDynamicEnemyHitTarget>();
            if (enemyTarget != null && enemyTarget.IsDamageable && !string.IsNullOrWhiteSpace(enemyTarget.EnemyId))
            {
                NetworkClient.Send(new DFMPDamageIntent
                {
                    RequestId = nextDamageRequestId++,
                    Sequence = nextDamageSequence++,
                    SourceKind = DFMPDamageSourceKind.Player,
                    VitalKind = DFMPVitalKind.Health,
                    AttackKind = arrowHit ? DFMPCombatAttackKind.Ranged : DFMPCombatAttackKind.Melee,
                    TargetEnemyId = enemyTarget.EnemyId,
                    Amount = 5
                });
                string hitKind = arrowHit ? "ranged" : "melee";
                Debug.Log($"[DFMP Combat] Submitted {hitKind} weapon-hit intent on dynamic enemy: enemyId={enemyTarget.EnemyId}, amount=5, summonedArrow={arrowSummoned}.");
                return true;
            }

            return false;
        }

        static bool TryHandlePlayerMissileHit(object hitColliderObject)
        {
            Collider hitCollider = hitColliderObject as Collider;
            if (hitCollider == null || !NetworkClient.isConnected || !NetworkClient.ready)
                return false;

            DFMPRemotePlayerHitTarget hitTarget = hitCollider.transform.GetComponentInParent<DFMPRemotePlayerHitTarget>();
            if (hitTarget != null && hitTarget.ConnectionId != DFMPSpawnAssignmentController.LocalConnectionId)
            {
                NetworkClient.Send(new DFMPDamageIntent
                {
                    RequestId = nextDamageRequestId++,
                    Sequence = nextDamageSequence++,
                    SourceKind = DFMPDamageSourceKind.Player,
                    VitalKind = DFMPVitalKind.Health,
                    AttackKind = DFMPCombatAttackKind.Ranged,
                    TargetConnectionId = hitTarget.ConnectionId,
                    Amount = 5
                });
                Debug.Log($"[DFMP Combat] Submitted ranged missile-hit intent: target={hitTarget.ConnectionId}, amount=5.");
                return true;
            }

            DFMPDynamicEnemyHitTarget enemyTarget = hitCollider.transform.GetComponentInParent<DFMPDynamicEnemyHitTarget>();
            if (enemyTarget != null && enemyTarget.IsDamageable && !string.IsNullOrWhiteSpace(enemyTarget.EnemyId))
            {
                NetworkClient.Send(new DFMPDamageIntent
                {
                    RequestId = nextDamageRequestId++,
                    Sequence = nextDamageSequence++,
                    SourceKind = DFMPDamageSourceKind.Player,
                    VitalKind = DFMPVitalKind.Health,
                    AttackKind = DFMPCombatAttackKind.Ranged,
                    TargetEnemyId = enemyTarget.EnemyId,
                    Amount = 5
                });
                Debug.Log($"[DFMP Combat] Submitted ranged missile-hit intent on dynamic enemy: enemyId={enemyTarget.EnemyId}, amount=5.");
                return true;
            }

            return false;
        }

        static void OnActionDoorToggled(ulong loadID, bool isOpen)
        {
            if (loadID == 0 || !NetworkClient.isConnected || !NetworkClient.ready)
                return;

            StreamingWorld streamingWorld = FindObjectOfType<StreamingWorld>();
            DFMPWorldContextReport report;
            if (streamingWorld != null && TryBuildWorldContextReport(streamingWorld, out report))
            {
                NetworkClient.Send(new DFMPActionDoorSyncMessage
                {
                    LoadID = loadID,
                    IsOpen = isOpen,
                    Context = report
                });
                Debug.Log($"[DFMP World] Sent action door sync report: loadID={loadID}, isOpen={isOpen}.");
            }
        }

        void SendIdentityReport(bool force)
        {
            if (GameManager.Instance == null || GameManager.Instance.PlayerEntity == null)
                return;

            if (DFMPClientJoinFlowController.Instance != null &&
                !DFMPClientJoinFlowController.Instance.ShouldReportLocalIdentity())
                return;

            var playerEntity = GameManager.Instance.PlayerEntity;
            ulong[] equipTable = playerEntity.ItemEquipTable.SerializeEquipTable();
            int signature = GetInventorySignature(playerEntity.Items.Count, playerEntity.GoldPieces, equipTable);

            // Heartbeat alone would drop an equip made seconds before the player quits.
            bool inventoryChanged = signature != lastInventorySignature;
            if (!force &&
                (inventoryChanged ? Time.unscaledTime < nextChangeReportTime : Time.unscaledTime < nextIdentityReportTime))
                return;

            lastInventorySignature = signature;
            nextChangeReportTime = Time.unscaledTime + ChangeReportInterval;
            nextIdentityReportTime = Time.unscaledTime + HeartbeatReportInterval;

            int[] attributes = new int[8];
            for (int index = 0; index < attributes.Length; index++)
                attributes[index] = playerEntity.Stats.GetPermanentStatValue(index);

            int[] skills = new int[35];
            for (int index = 0; index < skills.Length; index++)
                skills[index] = playerEntity.Skills.GetPermanentSkillValue(index);

            NetworkClient.Send(new DFMPPlayerIdentityReport
            {
                DisplayName = playerEntity.Name,
                Race = (int)playerEntity.Race,
                Gender = (int)playerEntity.Gender,
                OutfitVariant = DFMPPositionProtocol.GetInitialOutfitVariant(playerEntity.FaceIndex),
                FaceVariant = playerEntity.FaceIndex,
                Level = playerEntity.Level,
                Health = playerEntity.CurrentHealth,
                MaxHealth = playerEntity.MaxHealth,
                SpellPoints = playerEntity.CurrentMagicka,
                MaxSpellPoints = playerEntity.MaxMagicka,
                Fatigue = playerEntity.CurrentFatigue,
                MaxFatigue = playerEntity.MaxFatigue,
                Gold = playerEntity.GoldPieces,
                StartingLevelUpSkillSum = playerEntity.StartingLevelUpSkillSum,
                CareerJson = DFMPCareerCodec.Encode(playerEntity.Career),
                Attributes = attributes,
                Skills = skills,
                InventoryJson = DFMPInventorySnapshotCodec.Encode(
                    DFMPCharacterPersistence.CaptureItems(playerEntity.Items.SerializeItems()),
                    DFMPCharacterPersistence.CaptureEquipment(equipTable)),
                EquipmentJson = string.Empty
            });

            SendQuestStateReport(force);
        }

        void SendQuestStateReport(bool force)
        {
            if (!force && Time.unscaledTime < nextQuestStateReportTime)
                return;

            nextQuestStateReportTime =
                Time.unscaledTime + DFMPWorldSettings.CurrentQuestAutosaveIntervalSeconds;
            DFMPQuestStateEnvelope questState;
            string questReason;
            if (!DFMPQuestStateCodec.TryCapture(out questState, out questReason))
                return;

            string payload = DFMPQuestStateCodec.Encode(questState);
            string checksum = DFMPQuestPayloadProtocol.ComputeChecksum(Encoding.UTF8.GetBytes(payload));
            if (!force && string.Equals(checksum, lastQuestPayloadChecksum, System.StringComparison.Ordinal))
                return;

            string sentChecksum;
            if (DFMPNetworkClient.SendQuestState(questState, out sentChecksum))
                lastQuestPayloadChecksum = sentChecksum;
        }

        bool SendWorldContextReport(StreamingWorld streamingWorld, bool force)
        {
            DFMPWorldContextReport report;
            if (!TryBuildWorldContextReport(streamingWorld, out report))
                return false;

            int signature = DFMPWorldContextProtocol.GetSignature(report);
            if (!force && signature == lastContextSignature)
                return true;

            lastContextSignature = signature;
            NetworkClient.Send(report);
            return true;
        }

        public static bool TryBuildWorldContextReport(StreamingWorld streamingWorld, out DFMPWorldContextReport report)
        {
            report = new DFMPWorldContextReport();
            if (streamingWorld == null || streamingWorld.LocalPlayerGPS == null)
                return false;

            PlayerGPS playerGPS = streamingWorld.LocalPlayerGPS;
            var mapPixel = playerGPS.CurrentMapPixel;
            report.Kind = DFMPWorldContextKind.Exterior;
            report.MapPixelX = mapPixel.X;
            report.MapPixelY = mapPixel.Y;
            report.RegionIndex = playerGPS.CurrentRegionIndex;

            if (playerGPS.HasCurrentLocation)
            {
                report.LocationIndex = playerGPS.CurrentLocationIndex;
                report.LocationId = playerGPS.CurrentLocation.Name;
            }

            PlayerEnterExit playerEnterExit = GameManager.Instance != null ? GameManager.Instance.PlayerEnterExit : null;
            if (playerEnterExit == null || !playerEnterExit.IsPlayerInside)
                return true;

            if (playerEnterExit.IsPlayerInsideBuilding)
            {
                report.Kind = DFMPWorldContextKind.BuildingInterior;
                report.BuildingKey = playerEnterExit.BuildingDiscoveryData.buildingKey;
                StaticDoor[] exteriorDoors = playerEnterExit.ExteriorDoors;
                if (exteriorDoors != null && exteriorDoors.Length > 0)
                {
                    report.HasExteriorDoor = true;
                    report.ExteriorDoor = DFMPNetworkStaticDoor.FromStaticDoor(exteriorDoors[0]);
                }

                return true;
            }

            if (playerEnterExit.IsPlayerInsideDungeon && playerEnterExit.Dungeon != null)
            {
                report.Kind = DFMPWorldContextKind.Dungeon;
                var dungeonSummary = playerEnterExit.Dungeon.Summary;
                if (dungeonSummary.LocationData.Loaded)
                {
                    report.RegionIndex = dungeonSummary.LocationData.RegionIndex;
                    report.LocationIndex = dungeonSummary.LocationData.LocationIndex;
                }

                report.LocationId = dungeonSummary.LocationName;
                int blockIndex = playerEnterExit.Dungeon.GetPlayerBlockIndex(playerEnterExit.transform.position);
                report.DungeonBlockIndex = blockIndex < 0 ? 0 : blockIndex;
                DFLocation.DungeonBlock blockData;
                if (blockIndex >= 0 && playerEnterExit.Dungeon.GetBlockData(blockIndex, out blockData))
                    report.DungeonBlockName = blockData.BlockName;

                return true;
            }

            return true;
        }

        public static int GetInventorySignature(int itemCount, int gold, ulong[] equipTable)
        {
            int signature = itemCount * 397 ^ gold;
            for (int index = 0; index < equipTable.Length; index++)
                signature = signature * 397 ^ equipTable[index].GetHashCode();

            return signature;
        }
    }
}
