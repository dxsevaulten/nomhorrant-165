using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nomhorrant.Core
{
    /// <summary>
    /// Gerbang akses Evolve UI.
    ///
    /// Prinsip keamanan: kode verifikasi TIDAK PERNAH disimpan (baik di repo maupun config),
    /// yang disimpan hanya SHA-256(salt + code). Hash dibuat lewat tools/gen_access_hash.py
    /// di mesin pengguna masing-masing.
    /// </summary>
    public static class AccessGate
    {
        /// <summary>
        /// Hash tertanam dalam bentuk "sha256:&lt;salt-hex&gt;:&lt;hash-hex&gt;".
        /// Kosongkan (default) berarti menunggu hash dari config.local.json / env var
        /// NOMHORRANT_ACCESS_HASH. Jangan pernah menaruh kode asli di sini.
        /// </summary>
        public const string EmbeddedAccessHash = "";

        public static string? LoadConfiguredHash()
        {
            var fromEnv = Environment.GetEnvironmentVariable("NOMHORRANT_ACCESS_HASH");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            var cfgPath = Path.Combine(AppContext.BaseDirectory, "config.local.json");
            if (!File.Exists(cfgPath))
                cfgPath = Path.Combine(Directory.GetCurrentDirectory(), "config.local.json");
            if (!File.Exists(cfgPath)) return string.IsNullOrWhiteSpace(EmbeddedAccessHash) ? null : EmbeddedAccessHash;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
                if (doc.RootElement.TryGetProperty("accessHash", out var h))
                    return h.GetString();
            }
            catch (JsonException) { /* config rusak: abaikan */ }
            return string.IsNullOrWhiteSpace(EmbeddedAccessHash) ? null : EmbeddedAccessHash;
        }

        /// <summary>Verifikasi kode pengguna terhadap hash tersimpan. Constant-time comparison.</summary>
        public static bool Verify(string code, string? storedHash = null)
        {
            storedHash ??= LoadConfiguredHash();
            if (string.IsNullOrWhiteSpace(storedHash)) return false; // belum dikonfigurasi → terkunci
            if (string.IsNullOrEmpty(code)) return false;

            var parts = storedHash.Trim().Split(':');
            if (parts.Length != 3 || parts[0] != "sha256") return false;

            var computed = HashHex(parts[1], code);
            return FixedTimeEquals(computed, parts[2]);
        }

        /// <summary>Hash kode dengan salt (dipakai tools/gen_access_hash.py versi .NET).</summary>
        public static string HashHex(string saltHex, string code)
        {
            var salt = Convert.FromHexString(saltHex);
            using var sha = SHA256.Create();
            var buffer = new byte[salt.Length + Encoding.UTF8.GetByteCount(code)];
            salt.CopyTo(buffer, 0);
            Encoding.UTF8.GetBytes(code, 0, code.Length, buffer, salt.Length);
            return Convert.ToHexString(sha.ComputeHash(buffer)).ToLowerInvariant();
        }

        /// <summary>Generate pasangan salt+hash baru (salt acak).</summary>
        public static string Generate(string code)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var saltHex = Convert.ToHexString(salt).ToLowerInvariant();
            return $"sha256:{saltHex}:{HashHex(saltHex, code)}";
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
        }
    }
}
