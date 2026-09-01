using System;
using System.IO;
using System.Text.Json;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Overlay
{
    internal sealed class OverlayLayoutStore
    {
        private readonly string _path;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public OverlayLayoutStore(string path)
        {
            _path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Layout path is required.", nameof(path)) : path;
        }

        public OverlayLayoutProfile Load()
        {
            try
            {
                if (!File.Exists(_path)) return new OverlayLayoutProfile();
                OverlayLayoutProfile? profile = JsonSerializer.Deserialize<OverlayLayoutProfile>(File.ReadAllText(_path), JsonOptions);
                return profile?.Schema == 1 ? profile : new OverlayLayoutProfile();
            }
            catch
            {
                return new OverlayLayoutProfile();
            }
        }

        public void Save(OverlayLayoutProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile, JsonOptions));
            File.Move(temporary, _path, true);
        }

        public void Reset()
        {
            if (File.Exists(_path)) File.Delete(_path);
            string temporary = _path + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
