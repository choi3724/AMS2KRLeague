using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AMS2LeagueClient.Core.Security;

namespace AMS2LeagueClient.Runtime
{
    public sealed class ActivityScheduledEventOptions
    {
        [JsonPropertyName("publicId")]
        public string PublicId { get; set; } = string.Empty;

        [JsonPropertyName("seasonPublicId")]
        public string SeasonPublicId { get; set; } = string.Empty;

        [JsonPropertyName("round")]
        public int? Round { get; set; }

        [JsonPropertyName("track")]
        public string Track { get; set; } = string.Empty;

        [JsonPropertyName("layout")]
        public string Layout { get; set; } = string.Empty;

        [JsonPropertyName("scheduledAtUtc")]
        public DateTimeOffset? ScheduledAtUtc { get; set; }

        [JsonPropertyName("captureOpensAtUtc")]
        public DateTimeOffset? CaptureOpensAtUtc { get; set; }

        [JsonPropertyName("expectedVehicleClass")]
        public string ExpectedVehicleClass { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    public sealed class ActivityConnectionOptions
    {
        private const int MaximumConfigBytes = 64 * 1024;
        private static readonly Regex BearerPattern = new Regex(
            "^[A-Za-z0-9_-]{32,128}$",
            RegexOptions.CultureInvariant);

        public const string DefaultApiBaseUrl = "https://krams2.mycafe24.com/ams2";
        public const string DefaultFileName = "activity-connection.json";

        [JsonPropertyName("apiBaseUrl")]
        public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        [JsonPropertyName("bearerToken")]
        public string BearerToken { get; set; } = string.Empty;

        [JsonPropertyName("scheduledEvent")]
        public ActivityScheduledEventOptions ScheduledEvent { get; set; } = new ActivityScheduledEventOptions();

        [JsonPropertyName("requestTimeoutSeconds")]
        public int RequestTimeoutSeconds { get; set; } = 15;

        [JsonIgnore]
        public string ConfigPath { get; private set; } = string.Empty;

        [JsonIgnore]
        public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);

        [JsonIgnore]
        public bool HasPlayerCredentials => IsBearerTokenValid(BearerToken);

        [JsonIgnore]
        public bool ConfigFileExists { get; private set; }

        public static string DefaultConfigPath
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AMS2KRLeague",
                DefaultFileName);

        public static ActivityConnectionOptions Load(string? path = null)
        {
            string configuredPath = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path!;
            string resolvedPath = Path.GetFullPath(configuredPath);
            string secureDirectory = Path.GetDirectoryName(resolvedPath)
                ?? throw new InvalidDataException("Activity connection configuration directory is invalid.");
            if (!File.Exists(resolvedPath))
            {
                ActivityConnectionOptions defaults = Normalize(new ActivityConnectionOptions(), resolvedPath, false);
                defaults.BearerToken = PairingTokenStore.Load(secureDirectory);
                return defaults;
            }

            var file = new FileInfo(resolvedPath);
            if (file.Length > MaximumConfigBytes)
            {
                throw new InvalidDataException("Activity connection configuration is larger than 64 KiB.");
            }

            byte[] json = File.ReadAllBytes(resolvedPath);
            ActivityConnectionOptions? loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<ActivityConnectionOptions>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
            }
            catch (JsonException)
            {
                // Do not include the source JSON or parser exception in an error;
                // either may contain the Player bearer credential.
                throw new InvalidDataException("Activity connection configuration JSON is invalid.");
            }

            if (loaded == null)
            {
                throw new InvalidDataException("Activity connection configuration must be a JSON object.");
            }

            string legacyBearer = loaded.BearerToken;
            loaded.BearerToken = string.Empty;
            bool requiresRepair = RequiresLegacyRepair(json);
            if (requiresRepair)
            {
                PairingTokenStore.Clear(secureDirectory);
                loaded.ScheduledEvent = new ActivityScheduledEventOptions();
                SanitizeLegacyJson(resolvedPath, json, true);
            }
            else if (IsBearerTokenValid(legacyBearer))
            {
                PairingTokenStore.Save(secureDirectory, legacyBearer);
                SanitizeLegacyJson(resolvedPath, json, false);
            }
            loaded.BearerToken = PairingTokenStore.Load(secureDirectory);
            return Normalize(loaded, resolvedPath, true);
        }

