using System;
using System.Security.Cryptography;
using System.Text;

namespace DFMP.Runtime
{
    /// <summary>
    /// Password handling for the private-beta `server_local` authentication mode.
    /// </summary>
    public static class DFMPCredential
    {
        /// <summary>Client-side stretching. This is the work factor that protects a weak password.</summary>
        public const int ClientIterations = 600000;

        /// <summary>
        /// Server-side iterations are deliberately low: the stored input is already a 256-bit
        /// derived key, so it is not brute-forceable and only needs salting against store theft.
        /// </summary>
        public const int ServerIterations = 10000;

        public const int KeyLength = 32;
        public const int SaltLength = 16;
        public const int MinimumPasswordLength = 8;
        public const int MaximumPasswordLength = 256;

        const string ClientSaltPrefix = "dfmp-credential-v1:";

        /// <summary>
        /// Derives the value the client sends in place of the password. The account id doubles as
        /// the salt so the derivation is reproducible on any machine without server round trips.
        /// </summary>
        public static string DeriveClientCredential(string accountId, string password, int iterations = ClientIterations)
        {
            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException("Account id is required.", "accountId");
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password is required.", "password");

            byte[] salt = Encoding.UTF8.GetBytes(ClientSaltPrefix + accountId.Trim().ToLowerInvariant());
            return ToHex(Pbkdf2HmacSha256(Encoding.UTF8.GetBytes(password), salt, iterations));
        }

        public static string HashCredential(string credential, byte[] salt, int iterations)
        {
            if (string.IsNullOrEmpty(credential))
                throw new ArgumentException("Credential is required.", "credential");
            if (salt == null || salt.Length == 0)
                throw new ArgumentException("Salt is required.", "salt");

            return ToHex(Pbkdf2HmacSha256(Encoding.UTF8.GetBytes(credential), salt, iterations));
        }

        /// <summary>
        /// PBKDF2-HMAC-SHA256 (RFC 8018) for a single 32-byte output block. Written out rather than
        /// using Rfc2898DeriveBytes because this target framework only exposes the HMAC-SHA1 variant,
        /// and the derivation has to match the launcher byte for byte.
        /// </summary>
        static byte[] Pbkdf2HmacSha256(byte[] password, byte[] salt, int iterations)
        {
            if (iterations < 1)
                throw new ArgumentOutOfRangeException("iterations");

            using (var hmac = new HMACSHA256(password))
            {
                byte[] block = new byte[salt.Length + 4];
                Buffer.BlockCopy(salt, 0, block, 0, salt.Length);
                // Big-endian block index 1; a 32-byte output is exactly one SHA-256 block.
                block[salt.Length + 3] = 1;

                byte[] u = hmac.ComputeHash(block);
                byte[] result = new byte[KeyLength];
                Buffer.BlockCopy(u, 0, result, 0, KeyLength);

                for (int i = 1; i < iterations; i++)
                {
                    u = hmac.ComputeHash(u);
                    for (int j = 0; j < KeyLength; j++)
                        result[j] ^= u[j];
                }

                return result;
            }
        }

        public static bool IsValidCredentialFormat(string credential)
        {
            if (credential == null || credential.Length != KeyLength * 2)
                return false;

            foreach (char value in credential)
            {
                bool isHex = (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');
                if (!isHex)
                    return false;
            }

            return true;
        }

        public static bool IsAcceptablePassword(string password, out string reason)
        {
            reason = string.Empty;

            if (password == null || password.Length < MinimumPasswordLength)
            {
                reason = $"password must be at least {MinimumPasswordLength} characters";
                return false;
            }

            if (password.Length > MaximumPasswordLength)
            {
                reason = $"password must be at most {MaximumPasswordLength} characters";
                return false;
            }

            return true;
        }

        public static byte[] CreateSalt()
        {
            byte[] salt = new byte[SaltLength];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(salt);

            return salt;
        }

        public static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int difference = 0;
            for (int i = 0; i < left.Length; i++)
                difference |= left[i] ^ right[i];

            return difference == 0;
        }

        public static string ToHex(byte[] value)
        {
            var builder = new StringBuilder(value.Length * 2);
            foreach (byte item in value)
                builder.Append(item.ToString("x2"));

            return builder.ToString();
        }

        public static byte[] FromHex(string value)
        {
            if (value == null || value.Length % 2 != 0)
                return new byte[0];

            byte[] bytes = new byte[value.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(value.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                    return new byte[0];
            }

            return bytes;
        }
    }
}
