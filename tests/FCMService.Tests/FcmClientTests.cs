using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FCMCore;
using FCMCore.Auth;
using FCMCore.Models;
using FCMService.Tests.Helpers;
using Xunit;

namespace FCMService.Tests;

public sealed class FcmClientTests
{
    private static (FcmClient Client, StubHttpMessageHandler Handler, FcmAccessTokenProvider Provider, RSA Key) CreateClient(
        Func<HttpRequestMessage, string?, HttpResponseMessage> responder,
        string? projectId = null)
    {
        var (provider, _, key) = TestCredentials.NewProvider();
        var handler = new StubHttpMessageHandler(responder);
        var http = new HttpClient(handler);

        var client = new FcmClient(
            new FcmClientOptions
            {
                TokenProvider = provider,
                ProjectId = projectId,
            },
            http,
            ownsHttpClient: true);

        return (client, handler, provider, key);
    }

    [Fact]
    public async Task SendAsync_posts_to_the_messages_send_path()
    {
        var (client, handler, provider, key) = CreateClient((_, _) => StubHttpMessageHandler.Ok());
        using (client)
        using (provider)
        using (key)
        {
            await client.SendAsync(FcmMessage.Alert("tok", "T", "B"));

            var request = handler.LastRequest;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("fcm.googleapis.com", request.RequestUri!.Host);
            Assert.Equal("/v1/projects/test-project/messages:send", request.RequestUri.AbsolutePath);
        }
    }

    [Fact]
    public async Task SendAsync_sets_the_bearer_authorization_header()
    {
        var (client, handler, provider, key) = CreateClient((_, _) => StubHttpMessageHandler.Ok());
        using (client)
        using (provider)
        using (key)
        {
            await client.SendAsync(FcmMessage.Alert("tok", "T", "B"));

            var auth = handler.LastRequest.Headers.TryGetValues("authorization", out var values)
                ? values.FirstOrDefault()
                : null;
            Assert.Equal("Bearer ya29.test-token", auth);
            Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        }
    }

    [Fact]
    public async Task SendAsync_wraps_the_message_in_the_envelope()
    {
        var (client, handler, provider, key) = CreateClient((_, _) => StubHttpMessageHandler.Ok());
        using (client)
        using (provider)
        using (key)
        {
            await client.SendAsync(FcmMessage.Alert("tok", "Hi", "There"));

            using var doc = JsonDocument.Parse(handler.LastBody!);
            var message = doc.RootElement.GetProperty("message");
            Assert.Equal("tok", message.GetProperty("token").GetString());
            Assert.Equal("Hi", message.GetProperty("notification").GetProperty("title").GetString());
            Assert.Equal("There", message.GetProperty("notification").GetProperty("body").GetString());
        }
    }

    [Fact]
    public async Task SendAsync_uses_the_explicit_project_id_over_the_key()
    {
        var (client, handler, provider, key) = CreateClient((_, _) => StubHttpMessageHandler.Ok(), projectId: "other-project");
        using (client)
        using (provider)
        using (key)
        {
            await client.SendAsync(FcmMessage.Alert("tok", "T", "B"));
            Assert.Equal("/v1/projects/other-project/messages:send", handler.LastRequest.RequestUri!.AbsolutePath);
        }
    }

    [Fact]
    public async Task SendAsync_throws_when_no_target_is_set()
    {
        var (client, _, provider, key) = CreateClient((_, _) => StubHttpMessageHandler.Ok());
        using (client)
        using (provider)
        using (key)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new FcmMessage()));
        }
    }

    [Fact]
    public async Task SendAsync_returns_success_with_message_name()
    {
        var (client, _, provider, key) = CreateClient((_, _) =>
            StubHttpMessageHandler.Ok("projects/test-project/messages/0:123"));
        using (client)
        using (provider)
        using (key)
        {
            var result = await client.SendAsync(FcmMessage.Alert("tok", "T", "B"));

            Assert.True(result.IsSuccess);
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("projects/test-project/messages/0:123", result.MessageName);
            Assert.Null(result.ErrorCode);
        }
    }

    [Fact]
    public async Task SendAsync_parses_unregistered_error_and_flags_token_for_removal()
    {
        const string body = """
            {"error":{"code":404,"message":"Requested entity was not found.","status":"NOT_FOUND",
            "details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}
            """;
        var (client, _, provider, key) = CreateClient((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.NotFound, body));
        using (client)
        using (provider)
        using (key)
        {
            var result = await client.SendAsync(FcmMessage.Alert("tok", "T", "B"));

            Assert.False(result.IsSuccess);
            Assert.Equal(404, result.StatusCode);
            Assert.Equal("NOT_FOUND", result.ErrorStatus);
            Assert.Equal("UNREGISTERED", result.ErrorCode);
            Assert.Equal("Requested entity was not found.", result.Reason);
            Assert.True(result.ShouldRemoveToken);
        }
    }

    [Fact]
    public async Task SendAsync_does_not_flag_a_generic_invalid_argument_for_removal()
    {
        const string body = """
            {"error":{"code":400,"message":"Invalid value at 'message.android.ttl'.","status":"INVALID_ARGUMENT",
            "details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"INVALID_ARGUMENT"}]}}
            """;
        var (client, _, provider, key) = CreateClient((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));
        using (client)
        using (provider)
        using (key)
        {
            var result = await client.SendAsync(FcmMessage.Alert("tok", "T", "B"));

            Assert.False(result.IsSuccess);
            Assert.Equal(400, result.StatusCode);
            Assert.Equal("INVALID_ARGUMENT", result.ErrorCode);
            Assert.False(result.ShouldRemoveToken);
        }
    }

    [Fact]
    public async Task SendManyAsync_returns_one_result_per_message_in_order()
    {
        // Echo each message's token into the returned message name so we can assert ordering.
        var (client, _, provider, key) = CreateClient((_, body) =>
        {
            using var doc = JsonDocument.Parse(body!);
            var token = doc.RootElement.GetProperty("message").GetProperty("token").GetString();
            return StubHttpMessageHandler.Ok($"projects/test-project/messages/{token}");
        });

        using (client)
        using (provider)
        using (key)
        {
            var messages = Enumerable.Range(0, 25)
                .Select(i => FcmMessage.Alert($"token-{i}", "T", "B"))
                .ToList();

            var results = await client.SendManyAsync(messages, maxConcurrency: 8);

            Assert.Equal(25, results.Count);
            for (var i = 0; i < results.Count; i++)
            {
                Assert.True(results[i].IsSuccess);
                Assert.Equal($"projects/test-project/messages/token-{i}", results[i].MessageName);
            }
        }
    }
}
