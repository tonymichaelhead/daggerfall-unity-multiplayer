using System;
using System.Collections.Generic;

namespace DFMP.Runtime
{
    /// <summary>
    /// Per-address and per-account attempt limiting for the connect handshake.
    /// Uses an injectable clock so lockout windows are testable without waiting.
    /// </summary>
    public sealed class DFMPAuthThrottle
    {
        public const int DefaultMaxAttempts = 5;
        public const double DefaultWindowSeconds = 300.0;
        public const double DefaultLockoutSeconds = 300.0;

        sealed class Bucket
        {
            public int Attempts;
            public double WindowStart;
            public double LockedUntil;
        }

        readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        readonly Func<double> clock;
        readonly int maxAttempts;
        readonly double windowSeconds;
        readonly double lockoutSeconds;

        public DFMPAuthThrottle(
            Func<double> clock,
            int maxAttempts = DefaultMaxAttempts,
            double windowSeconds = DefaultWindowSeconds,
            double lockoutSeconds = DefaultLockoutSeconds)
        {
            this.clock = clock ?? (() => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            this.maxAttempts = Math.Max(1, maxAttempts);
            this.windowSeconds = Math.Max(1.0, windowSeconds);
            this.lockoutSeconds = Math.Max(1.0, lockoutSeconds);
        }

        public bool IsLockedOut(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            Bucket bucket;
            if (!buckets.TryGetValue(key, out bucket))
                return false;

            return clock() < bucket.LockedUntil;
        }

        public void RecordFailure(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            double now = clock();
            Bucket bucket;
            if (!buckets.TryGetValue(key, out bucket))
            {
                bucket = new Bucket { WindowStart = now };
                buckets[key] = bucket;
            }

            if (now - bucket.WindowStart > windowSeconds)
            {
                bucket.WindowStart = now;
                bucket.Attempts = 0;
            }

            bucket.Attempts++;
            if (bucket.Attempts >= maxAttempts)
            {
                bucket.LockedUntil = now + lockoutSeconds;
                bucket.Attempts = 0;
                bucket.WindowStart = now;
            }
        }

        public void RecordSuccess(string key)
        {
            if (!string.IsNullOrEmpty(key))
                buckets.Remove(key);
        }

        public void Clear()
        {
            buckets.Clear();
        }
    }
}
