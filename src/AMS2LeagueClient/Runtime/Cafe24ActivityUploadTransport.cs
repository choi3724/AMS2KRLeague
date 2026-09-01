using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Security;

namespace AMS2LeagueClient.Runtime
{
    public sealed class Cafe24BootstrapResponse
    {
        public ActivityScheduledEventOptions ScheduledEvent { get; set; } = new ActivityScheduledEventOptions();
        public string ServiceVersion { get; set; } = string.Empty;
        public DateTimeOffset? ServerTimeUtc { get; set; }
    }

    public sealed class Cafe24HealthResponse
    {
        public string Status { get; set; } = string.Empty;
        public string ServiceVersion { get; set; } = string.Empty;
        public int? SchemaVersion { get; set; }
    }

    public sealed class Cafe24AnonymousEnrollmentResponse
    {
        public string InstallationToken { get; set; } = string.Empty;
        public string InstallationId { get; set; } = string.Empty;
        public string[] Scopes { get; set; } = Array.Empty<string>();
        public bool Duplicate { get; set; }
    }

    public sealed class Cafe24ActivityUploadTransport : IActivityUploadTransport, IDisposable
    {
        public const string PlayerActivitiesEndpoint = "v1/player/activities";
        public const string SessionWitnessEndpoint = "v1/session/witness";
        public const string EnrollmentEndpoint = "v1/player/enroll";
        public const string BootstrapEndpoint = "v1/bootstrap";
        public const string HealthEndpoint = "v1/health";

        private const int MaximumResponseBytes = 256 * 1024;
        private static readonly SemaphoreSlim EnrollmentGate = new SemaphoreSlim(1, 1);
        private readonly ActivityConnectionOptions _options;
        private readonly string _installationId;
        private readonly string _clientVersion;
        private readonly string _credentialDirectory;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        public Cafe24ActivityUploadTransport(ActivityConnectionOptions options, HttpClient? httpClient = null)
            : this(options, string.Empty, string.Empty, httpClient)
        {
        }

        public Cafe24ActivityUploadTransport(
            ActivityConnectionOptions options,
            string installationId,
            string clientVersion,
            HttpClient? httpClient = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _installationId = (installationId ?? string.Empty).Trim();
            _clientVersion = (clientVersion ?? string.Empty).Trim();
            _credentialDirectory = Path.GetDirectoryName(_options.ConfigPath)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMS2KRLeague");
            if (httpClient == null)
            {
                // The platform handler performs normal TLS certificate and hostname
                // validation. Redirects are disabled so the bearer credential
                // cannot be forwarded to another origin.
                var handler = new HttpClientHandler { AllowAutoRedirect = false };
                _httpClient = new HttpClient(handler, true)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
                _ownsHttpClient = true;
            }
            else
            {
                _httpClient = httpClient;
            }
        }

        public async Task<ActivityUploadTransportResult> SendAsync(
            ActivityUploadItem item,
            CancellationToken cancellationToken)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            ThrowIfDisposed();

