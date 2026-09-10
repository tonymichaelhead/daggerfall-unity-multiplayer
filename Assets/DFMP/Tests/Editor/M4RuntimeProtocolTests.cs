using System;
using NUnit.Framework;
using UnityEngine;
using DFMP.Runtime;

namespace DFMP.Tests
{
    [TestFixture]
    public class M4RuntimeProtocolTests
    {
        [Test]
        public void ChatValidation_AcceptsValidBoundedInput()
        {
            string sanitized;
            string reason;
            Assert.IsTrue(DFMPChatProtocol.TrySanitize("Hello there! 123", out sanitized, out reason));
            Assert.AreEqual("Hello there! 123", sanitized);
            Assert.IsTrue(string.IsNullOrEmpty(reason));
        }

        [Test]
        public void ChatSubmission_ContainsOnlyClientAuthoredText()
        {
            var fields = typeof(DFMPChatSubmitMessage).GetFields();

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("Text", fields[0].Name);
            Assert.AreEqual(typeof(string), fields[0].FieldType);
        }

        [Test]
        public void ChatValidation_RejectsEmptyOversizedInvalidOrMalformedInput()
        {
            string sanitized;
            string reason;
            Assert.IsFalse(DFMPChatProtocol.TrySanitize(string.Empty, out sanitized, out reason));
            Assert.IsFalse(DFMPChatProtocol.TrySanitize("   ", out sanitized, out reason));
            Assert.IsFalse(DFMPChatProtocol.TrySanitize(new string('x', DFMPChatProtocol.MaxMessageLength + 1), out sanitized, out reason));
            Assert.IsFalse(DFMPChatProtocol.TrySanitize("bad<name>", out sanitized, out reason));
            Assert.IsFalse(DFMPChatProtocol.TrySanitize("\u0001hello", out sanitized, out reason));
        }

        [Test]
        public void ChatRateLimiter_AcceptsAllowedMessagesAndRejectsFlooding()
        {
            var limiter = new DFMPChatRateLimiter(1.0f);
            Assert.IsTrue(limiter.TryConsume(42, 10.0f, out string reason));
            Assert.IsTrue(string.IsNullOrEmpty(reason));
            Assert.IsFalse(limiter.TryConsume(42, 10.2f, out reason));
            Assert.IsTrue(reason.Contains("rate"));
            Assert.IsTrue(limiter.TryConsume(43, 10.2f, out reason));
        }

        [Test]
        public void ChatHistory_EvictsOldestEntryAtCapacity()
        {
            var history = new DFMPChatHistory(2);
            history.Add(CreateChatDelivery(1, "Alyx", "First"), 1f);
            history.Add(CreateChatDelivery(2, "Barenziah", "Second"), 2f);
            history.Add(CreateChatDelivery(3, "Cyrus", "Third"), 3f);

            Assert.AreEqual(2, history.Entries.Count);
            Assert.AreEqual("Second", history.Entries[0].MessageText);
            Assert.AreEqual("Third", history.Entries[1].MessageText);
        }

        [Test]
        public void ChatHistory_PassiveEntriesRespectAgeAndRowLimit()
        {
            var history = new DFMPChatHistory();
            history.Add(CreateChatDelivery(1, "Alyx", "Expired"), 1f);
            history.Add(CreateChatDelivery(2, "Barenziah", "Visible"), 10f);
            history.Add(CreateChatDelivery(3, "Cyrus", "Newest"), 12f);

            var passiveEntries = history.GetPassiveEntries(13f, 2, 5f);

            Assert.AreEqual(2, passiveEntries.Count);
            Assert.AreEqual("Visible", passiveEntries[0].MessageText);
            Assert.AreEqual("Newest", passiveEntries[1].MessageText);
        }

        [Test]
        public void ChatHistory_DefaultPassiveLifetimeIsFourSeconds()
        {
            var history = new DFMPChatHistory();
            history.Add(CreateChatDelivery(1, "Alyx", "Visible"), 10f);

            Assert.AreEqual(1, history.GetPassiveEntries(14f, 6).Count);
            Assert.AreEqual(0, history.GetPassiveEntries(14.01f, 6).Count);
        }

        [Test]
        public void ChatHistory_FormatsSystemMessagesWithoutPlayerPrefix()
        {
            var history = new DFMPChatHistory();
            history.Add(CreateChatDelivery(4, "Alyx", "Hello"), 1f);
            history.Add(new DFMPChatDeliveryMessage
            {
                Kind = DFMPChatMessageKind.Motd,
                ConnectionId = -1,
                SenderDisplayName = string.Empty,
                Text = "Mind the guards."
            }, 2f);

            Assert.AreEqual("Alyx: Hello", history.Entries[0].DisplayText);
            Assert.AreEqual("Mind the guards.", history.Entries[1].DisplayText);
            Assert.IsFalse(history.Entries[1].IsPlayerMessage);
        }

        [Test]
        public void ChatHistory_TruncatesPassiveTextWithoutChangingFullHistory()
        {
            var history = new DFMPChatHistory();
            history.Add(CreateChatDelivery(1, "Alyx", "This message is intentionally long."), 1f);

            Assert.AreEqual("Alyx: This m...", history.Entries[0].GetDisplayText(15));
            Assert.AreEqual("Alyx: This message is intentionally long.", history.Entries[0].DisplayText);
        }

