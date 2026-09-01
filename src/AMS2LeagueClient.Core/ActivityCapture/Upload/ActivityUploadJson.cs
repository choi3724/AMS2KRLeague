using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS2LeagueClient.Core.ActivityCapture.Upload
{
    internal static class ActivityUploadJson
    {
        public static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
