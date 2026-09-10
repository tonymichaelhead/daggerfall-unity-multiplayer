using System;
using System.Collections.Generic;

namespace DFMP.Runtime
{
    public sealed class DFMPChatHistoryEntry
    {
        public DFMPChatMessageKind Kind { get; private set; }
        public int ConnectionId { get; private set; }
        public string SenderDisplayName { get; private set; }
        public string MessageText { get; private set; }
        public float ReceivedAt { get; private set; }
        public long Sequence { get; private set; }

        public bool IsPlayerMessage
        {
            get { return Kind == DFMPChatMessageKind.Player; }
        }

        public string DisplayText
        {
            get
            {
                if (IsPlayerMessage)
                    return string.Format("{0}: {1}", SenderDisplayName, MessageText);

                return MessageText;
            }
        }

        public DFMPChatHistoryEntry(DFMPChatDeliveryMessage message, float receivedAt, long sequence)
        {
            Kind = message.Kind;
            ConnectionId = message.ConnectionId;
            SenderDisplayName = message.SenderDisplayName ?? string.Empty;
            MessageText = message.Text ?? string.Empty;
            ReceivedAt = receivedAt;
            Sequence = sequence;
        }

        public string GetDisplayText(int maximumCharacters)
        {
            string displayText = DisplayText;
            if (maximumCharacters < 4 || displayText.Length <= maximumCharacters)
                return displayText;

            return displayText.Substring(0, maximumCharacters - 3) + "...";
        }
    }

    public sealed class DFMPChatHistory
    {
        public const int DefaultCapacity = 100;
        public const float DefaultPassiveLifetimeSeconds = 4f;

        readonly List<DFMPChatHistoryEntry> entries = new List<DFMPChatHistoryEntry>();
        readonly int capacity;
        long revision;

        public IReadOnlyList<DFMPChatHistoryEntry> Entries
        {
            get { return entries; }
        }

        public long Revision
        {
            get { return revision; }
        }

        public DFMPChatHistory(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException("capacity");

            this.capacity = capacity;
        }

        public void Add(DFMPChatDeliveryMessage message, float receivedAt)
        {
            while (entries.Count >= capacity)
                entries.RemoveAt(0);

            revision++;
            entries.Add(new DFMPChatHistoryEntry(message, receivedAt, revision));
        }

        public List<DFMPChatHistoryEntry> GetPassiveEntries(float currentTime, int maximumCount, float lifetimeSeconds = DefaultPassiveLifetimeSeconds)
        {
            var visibleEntries = new List<DFMPChatHistoryEntry>();
            if (maximumCount <= 0 || lifetimeSeconds < 0f)
                return visibleEntries;

            int firstIndex = Math.Max(0, entries.Count - maximumCount);
            for (int index = firstIndex; index < entries.Count; index++)
            {
                DFMPChatHistoryEntry entry = entries[index];
                if (currentTime - entry.ReceivedAt <= lifetimeSeconds)
                    visibleEntries.Add(entry);
            }

            return visibleEntries;
        }

        public void Clear()
        {
            entries.Clear();
            revision++;
        }
    }
}