            string endpoint = NormalizeEndpoint(item.Metadata.Endpoint);
            if (!string.Equals(endpoint, PlayerActivitiesEndpoint, StringComparison.Ordinal)
                && !string.Equals(endpoint, SessionWitnessEndpoint, StringComparison.Ordinal))
            {
                return ActivityUploadTransportResult.Http(400, false, "ENDPOINT_NOT_ALLOWED");
            }
            if (!TryBuildRouteUri(endpoint, out Uri? requestUri) || requestUri == null)
            {
                return ActivityUploadTransportResult.Http(400, false, "HTTPS_API_BASE_URL_REQUIRED");
            }
            if (!IsIdempotencyKeyValid(item.Metadata.IdempotencyKey))
            {
                return ActivityUploadTransportResult.Http(400, false, "IDEMPOTENCY_KEY_INVALID");
            }
            if (!string.Equals(item.Metadata.ContentType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return ActivityUploadTransportResult.Http(422, false, "CONTENT_TYPE_NOT_SUPPORTED");
            }

            byte[] payload = item.PayloadUtf8.ToArray();
            string bodySha256 = Sha256Hex(payload);
            if (!string.Equals(bodySha256, item.Metadata.BodySha256, StringComparison.OrdinalIgnoreCase))
            {
                return ActivityUploadTransportResult.Http(422, false, "LOCAL_BODY_HASH_MISMATCH");
            }

            try
            {
                await EnsureAnonymousEnrollmentAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ActivityUploadTransportResult.NetworkFailure("ENROLLMENT_TIMEOUT");
            }
            catch (HttpRequestException)
            {
                return ActivityUploadTransportResult.NetworkFailure("ENROLLMENT_NETWORK_UNAVAILABLE");
            }
            catch (IOException)
            {
                return ActivityUploadTransportResult.NetworkFailure("ENROLLMENT_IO_FAILURE");
            }
            catch (InvalidOperationException)
            {
                return ActivityUploadTransportResult.NetworkFailure("ENROLLMENT_UNAVAILABLE");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Idempotency-Key", item.Metadata.IdempotencyKey);
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
            // Cafe24's shared-hosting FastCGI layer may remove the standard
            // Authorization header before PHP. Send the same value in a
            // service-specific HTTPS compatibility header as well.
            request.Headers.TryAddWithoutValidation(
                "X-AMS2-Authorization",
                "Bearer " + _options.BearerToken);

            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;
                byte[]? responseBytes = await ReadLimitedAsync(
                    response.Content,
                    MaximumResponseBytes,
                    timeout.Token).ConfigureAwait(false);
                if (responseBytes == null)
                {
                    return IsSuccessStatus(statusCode)
                        ? ActivityUploadTransportResult.NetworkFailure("RESPONSE_TOO_LARGE")
                        : ActivityUploadTransportResult.Http(statusCode, false, "RESPONSE_TOO_LARGE");
                }

                return ParseUploadResponse(statusCode, responseBytes);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ActivityUploadTransportResult.NetworkFailure("UPLOAD_TIMEOUT");
            }
            catch (HttpRequestException)
            {
                return ActivityUploadTransportResult.NetworkFailure("NETWORK_UNAVAILABLE");
            }
            catch (IOException)
            {
                return ActivityUploadTransportResult.NetworkFailure("NETWORK_IO_FAILURE");
            }
        }

