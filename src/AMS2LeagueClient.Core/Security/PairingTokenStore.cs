using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AMS2LeagueClient.Core.Security
{
    /// <summary>
    /// Stores the optional Player pairing credential with Windows DPAPI.
    /// The protected payload can only be decrypted by the Windows user that
    /// created it and is never written to logs or public configuration JSON.
    /// </summary>
    public static class PairingTokenStore
    {
        public const string FileName = "pairing-token.dat";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AMS2LeagueOverlay:pairing:v1");

        public static string Load(string directory)
        {
            string path = ResolvePath(directory);
            if (!File.Exists(path)) return string.Empty;

            try
            {
                byte[] protectedBytes = File.ReadAllBytes(path);
                if (protectedBytes.Length == 0 || protectedBytes.Length > 16 * 1024)
                {
                    throw new InvalidDataException("The protected pairing credential is invalid.");
                }

                byte[] plaintext = ProtectedData.Unprotect(
                    protectedBytes,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                try
                {
                    return Encoding.UTF8.GetString(plaintext);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch (CryptographicException)
            {
                throw new InvalidDataException("The protected pairing credential cannot be opened by this Windows user.");
            }
        }

        public static void Save(string directory, string token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            string root = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, FileName);
            string temporaryPath = path + ".new";
            byte[] plaintext = Encoding.UTF8.GetBytes(token);
            byte[] protectedBytes;
            try
            {
                protectedBytes = ProtectedData.Protect(
                    plaintext,
                    Entropy,
                    DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(protectedBytes, 0, protectedBytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public static void Clear(string directory)
        {
            string path = ResolvePath(directory);
            if (File.Exists(path)) File.Delete(path);
        }

        public static string ResolvePath(string directory)
            => Path.Combine(
                Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory))),
                FileName);
    }
}
