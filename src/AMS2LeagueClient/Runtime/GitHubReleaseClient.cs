using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AMS2LeagueClient.Runtime
{
    public sealed class ReleaseUpdate
    {
        public string Version { get; }
        public Uri DownloadUri { get; }
        public long Size { get; }
        public string Sha256 { get; }
        internal ReleaseUpdate(string version, Uri uri, long size, string sha256)
        {
            Version = version;
            DownloadUri = uri;
            Size = size;
            Sha256 = sha256;
        }
    }

    /// <summary>Public release metadata only. Never sends account/telemetry credentials to GitHub.</summary>
    public sealed class GitHubReleaseClient
    {
        public const string Repository = "choi3724/AMS2KRLeague";
        private const long MaximumInstallerBytes = 512L * 1024 * 1024;
        private readonly HttpClient _http;
        public GitHubReleaseClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

        public async Task<ReleaseUpdate?> FindUpdateAsync(string currentVersion, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/releases/latest");
            request.Headers.UserAgent.ParseAdd("AMS2LeagueOverlay/" + currentVersion);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var body = new MemoryStream();
            await CopyBoundedAsync(source, body, 1024 * 1024, null, token).ConfigureAwait(false);
            return ParseRelease(System.Text.Encoding.UTF8.GetString(body.ToArray()), currentVersion);
        }

        public static ReleaseUpdate? ParseRelease(string json, string currentVersion)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
            string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            string version = tag.StartsWith("v", StringComparison.Ordinal) ? tag.Substring(1) : tag;
            if (!IsNewer(version, currentVersion)) return null;
            // Asset names and URL must belong to this exact tagged public release.
            string name = "AMS2-League-Overlay-" + version + "-Setup.exe";
            JsonElement[] assets = root.GetProperty("assets").EnumerateArray()
                .Where(item => item.GetProperty("name").GetString() == name).ToArray();
            if (assets.Length != 1) throw new InvalidDataException("설치 파일을 찾을 수 없거나 중복되어 있습니다.");
            JsonElement asset = assets[0];
            string url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            string expected = "https://github.com/" + Repository + "/releases/download/" + tag + "/" + name;
            if (!string.Equals(url, expected, StringComparison.Ordinal)) throw new InvalidDataException("공식 저장소의 설치 파일 주소가 아닙니다.");
            long size = asset.GetProperty("size").GetInt64();
            string digest = asset.TryGetProperty("digest", out JsonElement rawDigest) ? rawDigest.GetString() ?? string.Empty : string.Empty;
            if (size <= 0 || size > MaximumInstallerBytes || !Regex.IsMatch(digest, @"^sha256:[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant))
                throw new InvalidDataException("설치 파일의 크기 또는 SHA-256 정보가 유효하지 않습니다.");
            return new ReleaseUpdate(version, new Uri(url), size, digest.Substring(7));
        }

        public async Task<string> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int>? progress, CancellationToken token)
        {
            Directory.CreateDirectory(directory);
            string final = Path.Combine(directory, "AMS2-League-Overlay-" + update.Version + "-Setup.exe");
            string partial = final + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUri);
                request.Headers.UserAgent.ParseAdd("AMS2LeagueOverlay-Updater");
                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                Uri? finalUri = response.RequestMessage?.RequestUri;
                if (finalUri == null || finalUri.Scheme != Uri.UriSchemeHttps
                    || !(finalUri.Host == "github.com" || finalUri.Host == "release-assets.githubusercontent.com" || finalUri.Host == "objects.githubusercontent.com"))
                    throw new InvalidDataException("설치 파일 다운로드가 신뢰할 수 없는 주소로 이동했습니다.");
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != update.Size)
                    throw new InvalidDataException("설치 파일 크기가 릴리스 정보와 다릅니다.");
                using (Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    long copied = await CopyBoundedAsync(source, target, update.Size, progress, token).ConfigureAwait(false);
                    if (copied != update.Size) throw new InvalidDataException("설치 파일 다운로드가 중단되었습니다.");
                }
                using (var stream = File.OpenRead(partial))
                {
                    byte[] actual = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
                    if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(update.Sha256)))
                        throw new InvalidDataException("설치 파일 SHA-256 검증에 실패했습니다.");
                }
                token.ThrowIfCancellationRequested();
                File.Move(partial, final, true);
                return final;
            }
            finally
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
        }

        private static async Task<long> CopyBoundedAsync(Stream source, Stream target, long limit, IProgress<int>? progress, CancellationToken token)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int previousPercent = -1;
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                total += count;
                if (total > limit) throw new InvalidDataException("다운로드 파일이 허용된 크기를 초과했습니다.");
                await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                int percent = (int)(total * 100 / limit);
                if (percent != previousPercent) { progress?.Report(percent); previousPercent = percent; }
            }
            return total;
        }

        // SemVer precedence, including numeric prerelease identifiers and ignored build metadata.
        public static bool IsNewer(string candidate, string current)
        {
            string pattern = @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z";
            Match left = Regex.Match(candidate, pattern, RegexOptions.CultureInvariant);
            Match right = Regex.Match(current, pattern, RegexOptions.CultureInvariant);
            if (!left.Success || !right.Success) throw new InvalidDataException("버전 번호를 확인할 수 없습니다.");
            foreach (Match match in new[] { left, right })
                foreach (string id in match.Groups[4].Value.Split('.'))
                    if (id.Length > 1 && id.All(char.IsDigit) && id[0] == '0') throw new InvalidDataException("버전 번호가 올바르지 않습니다.");
            for (int i = 1; i <= 3; i++)
            {
                int compare = CompareNumeric(left.Groups[i].Value, right.Groups[i].Value);
                if (compare != 0) return compare > 0;
            }
            string a = left.Groups[4].Value, b = right.Groups[4].Value;
            if (a.Length == 0 || b.Length == 0) return a.Length == 0 && b.Length > 0;
            string[] aa = a.Split('.'), bb = b.Split('.');
            for (int i = 0; i < Math.Min(aa.Length, bb.Length); i++)
            {
                bool an = aa[i].All(char.IsDigit), bn = bb[i].All(char.IsDigit);
                int compare = an && bn ? CompareNumeric(aa[i], bb[i]) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(aa[i], bb[i]);
                if (compare != 0) return compare > 0;
            }
            return aa.Length > bb.Length;
        }

        private static int CompareNumeric(string left, string right)
            => left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
    }
}
