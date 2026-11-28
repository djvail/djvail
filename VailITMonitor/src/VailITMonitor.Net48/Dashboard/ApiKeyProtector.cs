using System;
using System.Security.Cryptography;
using System.Text;

namespace VailITMonitor.Dashboard
{
    /// <summary>
    /// Keeps the dashboard API key out of the settings file as plain text, using Windows DPAPI
    /// (machine scope): the stored "dpapi:..." value can only be decrypted on this server.
    /// </summary>
    public static class ApiKeyProtector
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VailITMonitor.Reporting.v1");

        public static string Protect(string apiKey)
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.LocalMachine);
            return Prefix + Convert.ToBase64String(bytes);
        }

        /// <summary>Accepts either a protected value or a plain key (handy for testing).</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "";
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored.Trim();

            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
