using System;
using System.IO;
using System.Linq;
using System.Text;

namespace AMS2LeagueClient.Runtime
{
    public static class ClientInstallationIdentity
    {
        private const string FileName = "installation-id.txt";

        public static string LoadOrCreate(string dataRoot)
        {
            string root = Path.GetFullPath(dataRoot ?? throw new ArgumentNullException(nameof(dataRoot)));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, FileName);
            if (File.Exists(path)) return Validate(File.ReadAllText(path, Encoding.UTF8));

            string created = "client-" + Guid.NewGuid().ToString("N");
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                byte[] bytes = new UTF8Encoding(false).GetBytes(created + Environment.NewLine);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
                return created;
            }
            catch (IOException) when (File.Exists(path))
            {
                return Validate(File.ReadAllText(path, Encoding.UTF8));
            }
        }

        public static bool IsValid(string? value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length >= 8 && normalized.Length <= 128
                && normalized.All(character => char.IsLetterOrDigit(character)
                    || character == '.' || character == '_' || character == ':' || character == '-');
        }

        private static string Validate(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (!IsValid(normalized))
            {
                throw new InvalidDataException("Client installation ID is missing or invalid.");
            }
            return normalized;
        }
    }
}
