using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FCMCore.Auth;
using FCMService.Tests.Helpers;
using Xunit;

namespace FCMService.Tests;

public sealed class FcmAccessTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_returns_the_access_token_from_the_endpoint()
    {
        var (provider, handler, key) = TestCredentials.NewProvider(accessToken: "ya29.abc");
        using (provider)
        using (key)
        {
            var token = await provider.GetTokenAsync();

            Assert.Equal("ya29.abc", token);
            Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
            Assert.Equal(TestCredentials.TokenUri, handler.LastRequest.RequestUri!.ToString());
        }
    }

    [Fact]
    public async Task GetTokenAsync_signs_an_rs256_jwt_bearer_assertion()
    {
        var (provider, handler, key) = TestCredentials.NewProvider();
        using (provider)
        using (key)
        {
            await provider.GetTokenAsync();

            var form = handler.LastBody!;
            Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer", form);

            var assertion = ExtractFormValue(form, "assertion");
            var parts = assertion.Split('.');
            Assert.Equal(3, parts.Length);

            var header = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[0]));
            Assert.Equal("RS256", header.GetProperty("alg").GetString());
            Assert.Equal("JWT", header.GetProperty("typ").GetString());

            var claims = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1]));
            Assert.Equal(TestCredentials.ClientEmail, claims.GetProperty("iss").GetString());
            Assert.Equal(FcmAccessTokenProvider.Scope, claims.GetProperty("scope").GetString());
            Assert.Equal(TestCredentials.TokenUri, claims.GetProperty("aud").GetString());
            Assert.Equal(3600, claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64());

            // The signature must verify against the service account's key.
            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64Url.DecodeFromChars(parts[2]);
            Assert.True(key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
    }

    [Fact]
    public async Task GetTokenAsync_caches_the_token_within_its_lifetime()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var (provider, handler, key) = TestCredentials.NewProvider(expiresIn: 3600, timeProvider: clock);
        using (provider)
        using (key)
        {
            await provider.GetTokenAsync();
            clock.Advance(TimeSpan.FromMinutes(30));
            await provider.GetTokenAsync();

            Assert.Single(handler.Requests);
        }
    }

    [Fact]
    public async Task GetTokenAsync_refreshes_after_the_token_expires()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var (provider, handler, key) = TestCredentials.NewProvider(expiresIn: 3600, timeProvider: clock);
        using (provider)
        using (key)
        {
            await provider.GetTokenAsync();
            clock.Advance(TimeSpan.FromMinutes(60)); // past expiry (minus the 60s skew)
            await provider.GetTokenAsync();

            Assert.Equal(2, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task GetTokenAsync_throws_when_the_endpoint_fails()
    {
        var rsa = RSA.Create(2048);
        var key = ServiceAccountKey.FromJson(TestCredentials.BuildJson(rsa));
        var handler = new StubHttpMessageHandler((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}"""));
        var http = new HttpClient(handler);

        using var provider = new FcmAccessTokenProvider(key, http);
        using (rsa)
        {
            var ex = await Assert.ThrowsAsync<FcmAuthenticationException>(() => provider.GetTokenAsync());
            Assert.Contains("401", ex.Message);
        }
    }

    [Fact]
    public void ProjectId_is_parsed_from_the_service_account_key()
    {
        var (provider, _, key) = TestCredentials.NewProvider();
        using (provider)
        using (key)
        {
            Assert.Equal(TestCredentials.ProjectId, provider.ProjectId);
        }
    }

    private static string ExtractFormValue(string form, string key)
    {
        foreach (var pair in form.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair[..separator] == key)
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        throw new InvalidOperationException($"Form value '{key}' not found.");
    }
}
