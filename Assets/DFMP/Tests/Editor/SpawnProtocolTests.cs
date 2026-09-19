using DFMP.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class SpawnProtocolTests
    {
        [TestCase(false, true, false, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, true, false)]
        [TestCase(true, true, false, true)]
        public void ClientDeathInterception_RejectsUnsafeLifecycleState(
            bool isConnected,
            bool isReady,
            bool hasPendingSpawnAssignment,
            bool hasPendingTransition)
        {
            Assert.IsFalse(DFMPDeathRespawnPolicy.ShouldInterceptClientDeath(
                isConnected,
                isReady,
                hasPendingSpawnAssignment,
                hasPendingTransition));
        }

        [Test]
        public void TransitionReportPolicy_RequestsImmediateContextForDungeonEntryAndReconnect()
        {
            Assert.IsTrue(DFMPTransitionReportPolicy.ShouldReportWorldContextImmediatelyAfterAcknowledgement(DFMPTransitionKind.DungeonEntry));
            Assert.IsTrue(DFMPTransitionReportPolicy.ShouldReportWorldContextImmediatelyAfterAcknowledgement(DFMPTransitionKind.Reconnect));
            Assert.IsFalse(DFMPTransitionReportPolicy.ShouldReportWorldContextImmediatelyAfterAcknowledgement(DFMPTransitionKind.DungeonExit));
            Assert.IsFalse(DFMPTransitionReportPolicy.ShouldReportWorldContextImmediatelyAfterAcknowledgement(DFMPTransitionKind.Door));
            Assert.IsFalse(DFMPTransitionReportPolicy.ShouldReportWorldContextImmediatelyAfterAcknowledgement(DFMPTransitionKind.DeathRespawn));
        }

        [Test]
        public void ClientDeathInterception_AcceptsReadySpawnedSession()
        {
            Assert.IsTrue(DFMPDeathRespawnPolicy.ShouldInterceptClientDeath(true, true, false, false));
        }

        [Test]
        public void DeadSpawnedSession_WithRespawnPosition_AcceptsDeathRespawnAssignment()
        {
            DFMPDeathRespawnRejectionReason reason = DFMPDeathRespawnPolicy.GetRejectionReason(new DFMPDeathRespawnContext
            {
                HasSession = true,
                SpawnConfirmed = true,
                IsDead = true,
                HasPendingTransition = false,
                HasRespawnPosition = true
            });

            Assert.AreEqual(DFMPDeathRespawnRejectionReason.None, reason);
            Assert.IsTrue(DFMPDeathRespawnPolicy.IsAccepted(reason));
        }

        [TestCase(DFMPDeathRespawnRejectionReason.MissingSession, false, true, true, false, true)]
        [TestCase(DFMPDeathRespawnRejectionReason.SpawnNotConfirmed, true, false, true, false, true)]
        [TestCase(DFMPDeathRespawnRejectionReason.PlayerNotDead, true, true, false, false, true)]
        [TestCase(DFMPDeathRespawnRejectionReason.TransitionAlreadyPending, true, true, true, true, true)]
        [TestCase(DFMPDeathRespawnRejectionReason.MissingRespawnPosition, true, true, true, false, false)]
        public void InvalidDeathRespawnContext_RejectsAssignment(
            DFMPDeathRespawnRejectionReason expectedReason,
            bool hasSession,
            bool spawnConfirmed,
            bool isDead,
            bool hasPendingTransition,
            bool hasRespawnPosition)
        {
            DFMPDeathRespawnRejectionReason reason = DFMPDeathRespawnPolicy.GetRejectionReason(new DFMPDeathRespawnContext
            {
                HasSession = hasSession,
                SpawnConfirmed = spawnConfirmed,
                IsDead = isDead,
                HasPendingTransition = hasPendingTransition,
                HasRespawnPosition = hasRespawnPosition
            });

            Assert.AreEqual(expectedReason, reason);
            Assert.IsFalse(DFMPDeathRespawnPolicy.IsAccepted(reason));
        }

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
        public void TransitionAssignment_CarriesInteriorReopenPayload()
        {
            var door = new DaggerfallWorkshop.StaticDoor
            {
                buildingKey = 55,
                blockIndex = 2,
                recordIndex = 3,
                doorIndex = 1
            };

            var assignment = DFMPSpawnProtocol.CreateTransitionAssignment(
                9,
                3,
                DFMPTransitionKind.Reconnect,
                new DFMPWorldPosition { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554 },
                new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.BuildingInterior,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    BuildingKey = 55,
                    LocationId = "Daggerfall"
                },
                null,
                -1,
                true,
                new Vector3(2f, 0.5f, -1f),
                true,
                door);

            Assert.IsTrue(assignment.HasInteriorLocalPosition);
            Assert.AreEqual(2f, assignment.InteriorLocalX);
            Assert.AreEqual(0.5f, assignment.InteriorLocalY);
            Assert.AreEqual(-1f, assignment.InteriorLocalZ);
            Assert.IsTrue(assignment.HasExteriorDoor);
            Assert.AreEqual(55, assignment.ExteriorDoor.BuildingKey);
            Assert.AreEqual(2, assignment.ExteriorDoor.BlockIndex);
            Assert.AreEqual(DFMPWorldContextKind.BuildingInterior, assignment.ContextKind);
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
        public void DungeonTransition_ExteriorEntryAssignsDungeonContext()
        {
            GameObject go = new GameObject("DFMP_DungeonEntryRequestTest");

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
                    LocationId = "Daggerfall Dungeon"
                };
                var request = CreateDungeonTransitionRequest(true);

                Assert.AreEqual(DFMPDungeonTransitionRejectionReason.None, DFMPSpawnProtocol.GetDungeonTransitionRejectionReason(session, currentContext, true, request));

                DFMPWorldContextKey assignedContext = DFMPSpawnProtocol.GetDungeonTransitionAssignedContext(request);
                Assert.AreEqual(DFMPWorldContextKind.Dungeon, assignedContext.Kind);
                Assert.AreEqual(207, assignedContext.MapPixelX);
                Assert.AreEqual(213, assignedContext.MapPixelY);
                Assert.AreEqual("Daggerfall Dungeon", assignedContext.LocationId);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DungeonTransition_DungeonExitAssignsExteriorContext()
        {
            GameObject go = new GameObject("DFMP_DungeonExitRequestTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                session.ConfirmSpawn();
                var currentContext = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Dungeon,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall Dungeon"
                };
                var request = CreateDungeonTransitionRequest(false);

                Assert.AreEqual(DFMPDungeonTransitionRejectionReason.None, DFMPSpawnProtocol.GetDungeonTransitionRejectionReason(session, currentContext, true, request));

                DFMPWorldContextKey assignedContext = DFMPSpawnProtocol.GetDungeonTransitionAssignedContext(request);
                Assert.AreEqual(DFMPWorldContextKind.Exterior, assignedContext.Kind);
                Assert.AreEqual(207, assignedContext.MapPixelX);
                Assert.AreEqual(213, assignedContext.MapPixelY);
                Assert.AreEqual("Daggerfall Dungeon", assignedContext.LocationId);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DungeonTransition_RejectsContextMismatch()
        {
            GameObject go = new GameObject("DFMP_DungeonMismatchRequestTest");

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
                    LocationId = "Daggerfall"
                };

                Assert.AreEqual(DFMPDungeonTransitionRejectionReason.ContextMismatch, DFMPSpawnProtocol.GetDungeonTransitionRejectionReason(session, currentContext, true, CreateDungeonTransitionRequest(true)));
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
        public void JoinSpawn_UsesPersistedPositionInsteadOfStartingLocation()
        {
            var record = DFMPCharacterRecord.CreateNew("account-return", "world-a", "Returning");
            record.WorldX = 6792821;
            record.WorldY = 12.5f;
            record.WorldZ = 9374554;
            record.Context = new DFMPWorldContextRecord
            {
                Kind = "Exterior",
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall"
            };

            var startingLocation = new DFMPServerStartingLocationConfig
            {
                Mode = DFMPStartingLocationModes.ExplicitWorldCoordinates,
                LocationName = "Wayrest",
                WorldX = 1000000,
                WorldZ = 2000000
            };

            DFMPStartingLocationResolution resolution;
            bool usedPersistedPosition;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveJoinSpawn(record, startingLocation, out resolution, out usedPersistedPosition, out reason), reason);
            Assert.IsTrue(usedPersistedPosition);
            Assert.AreEqual(6792821, resolution.Position.WorldX);
            Assert.AreEqual(12.5f, resolution.Position.WorldY);
            Assert.AreEqual(9374554, resolution.Position.WorldZ);
            Assert.AreEqual(DFMPWorldContextKind.Exterior, resolution.Context.Kind);
            Assert.AreEqual(207, resolution.Context.MapPixelX);
            Assert.AreEqual(213, resolution.Context.MapPixelY);
            Assert.AreEqual("Daggerfall", resolution.Context.LocationId);
            Assert.AreEqual(string.Empty, resolution.StartMarkerName);
        }

        [Test]
        public void JoinSpawn_UsesStartingLocationWhenCharacterHasNoPersistedPosition()
        {
            var record = DFMPCharacterRecord.CreateNew("account-new", "world-a", "New Hero");
            var startingLocation = new DFMPServerStartingLocationConfig
            {
                Mode = DFMPStartingLocationModes.ExplicitWorldCoordinates,
                LocationName = "Daggerfall",
                WorldX = 6792821,
                WorldY = 0f,
                WorldZ = 9374554
            };

            DFMPStartingLocationResolution resolution;
            bool usedPersistedPosition;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveJoinSpawn(record, startingLocation, out resolution, out usedPersistedPosition, out reason), reason);
            Assert.IsFalse(usedPersistedPosition);
            Assert.AreEqual(6792821, resolution.Position.WorldX);
            Assert.AreEqual(9374554, resolution.Position.WorldZ);
            Assert.AreEqual("Daggerfall", resolution.Context.LocationId);
        }

        [Test]
        public void JoinSpawn_UsesStartingLocationWhenCharacterRecordIsMissing()
        {
            var startingLocation = new DFMPServerStartingLocationConfig
            {
                Mode = DFMPStartingLocationModes.ExplicitWorldCoordinates,
                LocationName = "Daggerfall",
                WorldX = 6792821,
                WorldZ = 9374554
            };

            DFMPStartingLocationResolution resolution;
            bool usedPersistedPosition;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveJoinSpawn(null, startingLocation, out resolution, out usedPersistedPosition, out reason), reason);
            Assert.IsFalse(usedPersistedPosition);
            Assert.AreEqual(6792821, resolution.Position.WorldX);
            Assert.AreEqual(9374554, resolution.Position.WorldZ);
        }

        [Test]
        public void JoinSpawn_PreservesDungeonLogoutWhenReopenPayloadIsValid()
        {
            var record = DFMPCharacterRecord.CreateNew("account-dungeon", "world-a", "Delver");
            record.WorldX = 6792821;
            record.WorldZ = 9374554;
            record.HasInteriorLocalPosition = true;
            record.InteriorLocalX = 12f;
            record.InteriorLocalY = 1f;
            record.InteriorLocalZ = -4f;
            record.Context = new DFMPWorldContextRecord
            {
                Kind = "Dungeon",
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 17,
                LocationIndex = 0,
                LocationId = "Privateer's Hold"
            };

            DFMPStartingLocationResolution resolution;
            bool usedPersistedPosition;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveJoinSpawn(record, null, out resolution, out usedPersistedPosition, out reason), reason);
            Assert.IsTrue(usedPersistedPosition);
            Assert.AreEqual(6792821, resolution.Position.WorldX);
            Assert.AreEqual(9374554, resolution.Position.WorldZ);
            Assert.AreEqual(DFMPWorldContextKind.Dungeon, resolution.Context.Kind);
            Assert.AreEqual("Privateer's Hold", resolution.Context.LocationId);
            Assert.AreEqual(207, resolution.Context.MapPixelX);
            Assert.AreEqual(213, resolution.Context.MapPixelY);
        }

        [Test]
        public void JoinSpawn_FlattensDungeonLogoutWithoutLocationIdentity()
        {
            var record = DFMPCharacterRecord.CreateNew("account-dungeon-fallback", "world-a", "Delver");
            record.WorldX = 6792821;
            record.WorldZ = 9374554;
            record.Context = new DFMPWorldContextRecord
            {
                Kind = "Dungeon",
                MapPixelX = 207,
                MapPixelY = 213
            };

            DFMPStartingLocationResolution resolution;
            bool usedPersistedPosition;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveJoinSpawn(record, null, out resolution, out usedPersistedPosition, out reason), reason);
            Assert.IsTrue(usedPersistedPosition);
            Assert.AreEqual(DFMPWorldContextKind.Exterior, resolution.Context.Kind);
            Assert.IsTrue(DFMPSpawnProtocol.IsValidMapPixel(resolution.Context.MapPixelX, resolution.Context.MapPixelY));
        }

        [Test]
        public void JoinSpawn_PreservesBuildingInteriorWhenExteriorDoorIsPersisted()
        {
            var record = DFMPCharacterRecord.CreateNew("account-building", "world-a", "Shopper");
            record.WorldX = 6792821;
            record.WorldZ = 9374554;
            record.HasInteriorLocalPosition = true;
            record.InteriorLocalX = 1.5f;
            record.InteriorLocalY = 0.2f;
            record.InteriorLocalZ = 2f;
            record.Context = new DFMPWorldContextRecord
            {
                Kind = "BuildingInterior",
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 17,
                LocationIndex = 0,
                LocationId = "Daggerfall",
                BuildingKey = 12345
            };
            record.ExteriorDoors = new[]
            {
                new DFMPStaticDoorRecord { BuildingKey = 12345 }
            };

            DFMPStartingLocationResolution resolution;
            bool usedPersistedPosition;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveJoinSpawn(record, null, out resolution, out usedPersistedPosition, out reason), reason);
            Assert.IsTrue(usedPersistedPosition);
            Assert.AreEqual(DFMPWorldContextKind.BuildingInterior, resolution.Context.Kind);
            Assert.AreEqual(12345, resolution.Context.BuildingKey);
        }

        [Test]
        public void JoinSpawn_FlattensBuildingInteriorWithoutExteriorDoors()
        {
            var record = DFMPCharacterRecord.CreateNew("account-building-fallback", "world-a", "Shopper");
            record.WorldX = 6792821;
            record.WorldZ = 9374554;
            record.Context = new DFMPWorldContextRecord
            {
                Kind = "BuildingInterior",
                MapPixelX = 207,
                MapPixelY = 213,
                BuildingKey = 12345,
                LocationId = "Daggerfall"
            };

            DFMPStartingLocationResolution resolution;
            bool usedPersistedPosition;
            string reason;
            Assert.IsTrue(DFMPSpawnProtocol.TryResolveJoinSpawn(record, null, out resolution, out usedPersistedPosition, out reason), reason);
            Assert.IsTrue(usedPersistedPosition);
            Assert.AreEqual(DFMPWorldContextKind.Exterior, resolution.Context.Kind);
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

        static DFMPDungeonTransitionRequest CreateDungeonTransitionRequest(bool enterDungeon)
        {
            return new DFMPDungeonTransitionRequest
            {
                EnterDungeon = enterDungeon,
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 41,
                LocationId = "Daggerfall Dungeon"
            };
        }
    }
}