        public async Task<Cafe24BootstrapResponse> GetBootstrapAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await EnsureAnonymousEnrollmentAsync(cancellationToken).ConfigureAwait(false);
            if (!TryBuildRouteUri(BootstrapEndpoint, out Uri? requestUri) || requestUri == null)
            {
                throw new InvalidOperationException("A valid HTTPS API base URL is required.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;
                byte[]? responseBytes = await ReadLimitedAsync(
                    response.Content,
                    MaximumResponseBytes,
                    timeout.Token).ConfigureAwait(false);
                if (responseBytes == null)
                {
                    throw new InvalidDataException("Cafe24 bootstrap response is too large.");
                }
                if (!IsSuccessStatus(statusCode))
                {
                    throw new HttpRequestException("Cafe24 bootstrap request failed with HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ".");
                }

                return ParseBootstrap(responseBytes);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Cafe24 bootstrap request timed out.");
            }
        }

        public async Task<Cafe24AnonymousEnrollmentResponse> EnsureAnonymousEnrollmentAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (ActivityConnectionOptions.IsBearerTokenValid(_options.BearerToken))
            {
                return new Cafe24AnonymousEnrollmentResponse
                {
                    InstallationToken = string.Empty,
                    InstallationId = _installationId,
                    Duplicate = true
                };
            }
            if (!ClientInstallationIdentity.IsValid(_installationId) || _clientVersion.Length > 32)
            {
                throw new InvalidOperationException("Anonymous enrollment requires a valid installation ID and client version.");
            }

            await EnrollmentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string existing = PairingTokenStore.Load(_credentialDirectory);
                if (ActivityConnectionOptions.IsBearerTokenValid(existing))
                {
                    _options.BearerToken = existing;
                    return new Cafe24AnonymousEnrollmentResponse
                    {
                        InstallationId = _installationId,
                        Duplicate = true
                    };
                }
                if (!TryBuildRouteUri(EnrollmentEndpoint, out Uri? requestUri) || requestUri == null)
                {
                    throw new InvalidOperationException("A valid HTTPS API base URL is required.");
                }

                byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema = "ams2-anonymous-enrollment-v1",
                    installationId = _installationId,
                    clientVersion = _clientVersion
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;
                byte[]? responseBytes = await ReadLimitedAsync(response.Content, MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
                if (responseBytes == null)
                {
                    throw new InvalidDataException("Cafe24 enrollment response is too large.");
                }
                if (!IsSuccessStatus(statusCode))
                {
                    throw new HttpRequestException("Cafe24 anonymous enrollment failed with HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ".");
                }

                Cafe24AnonymousEnrollmentResponse enrolled = ParseEnrollment(responseBytes);
                if (!ActivityConnectionOptions.IsBearerTokenValid(enrolled.InstallationToken)
                    || !string.Equals(enrolled.InstallationId, _installationId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Cafe24 enrollment response credential is invalid.");
                }
                PairingTokenStore.Save(_credentialDirectory, enrolled.InstallationToken);
                _options.BearerToken = enrolled.InstallationToken;
                enrolled.InstallationToken = string.Empty;
                return enrolled;
            }
            finally
            {
                EnrollmentGate.Release();
            }
        }

        public async Task<Cafe24HealthResponse> GetHealthAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!TryBuildRouteUri(HealthEndpoint, out Uri? requestUri) || requestUri == null)
            {
                throw new InvalidOperationException("A valid HTTPS API base URL is required.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;
                byte[]? responseBytes = await ReadLimitedAsync(
                    response.Content,
                    MaximumResponseBytes,
                    timeout.Token).ConfigureAwait(false);
                if (responseBytes == null)
                {
                    throw new InvalidDataException("Cafe24 health response is too large.");
                }
                if (!IsSuccessStatus(statusCode))
                {
                    throw new HttpRequestException("Cafe24 health request failed with HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ".");
                }

                return ParseHealth(responseBytes);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Cafe24 health request timed out.");
            }
        }

        public static Cafe24BootstrapResponse ParseBootstrap(ReadOnlyMemory<byte> utf8Json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(utf8Json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("event", out JsonElement eventElement)
                    || eventElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Cafe24 bootstrap response does not contain an event object.");
                }

                return new Cafe24BootstrapResponse
                {
                    ScheduledEvent = new ActivityScheduledEventOptions
                    {
                        PublicId = StringValue(eventElement, "publicId"),
                        SeasonPublicId = StringValue(eventElement, "seasonPublicId"),
                        Round = IntValue(eventElement, "round"),
                        Track = StringValue(eventElement, "track"),
                        Layout = StringValue(eventElement, "layout"),
                        ScheduledAtUtc = DateValue(eventElement, "scheduledAtUtc"),
                        CaptureOpensAtUtc = DateValue(eventElement, "captureOpensAtUtc"),
                        ExpectedVehicleClass = StringValue(eventElement, "expectedVehicleClass"),
                        Status = StringValue(eventElement, "status")
                    },
                    ServiceVersion = StringValue(root, "serviceVersion"),
                    ServerTimeUtc = DateValue(root, "serverTimeUtc")
                };
            }
            catch (JsonException)
            {
                throw new InvalidDataException("Cafe24 bootstrap response JSON is invalid.");
            }
        }

        public static Cafe24HealthResponse ParseHealth(ReadOnlyMemory<byte> utf8Json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(utf8Json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Cafe24 health response must be a JSON object.");
                }

                return new Cafe24HealthResponse
                {
                    Status = StringValue(root, "status"),
                    ServiceVersion = StringValue(root, "version").Length > 0
                        ? StringValue(root, "version")
                        : StringValue(root, "serviceVersion"),
                    SchemaVersion = IntValue(root, "schemaVersion")
                };
            }
            catch (JsonException)
            {
                throw new InvalidDataException("Cafe24 health response JSON is invalid.");
            }
        }

