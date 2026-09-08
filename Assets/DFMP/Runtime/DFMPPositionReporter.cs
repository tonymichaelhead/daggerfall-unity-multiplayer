using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallConnect;
using Mirror;
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
        int lastContextSignature;
        int lastInventorySignature;

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
            SendWorldContextReport(streamingWorld);
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
            if (inventoryChanged ? Time.unscaledTime < nextChangeReportTime : Time.unscaledTime < nextIdentityReportTime)
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
        }

        void SendWorldContextReport(StreamingWorld streamingWorld)
        {
            DFMPWorldContextReport report;
            if (!TryBuildWorldContextReport(streamingWorld, out report))
                return;

            int signature = DFMPWorldContextProtocol.GetSignature(report);
            if (signature == lastContextSignature)
                return;

            lastContextSignature = signature;
            NetworkClient.Send(report);
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