using System;
using System.Security.Cryptography;
using System.Text;
using Mirror;

namespace DFMP.Runtime
{
    public struct DFMPQuestPayloadChunkMessage : NetworkMessage
    {
        public string TransferId;
        public int EnvelopeVersion;
        public int ChunkIndex;
        public int ChunkCount;
        public int TotalBytes;
        public string Checksum;
        public byte[] Data;
    }

    public static class DFMPQuestPayloadProtocol
    {
        public const int ChunkSize = 24 * 1024;
        public const int MaximumChunkCount =
            (DFMPQuestStateCodec.MaximumEnvelopeBytes + ChunkSize - 1) / ChunkSize;

        public static bool TryCreateChunks(
            string payload,
            out DFMPQuestPayloadChunkMessage[] chunks,
            out string reason)
        {
            chunks = new DFMPQuestPayloadChunkMessage[0];
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(payload))
            {
                reason = "quest payload is empty";
                return false;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            if (bytes.Length > DFMPQuestStateCodec.MaximumEnvelopeBytes)
            {
                reason = "quest payload exceeds maximum size";
                return false;
            }

            string checksum = ComputeChecksum(bytes);
            string transferId = checksum.Substring(0, 32);
            int chunkCount = Math.Max(1, (bytes.Length + ChunkSize - 1) / ChunkSize);
            if (chunkCount > MaximumChunkCount)
            {
                reason = "quest payload requires too many chunks";
                return false;
            }

            chunks = new DFMPQuestPayloadChunkMessage[chunkCount];
            for (int index = 0; index < chunkCount; index++)
            {
                int offset = index * ChunkSize;
                int length = Math.Min(ChunkSize, bytes.Length - offset);
                var data = new byte[length];
                Buffer.BlockCopy(bytes, offset, data, 0, length);
                chunks[index] = new DFMPQuestPayloadChunkMessage
                {
                    TransferId = transferId,
                    EnvelopeVersion = DFMPQuestStateEnvelope.CurrentVersion,
                    ChunkIndex = index,
                    ChunkCount = chunkCount,
                    TotalBytes = bytes.Length,
                    Checksum = checksum,
                    Data = data
                };
            }

            return true;
        }

        public static bool IsValidMetadata(DFMPQuestPayloadChunkMessage chunk, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrEmpty(chunk.TransferId) || chunk.TransferId.Length != 32 ||
                string.IsNullOrEmpty(chunk.Checksum) || chunk.Checksum.Length != 64)
            {
                reason = "quest chunk identity is invalid";
                return false;
            }
            if (!string.Equals(
                chunk.TransferId,
                chunk.Checksum.Substring(0, 32),
                StringComparison.Ordinal))
            {
                reason = "quest chunk transfer id does not match checksum";
                return false;
            }

            if (chunk.EnvelopeVersion != DFMPQuestStateEnvelope.CurrentVersion)
            {
                reason = "quest chunk version is unsupported";
                return false;
            }

            if (chunk.ChunkCount < 1 || chunk.ChunkCount > MaximumChunkCount ||
                chunk.ChunkIndex < 0 || chunk.ChunkIndex >= chunk.ChunkCount)
            {
                reason = "quest chunk index is invalid";
                return false;
            }

            if (chunk.TotalBytes < 1 || chunk.TotalBytes > DFMPQuestStateCodec.MaximumEnvelopeBytes ||
                chunk.Data == null || chunk.Data.Length < 1 || chunk.Data.Length > ChunkSize)
            {
                reason = "quest chunk size is invalid";
                return false;
            }

            int expectedChunkCount = (chunk.TotalBytes + ChunkSize - 1) / ChunkSize;
            if (chunk.ChunkCount != expectedChunkCount)
            {
                reason = "quest chunk count does not match total size";
                return false;
            }

            int expectedLength = chunk.ChunkIndex == chunk.ChunkCount - 1
                ? chunk.TotalBytes - chunk.ChunkIndex * ChunkSize
                : ChunkSize;
            if (expectedLength < 1 || chunk.Data.Length != expectedLength)
            {
                reason = "quest chunk length does not match transfer metadata";
                return false;
            }

            return true;
        }

        public static string ComputeChecksum(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes ?? new byte[0]);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                    builder.Append(hash[index].ToString("x2"));
                return builder.ToString();
            }
        }
    }

    public sealed class DFMPQuestPayloadAssembler
    {
        string transferId;
        string checksum;
        int totalBytes;
        byte[][] chunks;
        int receivedCount;
        int receivedBytes;

        public void Reset()
        {
            transferId = null;
            checksum = null;
            totalBytes = 0;
            chunks = null;
            receivedCount = 0;
            receivedBytes = 0;
        }

        public bool TryAdd(
            DFMPQuestPayloadChunkMessage chunk,
            out string completedPayload,
            out string reason)
        {
            completedPayload = string.Empty;
            if (!DFMPQuestPayloadProtocol.IsValidMetadata(chunk, out reason))
                return false;

            if (chunks == null || !string.Equals(transferId, chunk.TransferId, StringComparison.Ordinal))
            {
                transferId = chunk.TransferId;
                checksum = chunk.Checksum;
                totalBytes = chunk.TotalBytes;
                chunks = new byte[chunk.ChunkCount][];
                receivedCount = 0;
                receivedBytes = 0;
            }
            else if (!string.Equals(checksum, chunk.Checksum, StringComparison.Ordinal) ||
                     totalBytes != chunk.TotalBytes || chunks.Length != chunk.ChunkCount)
            {
                reason = "quest chunk metadata changed during transfer";
                Reset();
                return false;
            }

            if (chunks[chunk.ChunkIndex] == null)
            {
                chunks[chunk.ChunkIndex] = chunk.Data;
                receivedCount++;
                receivedBytes += chunk.Data.Length;
            }
            else if (!BytesEqual(chunks[chunk.ChunkIndex], chunk.Data))
            {
                reason = "duplicate quest chunk has different data";
                Reset();
                return false;
            }

            if (receivedCount != chunks.Length)
                return true;

            if (receivedBytes != totalBytes)
            {
                reason = "assembled quest payload length is invalid";
                Reset();
                return false;
            }

            var bytes = new byte[totalBytes];
            int offset = 0;
            for (int index = 0; index < chunks.Length; index++)
            {
                Buffer.BlockCopy(chunks[index], 0, bytes, offset, chunks[index].Length);
                offset += chunks[index].Length;
            }

            if (!string.Equals(
                DFMPQuestPayloadProtocol.ComputeChecksum(bytes),
                checksum,
                StringComparison.Ordinal))
            {
                reason = "quest payload checksum mismatch";
                Reset();
                return false;
            }

            completedPayload = Encoding.UTF8.GetString(bytes);
            Reset();
            return true;
        }

        static bool BytesEqual(byte[] first, byte[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
                return false;
            for (int index = 0; index < first.Length; index++)
            {
                if (first[index] != second[index])
                    return false;
            }
            return true;
        }
    }
}