        public static Cafe24AnonymousEnrollmentResponse ParseEnrollment(ReadOnlyMemory<byte> utf8Json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(utf8Json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Cafe24 enrollment response must be a JSON object.");
                }
                string token = StringValue(root, "installationToken");
                string installationId = StringValue(root, "installationId");
                string[] scopes = root.TryGetProperty("scopes", out JsonElement scopeElement)
                    && scopeElement.ValueKind == JsonValueKind.Array
                    ? scopeElement.EnumerateArray()
                        .Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString() ?? string.Empty)
                        .Where(value => value.Length > 0)
                        .ToArray()
                    : Array.Empty<string>();
                bool duplicate = root.TryGetProperty("duplicate", out JsonElement duplicateElement)
                    && duplicateElement.ValueKind == JsonValueKind.True;
                return new Cafe24AnonymousEnrollmentResponse
                {
                    InstallationToken = token,
                    InstallationId = installationId,
                    Scopes = scopes,
                    Duplicate = duplicate
                };
            }
            catch (JsonException)
            {
                throw new InvalidDataException("Cafe24 enrollment response JSON is invalid.");
            }
        }

        private bool TryBuildRouteUri(string route, out Uri? result)
        {
            result = null;
            if (!_options.TryGetHttpsBaseUri(out Uri? baseUri) || baseUri == null)
            {
                return false;
            }

            string basePath = baseUri.AbsolutePath.TrimEnd('/');
            string apiPath = basePath.EndsWith("/api.php", StringComparison.OrdinalIgnoreCase)
                ? basePath
                : basePath + "/api.php";
            var builder = new UriBuilder(baseUri)
            {
                Path = apiPath,
                Query = "route=" + route,
                Fragment = string.Empty
            };
            result = builder.Uri;
            return string.Equals(result.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
        {
            CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            source.CancelAfter(_options.RequestTimeout);
            return source;
        }

        private static ActivityUploadTransportResult ParseUploadResponse(int statusCode, byte[] responseBytes)
        {
            if (responseBytes.Length == 0)
            {
                return IsSuccessStatus(statusCode)
                    ? ActivityUploadTransportResult.NetworkFailure("RESPONSE_EMPTY")
                    : ActivityUploadTransportResult.Http(statusCode, false, "HTTP_" + statusCode.ToString(CultureInfo.InvariantCulture));
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(responseBytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return InvalidResponse(statusCode);
                }

                JsonElement root = document.RootElement;
                bool duplicate = root.TryGetProperty("duplicate", out JsonElement duplicateElement)
                    && (duplicateElement.ValueKind == JsonValueKind.True
                        || (duplicateElement.ValueKind == JsonValueKind.String
                            && bool.TryParse(duplicateElement.GetString(), out bool parsedDuplicate)
                            && parsedDuplicate));
                string resultCode = duplicate
                    ? "DUPLICATE"
                    : NormalizeResultCode(StringValue(root, "error"));
                if (resultCode.Length == 0)
                {
                    resultCode = NormalizeResultCode(StringValue(root, "status"));
                }
                if (resultCode.Length == 0)
                {
                    return InvalidResponse(statusCode);
                }

                return ActivityUploadTransportResult.Http(statusCode, duplicate, resultCode);
            }
            catch (JsonException)
            {
                return InvalidResponse(statusCode);
            }
        }

        private static ActivityUploadTransportResult InvalidResponse(int statusCode)
            => IsSuccessStatus(statusCode)
                ? ActivityUploadTransportResult.NetworkFailure("RESPONSE_JSON_INVALID")
                : ActivityUploadTransportResult.Http(statusCode, false, "HTTP_" + statusCode.ToString(CultureInfo.InvariantCulture));

        private static async Task<byte[]?> ReadLimitedAsync(
            HttpContent content,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value > maximumBytes)
            {
                return null;
            }

            using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.ToArray();
                }
                if (buffer.Length + read > maximumBytes)
                {
                    return null;
                }
                buffer.Write(chunk, 0, read);
            }
        }

        private static string NormalizeEndpoint(string? value)
            => (value ?? string.Empty).Trim().TrimStart('/');

        private static bool IsIdempotencyKeyValid(string? value)
        {
            if (value == null || value.Length < 8 || value.Length > 128)
            {
                return false;
            }
            return value.All(character => char.IsLetterOrDigit(character)
                || character == '.'
                || character == '_'
                || character == ':'
                || character == '-');
        }

        private static bool IsSuccessStatus(int statusCode)
            => statusCode >= (int)HttpStatusCode.OK && statusCode < 300;

        private static string Sha256Hex(byte[] value)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(value)).ToLowerInvariant();
        }

        private static string StringValue(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement value)
                || value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.Undefined)
            {
                return string.Empty;
            }
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static int? IntValue(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement value))
            {
                return null;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            {
                return number;
            }
            return value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : (int?)null;
        }

        private static DateTimeOffset? DateValue(JsonElement parent, string name)
        {
            string value = StringValue(parent, name);
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed)
                ? parsed
                : (DateTimeOffset?)null;
        }

        private static string NormalizeResultCode(string value)
        {
            string normalized = new string((value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Take(64)
                .Select(character => char.IsLetterOrDigit(character)
                    || character == '_'
                    || character == '-'
                    || character == '.'
                    || character == ':'
                        ? character
                        : '_')
                .ToArray());
            return normalized;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Cafe24ActivityUploadTransport));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
