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
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.Security;

namespace AMS2LeagueClient.Runtime
{
    public sealed class Cafe24BootstrapResponse
    {
        public ActivityScheduledEventOptions ScheduledEvent { get; set; } = new ActivityScheduledEventOptions();
        public string ServiceVersion { get; set; } = string.Empty;
        public DateTimeOffset? ServerTimeUtc { get; set; }
        public string[] GzipRequestRoutes { get; set; } = Array.Empty<string>();
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

    public sealed class Cafe24ActivityUploadTransport : IActivityUploadTransport, ITelemetryChunkUploadTransport, IDisposable
    {
        public const string PlayerActivitiesEndpoint = "v1/player/activities";
        public const string SessionWitnessEndpoint = "v1/session/witness";
        public const string TelemetryChunksEndpoint = "v1/telemetry/chunks";
        public const string CompactTelemetryContentType = "application/vnd.ams2.compact-telemetry-v1";
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
        private readonly TimeProvider _timeProvider;
        public Action<string>? FailureDiagnostic { private get; set; }

        public Cafe24ActivityUploadTransport(ActivityConnectionOptions options, HttpClient? httpClient = null)
            : this(options, string.Empty, string.Empty, httpClient)
        {
        }

        public Cafe24ActivityUploadTransport(
            ActivityConnectionOptions options,
            string installationId,
            string clientVersion,
            HttpClient? httpClient = null,
            TimeProvider? timeProvider = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _timeProvider = timeProvider ?? TimeProvider.System;
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
            catch (AuthenticationRequiredException exception)
            {
                return ActivityUploadTransportResult.Http(401, false, exception.Code);
            }
            catch (InvalidOperationException)
            {
                return ActivityUploadTransportResult.NetworkFailure("ENROLLMENT_UNAVAILABLE");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Idempotency-Key", item.Metadata.IdempotencyKey);
            cancellationToken.ThrowIfCancellationRequested();
            byte[] wirePayload = payload;
            if (_options.SupportsGzipRequest(requestUri, endpoint))
            {
                byte[] gzip = TelemetryChunkSerializer.Gzip(payload);
                if (gzip.Length < payload.Length) wirePayload = gzip;
            }
            request.Content = new ByteArrayContent(wirePayload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (!ReferenceEquals(wirePayload, payload)) request.Content.Headers.ContentEncoding.Add("gzip");
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
                if (statusCode == 401)
                {
                    await RejectCredentialAsync(request.Headers.Authorization?.Parameter, cancellationToken).ConfigureAwait(false);
                    return ActivityUploadTransportResult.Http(401, false, "AUTH_REQUIRED");
                }
                byte[]? responseBytes = await ReadUploadResponseAsync(response, timeout.Token, cancellationToken).ConfigureAwait(false);
                if (statusCode == 403)
                {
                    return ActivityUploadTransportResult.Http(403, false,
                        DescribeForbidden(response, responseBytes, endpoint, item.Metadata.QueueItemId));
                }
                if (responseBytes == null)
                {
                    return IsSuccessStatus(statusCode)
                        ? ActivityUploadTransportResult.NetworkFailure("RESPONSE_TOO_LARGE")
                        : ActivityUploadTransportResult.Http(statusCode, false, "RESPONSE_TOO_LARGE");
                }

                return ParseUploadResponse(statusCode, responseBytes, endpoint, item);
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

        public async Task<TelemetryChunkUploadTransportResult> SendTelemetryChunkAsync(
            TelemetryChunkUploadItem item,
            CancellationToken cancellationToken)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            ThrowIfDisposed();
            TelemetryPendingUploadMetadata metadata = item.Metadata;
            if (!string.Equals(NormalizeEndpoint(metadata.Endpoint), TelemetryChunksEndpoint, StringComparison.Ordinal))
            {
                return TelemetryChunkUploadTransportResult.Failure(400, "ENDPOINT_NOT_ALLOWED", false);
            }
            if (!TryBuildRouteUri(TelemetryChunksEndpoint, out Uri? requestUri) || requestUri == null)
            {
                return TelemetryChunkUploadTransportResult.Failure(400, "HTTPS_API_BASE_URL_REQUIRED", false);
            }
            string idempotencyKey = "telemetry:" + metadata.ChunkId;
            bool supportedContentType = string.Equals(
                    metadata.ContentType,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    metadata.ContentType,
                    CompactTelemetryContentType,
                    StringComparison.OrdinalIgnoreCase);
            if (!IsIdempotencyKeyValid(idempotencyKey)
                || !supportedContentType
                || !string.Equals(metadata.ContentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
            {
                return TelemetryChunkUploadTransportResult.Failure(422, "LOCAL_TELEMETRY_METADATA_INVALID", false);
            }

            byte[] compressedPayload = item.CompressedPayload.ToArray();
            if (!string.Equals(Sha256Hex(compressedPayload), metadata.CompressedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return TelemetryChunkUploadTransportResult.Failure(422, "LOCAL_COMPRESSED_HASH_MISMATCH", false);
            }

            try
            {
                await EnsureAnonymousEnrollmentAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TelemetryChunkUploadTransportResult.Failure(null, "ENROLLMENT_TIMEOUT", true);
            }
            catch (AuthenticationRequiredException exception)
            {
                return TelemetryChunkUploadTransportResult.Failure(401, exception.Code, true);
            }
            catch (Exception exception) when (exception is HttpRequestException
                || exception is IOException
                || exception is InvalidOperationException)
            {
                return TelemetryChunkUploadTransportResult.Failure(null, "ENROLLMENT_UNAVAILABLE", true);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Headers.TryAddWithoutValidation("X-AMS2-Payload-SHA256", metadata.PayloadSha256);
            request.Headers.TryAddWithoutValidation("X-AMS2-Compressed-SHA256", metadata.CompressedSha256);
            request.Headers.TryAddWithoutValidation("X-AMS2-Chunk-Id", metadata.ChunkId);
            request.Headers.TryAddWithoutValidation("X-AMS2-Race-Mode",
                (metadata.RaceMode == "MULTIPLAYER" || metadata.RaceMode == "SINGLE_PLAYER") ? metadata.RaceMode : "UNKNOWN");
            request.Headers.TryAddWithoutValidation("X-AMS2-Session-Id", metadata.SessionId);
            request.Headers.TryAddWithoutValidation("X-AMS2-Session-Fingerprint", metadata.SessionFingerprint);
            request.Headers.TryAddWithoutValidation("X-AMS2-Witness-Id", metadata.WitnessId);
            request.Headers.TryAddWithoutValidation("X-AMS2-Attempt-Id", metadata.AttemptId);
            request.Headers.TryAddWithoutValidation(
                "X-AMS2-Attempt-Number",
                metadata.AttemptNumber.ToString(CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("X-AMS2-Visibility", metadata.Visibility.ToString());
            if (metadata.FirstCapturedAtUtc.HasValue)
            {
                request.Headers.TryAddWithoutValidation(
                    "X-AMS2-Captured-At-Start",
                    metadata.FirstCapturedAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            }
            if (metadata.LastCapturedAtUtc.HasValue)
            {
                request.Headers.TryAddWithoutValidation(
                    "X-AMS2-Captured-At-End",
                    metadata.LastCapturedAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            }
            if (metadata.CompactSchemaId.HasValue)
            {
                request.Headers.TryAddWithoutValidation(
                    "X-AMS2-Compact-Schema-Id",
                    metadata.CompactSchemaId.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (_clientVersion.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("X-AMS2-Client-Version", _clientVersion);
            }
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
            request.Headers.TryAddWithoutValidation("X-AMS2-Authorization", "Bearer " + _options.BearerToken);
            request.Content = new ByteArrayContent(compressedPayload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(metadata.ContentType);
            request.Content.Headers.ContentEncoding.Add("gzip");

            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;
                if (statusCode == 401)
                {
                    await RejectCredentialAsync(request.Headers.Authorization?.Parameter, cancellationToken).ConfigureAwait(false);
                    return TelemetryChunkUploadTransportResult.Failure(401, "AUTH_REQUIRED", true);
                }
                byte[]? responseBytes = await ReadUploadResponseAsync(response, timeout.Token, cancellationToken).ConfigureAwait(false);
                if (statusCode == 403)
                {
                    return TelemetryChunkUploadTransportResult.Failure(403,
                        DescribeForbidden(response, responseBytes, TelemetryChunksEndpoint, metadata.ChunkId), false);
                }
                if (responseBytes == null)
                {
                    return TelemetryChunkUploadTransportResult.Failure(
                        statusCode,
                        "RESPONSE_TOO_LARGE",
                        IsTelemetryRetryableStatus(statusCode));
                }
                TelemetryChunkUploadTransportResult result = ParseTelemetryUploadResponse(
                    statusCode,
                    responseBytes,
                    metadata.ChunkId,
                    metadata.PayloadSha256);
                // A server deployed before the additive long-track schemas must not
                // permanently quarantine valid local records during a rolling update.
                if (statusCode == 400 && result.ResultCode == "COMPACT_SCHEMA_UNKNOWN"
                    && metadata.CompactSchemaId >= 0x0100
                    && Enum.IsDefined(typeof(CompactTelemetrySchemaId), metadata.CompactSchemaId.Value))
                    return TelemetryChunkUploadTransportResult.Failure(
                        statusCode, "COMPACT_SCHEMA_UNKNOWN", true);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TelemetryChunkUploadTransportResult.Failure(null, "UPLOAD_TIMEOUT", true);
            }
            catch (HttpRequestException)
            {
                return TelemetryChunkUploadTransportResult.Failure(null, "NETWORK_UNAVAILABLE", true);
            }
            catch (IOException)
            {
                return TelemetryChunkUploadTransportResult.Failure(null, "NETWORK_IO_FAILURE", true);
            }
        }

        public async Task<Cafe24BootstrapResponse> GetBootstrapAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _options.SetGzipRequestSupport(null);
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

                Cafe24BootstrapResponse bootstrap = ParseBootstrap(responseBytes);
                _options.SetGzipRequestSupport(requestUri,
                    bootstrap.GzipRequestRoutes.Contains(PlayerActivitiesEndpoint, StringComparer.Ordinal),
                    bootstrap.GzipRequestRoutes.Contains(SessionWitnessEndpoint, StringComparer.Ordinal));
                return bootstrap;
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
            if (CredentialIsUsable(_options.BearerToken))
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
                // Another in-flight request may already have recovered the same identity.
                if (CredentialIsUsable(_options.BearerToken))
                    return new Cafe24AnonymousEnrollmentResponse { InstallationId = _installationId, Duplicate = true };
                string existing = PairingTokenStore.Load(_credentialDirectory);
                if (CredentialIsUsable(existing))
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

                if (_timeProvider.GetUtcNow() < _options.NextEnrollmentAttemptUtc)
                    throw new AuthenticationRequiredException(_options.EnrollmentFailureCode);
                _options.EnrollmentFailures = Math.Min(6, _options.EnrollmentFailures + 1);
                _options.NextEnrollmentAttemptUtc = _timeProvider.GetUtcNow()
                    + TimeSpan.FromSeconds(Math.Min(900, 30 * Math.Pow(2, _options.EnrollmentFailures - 1)));
                _options.EnrollmentFailureCode = "AUTH_ENROLLMENT_RETRY";
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
                    _options.EnrollmentFailureCode = "AUTH_ENROLLMENT_HTTP_" + statusCode.ToString(CultureInfo.InvariantCulture);
                    if (statusCode == 403 || statusCode == 409)
                        _options.NextEnrollmentAttemptUtc = _timeProvider.GetUtcNow() + TimeSpan.FromMinutes(15);
                    throw new AuthenticationRequiredException(_options.EnrollmentFailureCode);
                }

                Cafe24AnonymousEnrollmentResponse enrolled = ParseEnrollment(responseBytes);
                if (!ActivityConnectionOptions.IsBearerTokenValid(enrolled.InstallationToken)
                    || !string.Equals(enrolled.InstallationId, _installationId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Cafe24 enrollment response credential is invalid.");
                }
                PairingTokenStore.Save(_credentialDirectory, enrolled.InstallationToken);
                _options.BearerToken = enrolled.InstallationToken;
                _options.RejectedBearerToken = string.Empty;
                _options.EnrollmentFailures = 0;
                enrolled.InstallationToken = string.Empty;
                return enrolled;
            }
            finally
            {
                EnrollmentGate.Release();
            }
        }

        private bool CredentialIsUsable(string credential)
            => ActivityConnectionOptions.IsBearerTokenValid(credential)
                && !string.Equals(credential, _options.RejectedBearerToken, StringComparison.Ordinal);

        private async Task RejectCredentialAsync(string? credential, CancellationToken cancellationToken)
        {
            await EnrollmentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // A delayed 401 for an old request must not reject a newer credential.
                if (credential == _options.BearerToken) _options.RejectedBearerToken = credential ?? string.Empty;
            }
            finally { EnrollmentGate.Release(); }
        }

        private sealed class AuthenticationRequiredException : InvalidOperationException
        {
            public AuthenticationRequiredException(string code) : base("Server authentication requires recovery.") => Code = code;
            public string Code { get; }
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
                    ServerTimeUtc = DateValue(root, "serverTimeUtc"),
                    GzipRequestRoutes = ParseGzipRequestRoutes(root)
                };
            }
            catch (JsonException)
            {
                throw new InvalidDataException("Cafe24 bootstrap response JSON is invalid.");
            }
        }

        private static string[] ParseGzipRequestRoutes(JsonElement root)
        {
            if (!root.TryGetProperty("capabilities", out JsonElement capabilities)
                || capabilities.ValueKind != JsonValueKind.Object
                || !capabilities.TryGetProperty("gzipRequestRoutes", out JsonElement routes)
                || routes.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var supported = new System.Collections.Generic.List<string>();
            foreach (JsonElement value in routes.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String) return Array.Empty<string>();
                string route = value.GetString()!;
                if ((route == PlayerActivitiesEndpoint || route == SessionWitnessEndpoint) && !supported.Contains(route))
                    supported.Add(route);
            }
            return supported.ToArray();
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

        private static async Task<byte[]?> ReadUploadResponseAsync(HttpResponseMessage response,
            CancellationToken timeout, CancellationToken caller)
        {
            bool forbidden = response.StatusCode == HttpStatusCode.Forbidden;
            try { return await ReadLimitedAsync(response.Content, forbidden ? 4096 : MaximumResponseBytes, timeout).ConfigureAwait(false); }
            catch (Exception exception) when (forbidden && !caller.IsCancellationRequested
                && (exception is IOException || exception is HttpRequestException || exception is OperationCanceledException))
            {
                // The HTTP status is known even if the error body cannot be read.
                // Never turn a permanent 403 into an endless network retry.
                return null;
            }
        }

        private string DescribeForbidden(HttpResponseMessage response, byte[]? bytes, string endpoint, string itemId)
        {
            string mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "unknown";
            string kind = mediaType == "text/html" ? "HTML" : mediaType == "application/json" ? "JSON" : "OTHER";
            string code = "HTTP_403_" + kind;
            string requestId = string.Empty;
            if (bytes != null && bytes.Length > 0)
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(bytes);
                    kind = "JSON";
                    code = "HTTP_403_JSON";
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        string error = StringValue(document.RootElement, "error");
                        // Copy known application codes only, never arbitrary echoed values,
                        // message/body, token, cookie, URL or raw HTML into the log/queue.
                        if (error == "SCOPE_FORBIDDEN" || error == "OFFICIAL_RESULT_FORBIDDEN"
                            || error == "PRIVATE_DRIVER_UPLOAD_DENIED" || error == "COMPACT_PRIVATE_UPLOAD_DENIED") code = error;
                        requestId = SafeRequestId(StringValue(document.RootElement, "requestId"));
                    }
                }
                catch (JsonException)
                {
                    if (Encoding.UTF8.GetString(bytes).TrimStart().StartsWith("<", StringComparison.Ordinal))
                    {
                        kind = "HTML";
                        code = "HTTP_403_HTML";
                    }
                }
            }
            if (requestId.Length == 0)
            {
                foreach (string header in new[] { "X-Request-ID", "X-Correlation-ID" })
                {
                    if (response.Headers.TryGetValues(header, out var values)) requestId = SafeRequestId(values.FirstOrDefault() ?? string.Empty);
                    if (requestId.Length > 0) break;
                }
            }
            string safeContentType = mediaType == "application/json" || mediaType == "text/html" || mediaType == "text/plain"
                ? mediaType : "other";
            try
            {
                FailureDiagnostic?.Invoke("endpoint=" + endpoint + " item=" + itemId + " http=403 contentType="
                    + safeContentType + " responseKind=" + kind + " code=" + code + " requestId=" + requestId
                    + " body=" + (bytes == null ? "EXCEEDS_4096_BYTES_OR_UNREADABLE" : "OMITTED") + " action=QUARANTINE");
            }
            catch { /* A logging failure must not turn a permanent 403 into a retry. */ }
            return code;
        }

        private string SafeRequestId(string value)
        {
            // The application's ID is 16 hex chars; common infrastructure IDs are
            // hex/UUID. Reject bearer-shaped or echoed credential values outright.
            if (value.Length < 8 || value.Length > 64 || value.Equals(_options.BearerToken, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return value.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F') || character == '-') ? value : string.Empty;
        }

        private static ActivityUploadTransportResult ParseUploadResponse(int statusCode, byte[] responseBytes,
            string endpoint, ActivityUploadItem item)
        {
            if (responseBytes.Length == 0)
            {
                return IsSuccessStatus(statusCode)
                    ? ActivityUploadTransportResult.Http(statusCode, false, "RESPONSE_EMPTY")
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
                string error = NormalizeResultCode(StringValue(root, "error"));
                string status = NormalizeResultCode(StringValue(root, "status"));
                bool duplicate = BooleanValue(root, "duplicate") || status == "DUPLICATE";
                bool hasErrors = HasResponseErrors(root);
                if (IsSuccessStatus(statusCode) && error.Length == 0 && !hasErrors
                    && (status == "STORED" || status == "DUPLICATE"))
                {
                    // Deployed activity/witness ACKs have no mandatory hash field.
                    // Validate any echoed identity/hash without requiring a wire expansion.
                    string identityField = endpoint == SessionWitnessEndpoint ? "witnessId" : "activityId";
                    using var payload = JsonDocument.Parse(item.PayloadUtf8);
                    string expectedId = StringValue(payload.RootElement, identityField);
                    if ((root.TryGetProperty(identityField, out var id)
                            && (id.ValueKind != JsonValueKind.String || expectedId.Length == 0
                                || id.GetString() != expectedId))
                        || !OptionalAckMatches(root, "bodySha256", item.Metadata.BodySha256, true)
                        || !OptionalAckMatches(root, "contentSha256", item.Metadata.BodySha256, true)
                        || !OptionalAckMatches(root, "idempotencyKey", item.Metadata.IdempotencyKey, false))
                        return ActivityUploadTransportResult.Http(statusCode, false, "RESPONSE_INTEGRITY_MISMATCH");
                    return ActivityUploadTransportResult.Stored(statusCode, duplicate);
                }
                string code = error.Length > 0 ? error : hasErrors ? "RESPONSE_SEMANTIC_ERROR"
                    : status.Length > 0 ? status : "RESPONSE_ACK_UNRESOLVED";
                return ActivityUploadTransportResult.Http(statusCode, false, code);
            }
            catch (JsonException)
            {
                return InvalidResponse(statusCode);
            }
        }

        private static bool OptionalAckMatches(JsonElement root, string name, string expected, bool ignoreCase)
            => !root.TryGetProperty(name, out var value)
                || (value.ValueKind == JsonValueKind.String
                    && string.Equals(value.GetString(), expected, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        private static TelemetryChunkUploadTransportResult ParseTelemetryUploadResponse(
            int statusCode,
            byte[] responseBytes,
            string expectedChunkId,
            string expectedPayloadSha256)
        {
            if (responseBytes.Length == 0)
            {
                return TelemetryChunkUploadTransportResult.Failure(
                    statusCode,
                    "RESPONSE_EMPTY",
                    IsTelemetryRetryableStatus(statusCode));
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(responseBytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return TelemetryChunkUploadTransportResult.Failure(
                        statusCode,
                        "RESPONSE_JSON_INVALID",
                        IsTelemetryRetryableStatus(statusCode));
                }
                JsonElement root = document.RootElement;
                bool duplicate = BooleanValue(root, "duplicate") || BooleanValue(root, "duplicateChunk");
                string status = NormalizeResultCode(StringValue(root, "status"));
                string error = NormalizeResultCode(StringValue(root, "error"));
                if (IsSuccessStatus(statusCode) && error.Length == 0
                    && !HasResponseErrors(root)
                    && (status == "STORED" || status == "DUPLICATE"))
                {
                    string returnedChunkId = StringValue(root, "chunkId");
                    string contentSha256 = StringValue(root, "contentSha256");
                    if (!string.Equals(returnedChunkId, expectedChunkId, StringComparison.Ordinal)
                        || !string.Equals(contentSha256, expectedPayloadSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return TelemetryChunkUploadTransportResult.Failure(
                            statusCode,
                            "RESPONSE_INTEGRITY_MISMATCH",
                            true);
                    }
                    return TelemetryChunkUploadTransportResult.Stored(statusCode, duplicate || status == "DUPLICATE");
                }
                string code = error.Length > 0 && error != "UNKNOWN" ? error
                    : HasResponseErrors(root) ? "RESPONSE_SEMANTIC_ERROR"
                    : status.Length > 0 && status != "UNKNOWN" ? status
                    : "HTTP_" + statusCode.ToString(CultureInfo.InvariantCulture);
                return TelemetryChunkUploadTransportResult.Failure(
                    statusCode,
                    code,
                    IsTelemetryRetryableStatus(statusCode));
            }
            catch (JsonException)
            {
                return TelemetryChunkUploadTransportResult.Failure(
                    statusCode,
                    "RESPONSE_JSON_INVALID",
                    IsTelemetryRetryableStatus(statusCode));
            }
        }

        private static bool IsTelemetryRetryableStatus(int statusCode)
            => IsSuccessStatus(statusCode)
                || statusCode == 401
                || statusCode == 404
                || statusCode == 405
                || statusCode == 408
                || statusCode == 425
                || statusCode == 429
                || statusCode >= 500;

        private static bool BooleanValue(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement value)) return false;
            if (value.ValueKind == JsonValueKind.True) return true;
            return value.ValueKind == JsonValueKind.String
                && bool.TryParse(value.GetString(), out bool parsed)
                && parsed;
        }

        private static ActivityUploadTransportResult InvalidResponse(int statusCode)
            => IsSuccessStatus(statusCode)
                ? ActivityUploadTransportResult.Http(statusCode, false, "RESPONSE_JSON_INVALID")
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

        private static bool HasResponseErrors(JsonElement root)
        {
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null
                && !(error.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(error.GetString()))) return true;
            return root.TryGetProperty("errors", out var errors) && errors.ValueKind != JsonValueKind.Null
                && !(errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() == 0);
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
            if (normalized.Length == 0) return string.Empty;
            // Only contract codes may reach queue metadata or logs. Arbitrary
            // response strings can be echoed credentials, even in an error field.
            return new[] { "STORED", "DUPLICATE", "UNKNOWN", "QUARANTINED", "INVALID_PAYLOAD",
                "VALIDATION_FAILED", "DB_UNAVAILABLE", "IDEMPOTENCY_CONFLICT", "AUTH_REQUIRED",
                "SCOPE_FORBIDDEN", "OFFICIAL_RESULT_FORBIDDEN", "PRIVATE_DRIVER_UPLOAD_DENIED",
                "COMPACT_PRIVATE_UPLOAD_DENIED", "COMPACT_SCHEMA_UNKNOWN", "COMPACT_VALUE_RANGE_INVALID",
                "COMPACT_CRC_MISMATCH", "COMPACT_HASH_MISMATCH", "PAYLOAD_HASH_MISMATCH",
                "CHUNK_ID_CONFLICT", "SESSION_IDENTITY_CONFLICT", "INSTALLATION_ALREADY_PAIRED" }
                .Contains(normalized, StringComparer.Ordinal) ? normalized : "SERVER_ERROR_UNRECOGNIZED";
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
