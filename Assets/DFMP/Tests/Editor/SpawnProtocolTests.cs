using DFMP.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class SpawnProtocolTests
    {
        [Test]
        public void AssignmentState_ReceiveRequeueAndReset_TracksLifecycle()
        {
            var state = new DFMPSpawnAssignmentState();
            var assignment = new DFMPSpawnAssignment { MapPixelX = 207, MapPixelY = 213, WorldX = 6792821, WorldY = 12.5f, WorldZ = 9374554 };

            state.Receive(assignment);

            Assert.IsTrue(state.HasAssignment);
            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);
            Assert.AreEqual(207, state.Assignment.MapPixelX);
            Assert.AreEqual(213, state.Assignment.MapPixelY);
            Assert.AreEqual(6792821, state.Assignment.WorldX);
            Assert.AreEqual(12.5f, state.Assignment.WorldY);
            Assert.AreEqual(9374554, state.Assignment.WorldZ);

            Assert.IsTrue(state.TryRequestTeleport());
            state.Requeue();

            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);

            state.Reset();

            Assert.IsFalse(state.HasAssignment);
            Assert.IsFalse(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);
        }

        [Test]
        public void AssignmentState_RequestsTeleportOnlyOnceUntilRequeued()
        {
            var state = new DFMPSpawnAssignmentState();
            state.Receive(new DFMPSpawnAssignment { MapPixelX = 207, MapPixelY = 213 });

            Assert.IsTrue(state.TryRequestTeleport());
            Assert.IsFalse(state.TryRequestTeleport());
            Assert.IsFalse(state.TryAcknowledge(true, 207, 213));
            Assert.IsTrue(state.TryAcknowledge(false, 207, 213));
            Assert.IsFalse(state.TryRequestTeleport());
        }

        [Test]
        public void AssignmentState_DoesNotAcknowledgeWrongMapPixel()
        {
            var state = new DFMPSpawnAssignmentState();
            state.Receive(new DFMPSpawnAssignment { MapPixelX = 207, MapPixelY = 213 });
            state.TryRequestTeleport();

            Assert.IsFalse(state.TryAcknowledge(false, 208, 213));
            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsTrue(state.TeleportRequested);
        }

        [Test]
        public void SpawnAssignmentController_ResetClearsPendingServerAssignmentFlag()
        {
            DFMPSpawnAssignmentController.Reset();

            Assert.IsFalse(DFMPSpawnAssignmentController.HasPendingServerAssignment);
        }

        [Test]
        public void TransitionAssignmentState_ReceiveRequeueAndReset_TracksLifecycle()
        {
            var state = new DFMPTransitionAssignmentState();
            var assignment = CreateDungeonTransitionAssignment(42);

            state.Receive(assignment);

            Assert.IsTrue(state.HasAssignment);
            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);
            Assert.AreEqual(42, state.Assignment.AssignmentId);
            Assert.AreEqual(-1, state.Assignment.ConnectionId);

            Assert.IsTrue(state.TryRequestTeleport());
            Assert.IsFalse(state.TryRequestTeleport());

            state.Requeue();
            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);

            state.Reset();
            Assert.IsFalse(state.HasAssignment);
            Assert.IsFalse(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);
        }

        [Test]
        public void TransitionAssignmentState_RejectsInvalidAcknowledgements()
        {
            var state = new DFMPTransitionAssignmentState();
            var assignment = CreateDungeonTransitionAssignment(42);
            var acknowledgement = CreateDungeonTransitionAcknowledgement(42, 6792821, 9374554);

            Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.MissingAssignment, state.GetAcknowledgeRejectionReason(acknowledgement, false));

            state.Receive(assignment);
            Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.TeleportNotRequested, state.GetAcknowledgeRejectionReason(acknowledgement, false));

            state.TryRequestTeleport();
            acknowledgement.AssignmentId = 43;
            Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.AssignmentMismatch, state.GetAcknowledgeRejectionReason(acknowledgement, false));

            acknowledgement.AssignmentId = 42;
            Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.Repositioning, state.GetAcknowledgeRejectionReason(acknowledgement, true));

            acknowledgement.WorldX = 6782975;
            Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.OutsideAssignedMapPixel, state.GetAcknowledgeRejectionReason(acknowledgement, false));

            acknowledgement.WorldX = 6792821;
            acknowledgement.Context.DungeonBlockIndex = 8;
            Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.ContextMismatch, state.GetAcknowledgeRejectionReason(acknowledgement, false));
        }

        [Test]
        public void TransitionAssignmentState_AcknowledgesMatchingTransition()
        {
            var state = new DFMPTransitionAssignmentState();
            state.Receive(CreateDungeonTransitionAssignment(42));
            state.TryRequestTeleport();

            Assert.IsTrue(state.TryAcknowledge(CreateDungeonTransitionAcknowledgement(42, 6792821, 9374554), false));
            Assert.IsTrue(state.HasAssignment);
            Assert.IsFalse(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);
        }

        [Test]
        public void TransitionAssignmentState_ServerIssuedAssignmentCanBeAcknowledged()
        {
            var state = new DFMPTransitionAssignmentState();
            state.Receive(CreateDungeonTransitionAssignment(42));

            Assert.IsTrue(state.TryRequestTeleport());
            Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.None, state.GetAcknowledgeRejectionReason(CreateDungeonTransitionAcknowledgement(42, 6792821, 9374554), false));
        }

        [Test]
        public void TransitionAssignment_CarriesCanonicalContext()
        {
            var assignment = CreateDungeonTransitionAssignment(42);

            DFMPWorldContextKey context = DFMPSpawnProtocol.GetAssignedContext(assignment);
            DFMPWorldContextReport report = DFMPSpawnProtocol.GetAssignedContextReport(assignment);

            Assert.AreEqual(DFMPWorldContextKind.Dungeon, context.Kind);
            Assert.AreEqual(207, context.MapPixelX);
            Assert.AreEqual(213, context.MapPixelY);
            Assert.AreEqual(3, context.RegionIndex);
            Assert.AreEqual(41, context.LocationIndex);
            Assert.AreEqual("Daggerfall Dungeon", context.LocationId);
            Assert.AreEqual(7, context.DungeonBlockIndex);
            Assert.AreEqual("S0000161.RDB", context.DungeonBlockName);
            Assert.AreEqual(context.Kind, report.Kind);
            Assert.AreEqual(context.MapPixelX, report.MapPixelX);
            Assert.AreEqual(context.MapPixelY, report.MapPixelY);
            Assert.AreEqual(context.LocationId, report.LocationId);
            Assert.AreEqual(context.DungeonBlockIndex, report.DungeonBlockIndex);
            Assert.AreEqual(context.DungeonBlockName, report.DungeonBlockName);
        }

        [Test]
        public void TransitionAssignment_CarriesAssignedConnectionId()
        {
            var assignment = DFMPSpawnProtocol.CreateTransitionAssignment(
                42,
                7,
                DFMPTransitionKind.Reconnect,
                new DFMPWorldPosition { WorldX = 6792821, WorldY = 12.5f, WorldZ = 9374554 },
                new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall"
                });

            Assert.AreEqual(7, assignment.ConnectionId);
            Assert.AreEqual(DFMPTransitionKind.Reconnect, assignment.Kind);
        }

        [Test]
        public void TransitionProtocol_ConfirmsMatchingReconnectAndUpdatesSession()
        {
            GameObject go = new GameObject("DFMP_TransitionAcknowledgementTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                session.SetMovement(true);
                var assignmentState = new DFMPTransitionAssignmentState();
                assignmentState.Receive(DFMPSpawnProtocol.CreateTransitionAssignment(
                    42,
                    7,
                    DFMPTransitionKind.Reconnect,
                    new DFMPWorldPosition { WorldX = 6792821, WorldY = 12.5f, WorldZ = 9374554 },
                    new DFMPWorldContextKey
                    {
                        Kind = DFMPWorldContextKind.Exterior,
                        MapPixelX = 207,
                        MapPixelY = 213,
                        LocationId = "Daggerfall"
                    }));
                assignmentState.TryRequestTeleport();
                var acknowledgement = new DFMPTransitionAcknowledgement
                {
                    AssignmentId = 42,
                    WorldX = 6792821,
                    WorldY = 12.5f,
                    WorldZ = 9374554,
                    Context = new DFMPWorldContextReport
                    {
                        Kind = DFMPWorldContextKind.Exterior,
                        MapPixelX = 207,
                        MapPixelY = 213,
                        LocationId = "Daggerfall"
                    }
                };

                DFMPTransitionAcknowledgeRejectionReason rejectionReason;
                Assert.IsTrue(DFMPSpawnProtocol.TryConfirmTransition(session, assignmentState, acknowledgement, false, out rejectionReason));

                Assert.AreEqual(DFMPTransitionAcknowledgeRejectionReason.None, rejectionReason);
                Assert.IsTrue(session.SpawnConfirmed);
                Assert.IsFalse(session.IsMoving);
                Assert.AreEqual(6792821, session.WorldX);
                Assert.AreEqual(12.5f, session.WorldY);
                Assert.AreEqual(9374554, session.WorldZ);
                Assert.IsFalse(assignmentState.HasPendingAssignment);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DoorTransition_ExteriorEntryAssignsBuildingInteriorContext()
        {
            GameObject go = new GameObject("DFMP_DoorEntryRequestTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                session.ConfirmSpawn();
                var currentContext = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall"
                };
                var request = CreateDoorTransitionRequest(true);

                Assert.AreEqual(DFMPDoorTransitionRejectionReason.None, DFMPSpawnProtocol.GetDoorTransitionRejectionReason(session, currentContext, true, request));

                DFMPWorldContextKey assignedContext = DFMPSpawnProtocol.GetDoorTransitionAssignedContext(request);
                Assert.AreEqual(DFMPWorldContextKind.BuildingInterior, assignedContext.Kind);
                Assert.AreEqual(207, assignedContext.MapPixelX);
                Assert.AreEqual(213, assignedContext.MapPixelY);
                Assert.AreEqual("Daggerfall", assignedContext.LocationId);
                Assert.AreEqual(12345, assignedContext.BuildingKey);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DoorTransition_BuildingExitAssignsExteriorContext()
        {
            GameObject go = new GameObject("DFMP_DoorExitRequestTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                session.ConfirmSpawn();
                var currentContext = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.BuildingInterior,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall",
                    BuildingKey = 12345
                };
                var request = CreateDoorTransitionRequest(false);

                Assert.AreEqual(DFMPDoorTransitionRejectionReason.None, DFMPSpawnProtocol.GetDoorTransitionRejectionReason(session, currentContext, true, request));

                DFMPWorldContextKey assignedContext = DFMPSpawnProtocol.GetDoorTransitionAssignedContext(request);
                Assert.AreEqual(DFMPWorldContextKind.Exterior, assignedContext.Kind);
                Assert.AreEqual(207, assignedContext.MapPixelX);
                Assert.AreEqual(213, assignedContext.MapPixelY);
                Assert.AreEqual("Daggerfall", assignedContext.LocationId);
                Assert.AreEqual(0, assignedContext.BuildingKey);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DoorTransition_RejectsContextMismatch()
        {
            GameObject go = new GameObject("DFMP_DoorMismatchRequestTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                session.ConfirmSpawn();
                var currentContext = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = 208,
                    MapPixelY = 213,
                    LocationId = "Daggerfall"
                };

                Assert.AreEqual(DFMPDoorTransitionRejectionReason.ContextMismatch, DFMPSpawnProtocol.GetDoorTransitionRejectionReason(session, currentContext, true, CreateDoorTransitionRequest(true)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SpawnProtocol_ValidatesAcknowledgementWithinAssignedMapPixel()
        {
            Assert.IsTrue(DFMPSpawnProtocol.IsWithinMapPixel(6792821, 9374554, 207, 213));
            Assert.IsFalse(DFMPSpawnProtocol.IsWithinMapPixel(6782975, 9374554, 207, 213));
            Assert.IsFalse(DFMPSpawnProtocol.IsWithinMapPixel(6792821, 9338879, 207, 213));
        }

        [Test]
        public void SpawnProtocol_ValidatesMapPixelsAndComputesCenter()
        {
            Assert.IsTrue(DFMPSpawnProtocol.IsValidMapPixel(207, 213));
            Assert.IsFalse(DFMPSpawnProtocol.IsValidMapPixel(0, 213));
            Assert.IsFalse(DFMPSpawnProtocol.IsValidMapPixel(207, 499));

            DFMPWorldPosition center = DFMPSpawnProtocol.GetMapPixelCenter(207, 213);

            Assert.IsTrue(DFMPSpawnProtocol.IsWithinMapPixel(center.WorldX, center.WorldZ, 207, 213));
        }

        [Test]
        public void SpawnProtocol_RejectsAcknowledgementWithoutSession()
        {
            var acknowledgement = new DFMPSpawnAcknowledgement { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554 };

            Assert.IsFalse(DFMPSpawnProtocol.TryConfirmSpawn(null, acknowledgement));
        }

        [Test]
        public void SpawnProtocol_RejectsAcknowledgementOutsideAssignedMapPixel()
        {
            GameObject go = new GameObject("DFMP_SpawnAcknowledgementMismatchTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                var acknowledgement = new DFMPSpawnAcknowledgement { WorldX = 6782975, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsFalse(DFMPSpawnProtocol.TryConfirmSpawn(session, acknowledgement));
                Assert.IsFalse(session.SpawnConfirmed);
                Assert.AreEqual(6799360, session.WorldX);
                Assert.AreEqual(9388032, session.WorldZ);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SpawnProtocol_ConfirmsMatchingAcknowledgementAndPersistsPosition()
        {
            GameObject go = new GameObject("DFMP_SpawnAcknowledgementTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                session.SetMovement(true);
                var acknowledgement = new DFMPSpawnAcknowledgement { WorldX = 6792821, WorldY = 12.5f, WorldZ = 9374554 };

                Assert.IsTrue(DFMPSpawnProtocol.TryConfirmSpawn(session, acknowledgement));
                Assert.IsTrue(session.SpawnConfirmed);
                Assert.IsFalse(session.IsMoving);
                Assert.AreEqual(6792821, session.WorldX);
                Assert.AreEqual(12.5f, session.WorldY);
                Assert.AreEqual(9374554, session.WorldZ);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SpawnProtocol_DaggerfallCityLocationCenter_IsInsideExpectedMapPixel()
        {
            var daggerfallCityRect = new Rect(6782976, 9371648, 32768, 32768);

            DFMPWorldPosition spawn = DFMPSpawnProtocol.GetLocationCenter(daggerfallCityRect);

            Assert.AreEqual(6799360, spawn.WorldX);
            Assert.AreEqual(9388032, spawn.WorldZ);
            Assert.AreEqual(0f, spawn.WorldY);
            Assert.IsTrue(DFMPSpawnProtocol.IsWithinMapPixel(spawn.WorldX, spawn.WorldZ, 207, 213));
        }

        [Test]
        public void StartingLocation_ResolvesExplicitWorldCoordinates()
        {
            var config = new DFMPServerStartingLocationConfig
            {
                Mode = DFMPStartingLocationModes.ExplicitWorldCoordinates,
                LocationName = "Daggerfall",
                WorldX = 6792821,
                WorldY = 12.5f,
                WorldZ = 9374554
            };

            DFMPStartingLocationResolution resolution;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveStartingLocation(config, out resolution, out reason), reason);
            Assert.AreEqual(6792821, resolution.Position.WorldX);
            Assert.AreEqual(12.5f, resolution.Position.WorldY);
            Assert.AreEqual(9374554, resolution.Position.WorldZ);
            Assert.AreEqual(DFMPWorldContextKind.Exterior, resolution.Context.Kind);
            Assert.AreEqual(207, resolution.Context.MapPixelX);
            Assert.AreEqual(213, resolution.Context.MapPixelY);
            Assert.AreEqual("Daggerfall", resolution.Context.LocationId);
        }

        [Test]
        public void StartingLocation_RejectsNamedStartMarkerWithoutSelector()
        {
            var config = new DFMPServerStartingLocationConfig
            {
                Mode = DFMPStartingLocationModes.NamedStartMarker
            };

            DFMPStartingLocationResolution resolution;
            string reason;
            Assert.IsFalse(DFMPSpawnProtocol.TryResolveStartingLocation(config, out resolution, out reason));
            Assert.IsTrue(reason.Contains("requires a marker"));
        }

        [Test]
        public void StartingLocation_SelectsDeterministicStartMarkerBySelector()
        {
            var markerPositions = new[]
            {
                new Vector3(3f, 0f, 3f),
                new Vector3(-3f, 0f, -3f),
                new Vector3(3f, 0f, -3f),
                new Vector3(-3f, 0f, 3f)
            };

            int markerIndex;
            Assert.IsTrue(DFMPSpawnProtocol.TrySelectStartMarker(markerPositions, "southwest", out markerIndex));
            Assert.AreEqual(1, markerIndex);
            Assert.IsTrue(DFMPSpawnProtocol.TrySelectStartMarker(markerPositions, "southeast", out markerIndex));
            Assert.AreEqual(2, markerIndex);
            Assert.IsTrue(DFMPSpawnProtocol.TrySelectStartMarker(markerPositions, "northwest", out markerIndex));
            Assert.AreEqual(3, markerIndex);
            Assert.IsTrue(DFMPSpawnProtocol.TrySelectStartMarker(markerPositions, "northeast", out markerIndex));
            Assert.AreEqual(0, markerIndex);
        }

        [Test]
        public void StartingLocation_RejectsUnknownStartMarkerSelector()
        {
            int markerIndex;
            Assert.IsFalse(DFMPSpawnProtocol.TrySelectStartMarker(new[] { Vector3.zero }, "West Gate", out markerIndex));
            Assert.AreEqual(-1, markerIndex);
        }

        static DFMPTransitionAssignment CreateDungeonTransitionAssignment(int assignmentId)
        {
            return DFMPSpawnProtocol.CreateTransitionAssignment(
                assignmentId,
                DFMPTransitionKind.DungeonEntry,
                new DFMPWorldPosition
                {
                    WorldX = 6792821,
                    WorldY = 12.5f,
                    WorldZ = 9374554
                },
                new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Dungeon,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    RegionIndex = 3,
                    LocationIndex = 41,
                    LocationId = "Daggerfall Dungeon",
                    DungeonBlockIndex = 7,
                    DungeonBlockName = "S0000161.RDB"
                });
        }

        static DFMPTransitionAcknowledgement CreateDungeonTransitionAcknowledgement(int assignmentId, int worldX, int worldZ)
        {
            return new DFMPTransitionAcknowledgement
            {
                AssignmentId = assignmentId,
                WorldX = worldX,
                WorldY = 12.5f,
                WorldZ = worldZ,
                Context = new DFMPWorldContextReport
                {
                    Kind = DFMPWorldContextKind.Dungeon,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    RegionIndex = 3,
                    LocationIndex = 41,
                    LocationId = "Daggerfall Dungeon",
                    DungeonBlockIndex = 7,
                    DungeonBlockName = "S0000161.RDB"
                }
            };
        }

        static DFMPDoorTransitionRequest CreateDoorTransitionRequest(bool enterInterior)
        {
            return new DFMPDoorTransitionRequest
            {
                EnterInterior = enterInterior,
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 41,
                LocationId = "Daggerfall",
                BuildingKey = 12345
            };
        }
    }
}