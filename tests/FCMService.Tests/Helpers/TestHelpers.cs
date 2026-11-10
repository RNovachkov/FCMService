using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FCMCore.Auth;

namespace FCMService.Tests.Helpers;

/// <summary>A <see cref="TimeProvider"/> whose current time can be set by tests.</summary>
internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now += delta;
}

/// <summary>Captures outgoing requests and returns canned responses, with no network I/O.</summary>
internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly Lock _sync = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> Bodies { get; } = [];

    public HttpRequestMessage LastRequest => Requests[^1];
    public string? LastBody => Bodies[^1];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            Requests.Add(request);
            Bodies.Add(body);
        }

        return responder(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A successful FCM send response carrying the given message name.</summary>
    public static HttpResponseMessage Ok(string name = "projects/test-project/messages/1") =>
        Json(HttpStatusCode.OK, $$"""{"name":"{{name}}"}""");
}

/// <summary>Factory for service-account keys and providers backed by a freshly generated RSA key.</summary>
internal static class TestCredentials
{
    public const string ClientEmail = "svc@test.iam.gserviceaccount.com";
    public const string ProjectId = "test-project";
    public const string TokenUri = "https://oauth2.test/token";

    /// <summary>Builds service-account JSON content from a given RSA key.</summary>
    public static string BuildJson(RSA rsa)
    {
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var privateKeyJson = JsonSerializer.Serialize(pem); // quotes and escapes newlines
        return $$"""
            {"type":"service_account","project_id":"{{ProjectId}}","client_email":"{{ClientEmail}}","private_key":{{privateKeyJson}},"token_uri":"{{TokenUri}}"}
            """;
    }

    /// <summary>Writes a throwaway service-account JSON key to a temp file and returns its path.</summary>
    public static string WriteTempJson()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.Combine(Path.GetTempPath(), $"fcm-sa-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, BuildJson(rsa));
        return path;
    }

    /// <summary>
    /// Creates a provider whose token endpoint is stubbed to return <paramref name="accessToken"/>.
    /// The returned <see cref="RSA"/> holds the matching key pair so tests can verify the signed
    /// assertion; the caller disposes it.
    /// </summary>
    public static (FcmAccessTokenProvider Provider, StubHttpMessageHandler TokenHandler, RSA Key) NewProvider(
        string accessToken = "ya29.test-token",
        long expiresIn = 3600,
        TimeProvider? timeProvider = null)
    {
        var rsa = RSA.Create(2048);
        var key = ServiceAccountKey.FromJson(BuildJson(rsa));

        var handler = new StubHttpMessageHandler((_, _) =>
            StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                $$"""{"access_token":"{{accessToken}}","expires_in":{{expiresIn}},"token_type":"Bearer"}"""));

        var http = new HttpClient(handler);
        var provider = new FcmAccessTokenProvider(key, http, timeProvider);
        return (provider, handler, rsa);
    }
}
