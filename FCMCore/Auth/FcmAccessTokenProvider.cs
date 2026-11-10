using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FCMCore.Auth;

/// <summary>
/// Obtains and caches the OAuth2 access token used to authenticate FCM requests.
/// </summary>
/// <remarks>
/// <para>
/// FCM requires a two-step exchange: this provider signs a short-lived JWT assertion (RS256) with the 
/// service account's private key, then POSTs it to Google's OAuth2 token endpoint to receive an access 
/// token. That access token (valid for about an hour) is what authorizes each <c>messages:send</c> call.
/// </para>
/// <para>
/// The token is fetched lazily and reused until shortly before it expires. The instance is
/// thread-safe and makes at most one concurrent token request.
/// </para>
/// </remarks>
public sealed class FcmAccessTokenProvider : IDisposable
{
    /// <summary>The OAuth2 scope requested for sending FCM messages.</summary>
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    // Refresh a little before the real expiry so a token is never used in the last moments of its life.
    private static readonly TimeSpan ExpiryShrink = TimeSpan.FromSeconds(60);

    private readonly string _clientEmail;
    private readonly string _tokenUri;
    private readonly RSA _rsa;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _sync = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt;
    private bool _disposed;

    /// <summary>The project ID parsed from the service-account key, if it supplied one.</summary>
    public string? ProjectId { get; }

    /// <summary>
    /// Creates a provider from an already-parsed <see cref="ServiceAccountKey"/>. The provider owns
    /// the RSA key it imports and disposes it when disposed.
    /// </summary>
    /// <param name="key">The parsed service-account key.</param>
    /// <param name="httpClient">
    /// The HTTP client used for the token exchange. When <see langword="null"/>, the provider creates
    /// and owns its own. When supplied, the caller is responsible for disposing it.
    /// </param>
    /// <param name="timeProvider">Clock abstraction, mainly for testing. Defaults to the system clock.</param>
    public FcmAccessTokenProvider(ServiceAccountKey key, HttpClient? httpClient = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(key);

        _clientEmail = key.ClientEmail;
        _tokenUri = key.TokenUri;
        _rsa = key.CreateRsa();
        ProjectId = key.ProjectId;

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Loads a service-account JSON key from a file and creates a provider.</summary>
    /// <param name="serviceAccountFilePath">Path to the service-account JSON key file.</param>
    /// <param name="httpClient">Optional HTTP client for the token exchange (see the constructor).</param>
    public static FcmAccessTokenProvider FromServiceAccountFile(string serviceAccountFilePath, HttpClient? httpClient = null) =>
        new(ServiceAccountKey.FromFile(serviceAccountFilePath), httpClient);

    /// <summary>Parses a service-account JSON key from its content and creates a provider.</summary>
    /// <param name="serviceAccountJson">The full JSON content of the service-account key.</param>
    /// <param name="httpClient">Optional HTTP client for the token exchange (see the constructor).</param>
    public static FcmAccessTokenProvider FromServiceAccountJson(string serviceAccountJson, HttpClient? httpClient = null) =>
        new(ServiceAccountKey.FromJson(serviceAccountJson), httpClient);

    /// <summary>
    /// Returns a valid OAuth2 access token, exchanging a freshly signed JWT assertion for a new one
    /// only when no cached token remains valid.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the token request.</param>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_cachedToken is not null && now < _expiresAt - ExpiryShrink)
                return _cachedToken;

            var (token, expiresIn) = await RequestTokenAsync(now, cancellationToken).ConfigureAwait(false);
            _cachedToken = token;
            _expiresAt = now + TimeSpan.FromSeconds(expiresIn);
            return token;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<(string Token, long ExpiresIn)> RequestTokenAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var assertion = CreateAssertion(now);

        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
            new KeyValuePair<string, string>("assertion", assertion),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUri) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new FcmAuthenticationException("The request to the OAuth2 token endpoint failed.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new FcmAuthenticationException(
                    $"The OAuth2 token endpoint returned {(int)response.StatusCode}: {body}");

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                var token = root.TryGetProperty("access_token", out var tokenElement)
                    ? tokenElement.GetString()
                    : null;

                if (string.IsNullOrEmpty(token))
                    throw new FcmAuthenticationException("The OAuth2 token endpoint response did not contain an access_token.");

                var expiresIn = root.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt64(out var seconds)
                    ? seconds
                    : 3600;

                return (token, expiresIn);
            }
            catch (JsonException ex)
            {
                throw new FcmAuthenticationException("The OAuth2 token endpoint returned a response that was not valid JSON.", ex);
            }
        }
    }

    private string CreateAssertion(DateTimeOffset now)
    {
        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 3600;

        // Header: {"alg":"RS256","typ":"JWT"}
        const string header = """{"alg":"RS256","typ":"JWT"}""";
        // Claims: iss/sub = service account, scope, aud = token endpoint, iat, exp.
        var claims =
            $$"""{"iss":"{{_clientEmail}}","sub":"{{_clientEmail}}","scope":"{{Scope}}","aud":"{{_tokenUri}}","iat":{{iat}},"exp":{{exp}}}""";

        var headerSegment = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header));
        var claimsSegment = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(claims));
        var signingInput = $"{headerSegment}.{claimsSegment}";

        var signature = _rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _rsa.Dispose();
        _sync.Dispose();
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