        internal bool TryGetHttpsBaseUri(out Uri? uri)
        {
            uri = null;
            if (!Uri.TryCreate((ApiBaseUrl ?? string.Empty).Trim(), UriKind.Absolute, out Uri? parsed)
                || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(parsed.Host)
                || !string.IsNullOrEmpty(parsed.UserInfo)
                || !string.IsNullOrEmpty(parsed.Query)
                || !string.IsNullOrEmpty(parsed.Fragment))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        internal static bool IsBearerTokenValid(string? value)
            => value != null && BearerPattern.IsMatch(value);

        private static ActivityConnectionOptions Normalize(ActivityConnectionOptions options, string path, bool configFileExists)
        {
            options.ApiBaseUrl = (options.ApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            options.BearerToken = options.BearerToken ?? string.Empty;
            options.ScheduledEvent = options.ScheduledEvent ?? new ActivityScheduledEventOptions();
            NormalizeScheduledEvent(options.ScheduledEvent);

            if (options.RequestTimeoutSeconds < 1 || options.RequestTimeoutSeconds > 120)
            {
                throw new InvalidDataException("Activity connection requestTimeoutSeconds must be between 1 and 120.");
            }
            if (options.ApiBaseUrl.Length > 0 && !options.TryGetHttpsBaseUri(out _))
            {
                throw new InvalidDataException("Activity connection apiBaseUrl must be an absolute HTTPS URL without query, fragment, or user information.");
            }

            options.ConfigPath = path;
            options.ConfigFileExists = configFileExists;
            return options;
        }

        private static bool RequiresLegacyRepair(byte[] originalJson)
        {
            JsonNode? root = JsonNode.Parse(originalJson);
            if (!(root is JsonObject jsonObject)) return false;

            var identityFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "userId",
                "driverId",
                "steamId",
                "steamId64",
                "nickname",
                "displayName",
                "participantName"
            };
            foreach (var property in jsonObject)
            {
                if (identityFields.Contains(property.Key) && HasNonEmptyValue(property.Value)) return true;
            }

            return jsonObject.TryGetPropertyValue("scheduledEvent", out JsonNode? scheduledEvent)
                && ContainsLegacyTestMarker(scheduledEvent);
        }

        private static bool HasNonEmptyValue(JsonNode? node)
        {
            return node is JsonValue value
                && value.TryGetValue(out string? text)
                && !string.IsNullOrWhiteSpace(text);
        }

        private static bool ContainsLegacyTestMarker(JsonNode? node)
        {
            if (node is JsonValue value
                && value.TryGetValue(out string? text)
                && text != null
                && text.IndexOf("CANARY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (node is JsonObject jsonObject)
            {
                foreach (var property in jsonObject)
                {
                    if (ContainsLegacyTestMarker(property.Value)) return true;
                }
            }
            else if (node is JsonArray jsonArray)
            {
                foreach (JsonNode? item in jsonArray)
                {
                    if (ContainsLegacyTestMarker(item)) return true;
                }
            }
            return false;
        }

        private static void SanitizeLegacyJson(string path, byte[] originalJson, bool removeTestFields)
        {
            try
            {
                JsonNode? root = JsonNode.Parse(originalJson);
                if (!(root is JsonObject jsonObject))
                {
                    throw new InvalidDataException("Activity connection configuration must be a JSON object.");
                }

                var removableFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "bearerToken"
                };
                if (removeTestFields)
                {
                    removableFields.UnionWith(new[]
                    {
                        "userId",
                        "driverId",
                        "steamId",
                        "steamId64",
                        "nickname",
                        "displayName",
                        "participantName",
                        "scheduledEvent"
                    });
                }

                var fieldsToRemove = new List<string>();
                foreach (var property in jsonObject)
                {
                    if (removableFields.Contains(property.Key)) fieldsToRemove.Add(property.Key);
                }
                if (fieldsToRemove.Count == 0) return;

                foreach (string field in fieldsToRemove) jsonObject.Remove(field);
                string temporaryPath = path + ".secure-migration";
                try
                {
                    File.WriteAllText(
                        temporaryPath,
                        jsonObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
                        new System.Text.UTF8Encoding(false));
                    File.Move(temporaryPath, path, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is JsonException)
            {
                throw new InvalidDataException("The legacy pairing credential could not be migrated to protected storage.");
            }
        }

        private static void NormalizeScheduledEvent(ActivityScheduledEventOptions value)
        {
            value.PublicId = (value.PublicId ?? string.Empty).Trim();
            value.SeasonPublicId = (value.SeasonPublicId ?? string.Empty).Trim();
            value.Track = (value.Track ?? string.Empty).Trim();
            value.Layout = (value.Layout ?? string.Empty).Trim();
            value.ExpectedVehicleClass = (value.ExpectedVehicleClass ?? string.Empty).Trim();
            value.Status = (value.Status ?? string.Empty).Trim();
        }
    }
}
