using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FCMCore;
using FCMCore.Models;

namespace FCMService;

/// <summary>
/// A client for the FCM HTTP v1 provider API. It authenticates each request with an OAuth2 access
/// token obtained from your service-account key and delivers messages to a device token, a topic, or
/// a topic condition.
/// </summary>
/// <remarks>
/// The client is thread-safe and designed to be long-lived: create one per project and reuse it so
/// the HTTP/2 connection and the cached access token are shared across sends.
/// </remarks>
public sealed class FcmClient : IFcmClient, IDisposable
{
    private readonly FcmClientOptions _options;
    private readonly string _projectId;
    private readonly string _sendPath;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    /// <summary>Creates a client that owns its own HTTP/2 <see cref="HttpClient"/>.</summary>
    /// <param name="options">The client configuration.</param>
    public FcmClient(FcmClientOptions options)
        : this(options, CreateDefaultHttpClient(options), ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a client using a caller-supplied <see cref="HttpClient"/> (for example one from
    /// <c>IHttpClientFactory</c>). The caller is responsible for the base address and for disposing
    /// the client.
    /// </summary>
    /// <param name="options">The client configuration.</param>
    /// <param name="httpClient">A pre-configured HTTP client.</param>
    /// <param name="ownsHttpClient">When <see langword="true"/>, the <see cref="HttpClient"/> is disposed with this instance.</param>
    public FcmClient(FcmClientOptions options, HttpClient httpClient, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.TokenProvider);
        ArgumentNullException.ThrowIfNull(httpClient);

        _projectId = options.ProjectId ?? options.TokenProvider.ProjectId
            ?? throw new ArgumentException(
                "No project ID was supplied: set FcmClientOptions.ProjectId or use a service-account key that contains 'project_id'.",
                nameof(options));

        _options = options;
        _sendPath = $"/v1/projects/{_projectId}/messages:send";
        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;

        if (_http.BaseAddress is null)
            _http.BaseAddress = FcmClientOptions.DefaultEndpoint;
    }

    private static HttpClient CreateDefaultHttpClient(FcmClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = FcmClientOptions.DefaultEndpoint,
            Timeout = options.Timeout,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    /// <summary>
    /// Sends a single message to FCM and returns the result. This method does not throw for FCM-level
    /// rejections (invalid token, etc.); inspect <see cref="FcmResponse.IsSuccess"/> or call
    /// <see cref="FcmResponse.EnsureSuccess"/>.
    /// </summary>
    /// <param name="message">The message to deliver.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<FcmResponse> SendAsync(FcmMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Throws InvalidOperationException if the message does not set exactly one target.
        var body = new JsonObject
        {
            ["message"] = message.ToJsonNode(_options.JsonSerializerOptions),
        };

        var json = body.ToJsonString(_options.JsonSerializerOptions);

        var token = await _options.TokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, _sendPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ParseResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends many messages concurrently, returning a result for each in the same order as the input.
    /// Failures are reported per-message rather than throwing.
    /// </summary>
    /// <param name="messages">The messages to deliver.</param>
    /// <param name="maxConcurrency">The maximum number of in-flight requests. Defaults to 10.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<IReadOnlyList<FcmResponse>> SendManyAsync(
        IReadOnlyList<FcmMessage> messages,
        int maxConcurrency = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        var results = new FcmResponse[messages.Count];
        using var gate = new SemaphoreSlim(maxConcurrency);

        var tasks = new Task[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    results[index] = await SendAsync(messages[index], cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static async Task<FcmResponse> ParseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            string? messageName = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    if (document.RootElement.TryGetProperty("name", out var nameElement))
                        messageName = nameElement.GetString();
                }
                catch (JsonException)
                {
                    // Leave messageName null if the body is not the expected JSON shape.
                }
            }

            return new FcmResponse
            {
                IsSuccess = true,
                StatusCode = statusCode,
                MessageName = messageName,
            };
        }

        string? errorStatus = null;
        string? errorCode = null;
        string? reason = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.TryGetProperty("status", out var statusElement))
                        errorStatus = statusElement.GetString();

                    if (error.TryGetProperty("message", out var messageElement))
                        reason = messageElement.GetString();

                    errorCode = ExtractFcmErrorCode(error);
                }
            }
            catch (JsonException)
            {
                // Leave the error fields null if the body is not the expected JSON shape.
            }
        }

        return new FcmResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            ErrorStatus = errorStatus,
            ErrorCode = errorCode,
            Reason = reason,
        };
    }

    // The FCM-specific error code lives in error.details[] under the FcmError type's "errorCode" field.
    private static string? ExtractFcmErrorCode(JsonElement error)
    {
        if (!error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var detail in details.EnumerateArray())
        {
            if (detail.TryGetProperty("errorCode", out var codeElement) &&
                codeElement.ValueKind == JsonValueKind.String)
            {
                return codeElement.GetString();
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