        [Test]
        public void ChatLifecycleNotifier_AnnouncesOnlyConfirmedLifetimeOnce()
        {
            var notifier = new DFMPChatLifecycleNotifier();

            Assert.IsFalse(notifier.TryAnnounceDisconnect(7));
            Assert.IsTrue(notifier.TryAnnounceSpawn(7));
            Assert.IsFalse(notifier.TryAnnounceSpawn(7));
            Assert.IsTrue(notifier.TryAnnounceDisconnect(7));
            Assert.IsFalse(notifier.TryAnnounceDisconnect(7));

            notifier.TryAnnounceSpawn(7);
            notifier.Clear();
            Assert.IsFalse(notifier.TryAnnounceDisconnect(7));
        }

        [Test]
        public void ChatSenderValidation_RejectsUnknownUnreadyOrUnspawnedSession()
        {
            GameObject unreadyObject = new GameObject("DFMP_Test_UnreadySession");
            GameObject readyObject = new GameObject("DFMP_Test_ReadySession");

            try
            {
                var sessionState = unreadyObject.AddComponent<DFMPPlayerSessionState>();
                sessionState.Initialize(7, 0, 0f, 0);
                var result = DFMPChatProtocol.TryValidateSender(sessionState, 7, 7, out string reason);
                Assert.IsFalse(result);
                Assert.IsTrue(reason.Contains("ready") || reason.Contains("spawn"));

                var readySession = readyObject.AddComponent<DFMPPlayerSessionState>();
                readySession.Initialize(7, 0, 0f, 0);
                readySession.ConfirmSpawn();
                Assert.IsTrue(DFMPChatProtocol.TryValidateSender(readySession, 7, 7, out reason));
                Assert.IsTrue(string.IsNullOrEmpty(reason));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unreadyObject);
                UnityEngine.Object.DestroyImmediate(readyObject);
            }
        }

        [Test]
        public void Configuration_DefaultsAndValidation_LoadCorrectly()
        {
            var config = new DFMPServerConfig();
            config.Normalize();

            Assert.AreEqual("Tony's DFU RP", config.ServerName);
            Assert.AreEqual(7777, config.Port);
            Assert.AreEqual(7778, config.DiscoveryPort);
            Assert.AreEqual(16, config.MaxConnections);
            Assert.AreEqual(30, config.TickRate);
            Assert.AreEqual(5.0f, config.HeartbeatInterval);
            Assert.AreEqual(256, config.Chat.MaxMessageLength);
            Assert.AreEqual(1.0f, config.Chat.MinIntervalSeconds);
        }

        [Test]
        public void Configuration_InvalidValues_FallBackDeterministically()
        {
            var config = new DFMPServerConfig
            {
                Port = -1,
                DiscoveryPort = 0,
                MaxConnections = 0,
                TickRate = 0,
                HeartbeatInterval = -1f,
                Chat = new DFMPServerChatConfig { MaxMessageLength = 0, MinIntervalSeconds = -2f }
            };

            config.Normalize();

            Assert.AreEqual(7777, config.Port);
            Assert.AreEqual(7778, config.DiscoveryPort);
            Assert.AreEqual(16, config.MaxConnections);
            Assert.AreEqual(30, config.TickRate);
            Assert.AreEqual(5.0f, config.HeartbeatInterval);
            Assert.AreEqual(256, config.Chat.MaxMessageLength);
            Assert.AreEqual(1.0f, config.Chat.MinIntervalSeconds);
        }

        [Test]
        public void EventBus_SubscribesPublishesAndUnsubscribes()
        {
            var bus = new DFMPEventBus();
            int count = 0;
            Action<DFMPPlayerConnectedEvent> handler = _ => count++;
            bus.PlayerConnected += handler;
            bus.PublishPlayerConnected(new DFMPPlayerConnectedEvent { ConnectionId = 3, Address = "127.0.0.1" });
            Assert.AreEqual(1, count);

            bus.PlayerConnected -= handler;
            bus.PublishPlayerConnected(new DFMPPlayerConnectedEvent { ConnectionId = 4, Address = "127.0.0.1" });
            Assert.AreEqual(1, count);
        }

        [Test]
        public void EventBus_PublishesLifecycleAndChatPayloads()
        {
            var bus = new DFMPEventBus();
            DFMPPlayerSpawnedEvent spawned = null;
            DFMPChatMessageReceivedEvent chat = null;

            bus.PlayerSpawned += e => spawned = e;
            bus.ChatMessageReceived += e => chat = e;

            bus.PublishPlayerSpawned(new DFMPPlayerSpawnedEvent { ConnectionId = 5, WorldX = 100, WorldZ = 200 });
            bus.PublishChatMessageReceived(new DFMPChatMessageReceivedEvent
            {
                Kind = DFMPChatMessageKind.Player,
                ConnectionId = 5,
                SenderDisplayName = "Alyx",
                MessageText = "Hello",
                Message = new DFMPChatDeliveryMessage
                {
                    Kind = DFMPChatMessageKind.Player,
                    ConnectionId = 5,
                    SenderDisplayName = "Alyx",
                    Text = "Hello"
                }
            });

            Assert.NotNull(spawned);
            Assert.AreEqual(100, spawned.WorldX);
            Assert.AreEqual(200, spawned.WorldZ);
            Assert.NotNull(chat);
            Assert.AreEqual(DFMPChatMessageKind.Player, chat.Kind);
            Assert.AreEqual("Alyx", chat.SenderDisplayName);
            Assert.AreEqual("Hello", chat.MessageText);
        }

        static DFMPChatDeliveryMessage CreateChatDelivery(int connectionId, string sender, string text)
        {
            return new DFMPChatDeliveryMessage
            {
                Kind = DFMPChatMessageKind.Player,
                ConnectionId = connectionId,
                SenderDisplayName = sender,
                Text = text
            };
        }
    }
}
