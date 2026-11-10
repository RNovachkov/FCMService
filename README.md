# FCMService

A lightweight .NET 10 library for sending Android push notifications through **Firebase Cloud Messaging (FCM)** using the **HTTP v1 API** and service-account (OAuth2) authentication.

- **Service-account auth** — signs an RS256 JWT from your service-account key, exchanges it for a short-lived OAuth2 access token, and caches it within Google's refresh window.
- **Android-first payloads** — a platform-independent notification plus a full `android` override block (priority, TTL, collapse key, channel, icon, color, sound…) and arbitrary data messages.
- **Three targets** — send to a device token, a topic, or a topic condition.
- **Long-lived and thread-safe** — reuses one HTTP/2 connection and one cached access token across sends.
- **First-party dependencies only** — just the .NET base class library and the `Microsoft.Extensions.*` abstractions used by the optional `AddFcm()` helper.

## Install

```bash
dotnet add package FCMService
```

## Prerequisites

From the [Firebase console](https://console.firebase.google.com/) you need a service-account key:

| Value | Where it comes from |
| --- | --- |
| **Service-account JSON** | *Project settings → Service accounts → Generate new private key*. Download it once and keep it secret. It contains `client_email`, `private_key`, `token_uri`, and `project_id`. |
| **Project ID** | The `project_id` inside that file (used automatically; you can override it). |
| **Registration token** | Obtained on the device from the Firebase SDK (for example `FirebaseMessaging.getToken()`). |

## Quick start

```csharp
using FCMService;
using FCMCore;
using FCMCore.Auth;
using FCMCore.Models;

// 1. Create an access-token provider from your service-account key (reuse this; it caches the token).
using var tokenProvider = FcmAccessTokenProvider.FromServiceAccountFile(
    "/secure/service-account.json");

// 2. Configure and create a client (long-lived — create once and reuse).
var options = new FcmClientOptions
{
    TokenProvider = tokenProvider,
    // ProjectId is taken from the service-account key automatically; set it here to override.
};
using var client = new FcmClient(options);

// 3. Send a notification.
var message = FcmMessage.Alert(
    token: "<device registration token>",
    title: "Hello",
    body:  "World");

FcmResponse response = await client.SendAsync(message);

if (response.IsSuccess)
    Console.WriteLine($"Delivered. name = {response.MessageName}");
else
    Console.WriteLine($"Rejected ({response.StatusCode}): {response.ErrorCode} — {response.Reason}");
```

`SendAsync` does **not** throw when FCM rejects a message — inspect `response.IsSuccess` /
`response.ErrorCode`, or call `response.EnsureSuccess()` to turn a rejection into an `FcmException`.

## Handling responses and pruning dead tokens

When a device uninstalls your app (or the token otherwise expires), FCM reports the token as
unregistered. Remove such tokens from your database so you stop sending to them:

```csharp
var response = await client.SendAsync(message);

if (response.ShouldRemoveToken)
{
    // UNREGISTERED, SENDER_ID_MISMATCH, or HTTP 404 — the token is permanently invalid.
    await tokenStore.RemoveAsync(message.Token!);
}
```

`FcmResponse` exposes `IsSuccess`, `StatusCode`, `MessageName`, `ErrorStatus`, `ErrorCode`, `Reason`,
and the convenience `ShouldRemoveToken`.

## Sending to many devices

FCM's HTTP v1 API has no multicast endpoint, so this sends each message individually over the shared
HTTP/2 connection with bounded concurrency:

```csharp
var messages = tokens
    .Select(t => FcmMessage.Alert(t, "Sale!", "50% off today only"))
    .ToList();

IReadOnlyList<FcmResponse> results =
    await client.SendManyAsync(messages, maxConcurrency: 20);
```

Results are returned in the same order as the input; each is reported individually (no throwing).

## Targets and data messages

Factory helpers cover the common cases. Each returns an `FcmMessage` you can further customize.

```csharp
// A single device.
var toDevice = FcmMessage.Alert(token, "Hi", "There");

// Every device subscribed to a topic.
var toTopic = FcmMessage.ToTopic("news", "Breaking", "Something happened");

// A boolean condition over topics.
var conditional = FcmMessage.ToCondition("'news' in topics && 'sports' in topics", "Hi", "There");

// A data-only message handled by the app's background handler (high priority by default).
var data = FcmMessage.DataMessage(token, new Dictionary<string, string>
{
    ["type"] = "sync",
    ["id"]   = "42",
});
```

## Building a message by hand

```csharp
var message = new FcmMessage
{
    Token = token,
    Notification = new FcmNotification { Title = "Title", Body = "Body", Image = "https://…/img.png" },
    Data = new Dictionary<string, string> { ["deepLink"] = "app://chat/42" },
    Android = new AndroidConfig
    {
        Priority    = AndroidMessagePriority.High,
        TimeToLive  = TimeSpan.FromHours(1),
        CollapseKey = "chat-42",
        Notification = new AndroidNotification
        {
            ChannelId   = "chat",
            Icon        = "ic_stat_chat",
            Color       = "#4285F4",
            Sound       = "default",
            ClickAction = "OPEN_CHAT",
        },
    },
};
```

## Dependency injection

The quickest way to wire everything up is `AddFcm()`, which registers `IFcmClient` (and its
`FcmAccessTokenProvider`) as singletons:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddFcm(fcm =>
{
    fcm.ServiceAccountFilePath = "/secure/service-account.json";
    // fcm.ProjectId = "my-project";   // optional; defaults to the key's project_id
});
```

Then inject `IFcmClient` wherever you need it:

```csharp
public sealed class NotificationService(IFcmClient fcm)
{
    public Task<FcmResponse> PingAsync(string token) =>
        fcm.SendAsync(FcmMessage.Alert(token, "Hi", "There"));
}
```

### Binding from configuration

Bind the settings from an `Fcm` section of `appsettings.json`:

```jsonc
// appsettings.json
{
  "Fcm": {
    "ServiceAccountFilePath": "/secure/service-account.json",
    "ProjectId": "",
    "TimeoutSeconds": 30
  }
}
```

```csharp
builder.Services.AddFcm(builder.Configuration.GetSection("Fcm"));
```

### Manual registration

If you prefer to construct the client yourself:

```csharp
builder.Services.AddSingleton(_ =>
    FcmAccessTokenProvider.FromServiceAccountFile("/secure/service-account.json"));

builder.Services.AddSingleton<IFcmClient>(sp => new FcmClient(new FcmClientOptions
{
    TokenProvider = sp.GetRequiredService<FcmAccessTokenProvider>(),
}));
```

### Using `IHttpClientFactory`

Pass your own `HttpClient` (configure it for HTTP/2); the client will not dispose it:

```csharp
using System.Net;

builder.Services.AddHttpClient("fcm", http =>
{
    http.BaseAddress          = FcmClientOptions.DefaultEndpoint;
    http.DefaultRequestVersion = HttpVersion.Version20;
    http.DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrLower;
});

builder.Services.AddSingleton(_ =>
    FcmAccessTokenProvider.FromServiceAccountFile("/secure/service-account.json"));

builder.Services.AddSingleton<IFcmClient>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("fcm");
    var options = new FcmClientOptions { TokenProvider = sp.GetRequiredService<FcmAccessTokenProvider>() };
    return new FcmClient(options, http, ownsHttpClient: false);
});
```

## Authentication

FCM requires a two-step exchange, which `FcmAccessTokenProvider` handles for you:

1. It signs a short-lived JWT assertion (RS256) with the service account's private key.
2. It POSTs that assertion to Google's OAuth2 token endpoint (`token_uri`) to receive an access token.
3. That access token (valid for about an hour) authorizes each `messages:send` call.

The token is fetched lazily and reused until shortly before it expires.

## Targeting and the send endpoint

Every message is delivered to a single host, `https://fcm.googleapis.com`, at the path
`/v1/projects/{projectId}/messages:send`. The project ID is read from the service-account key, or you
can override it with `FcmClientOptions.ProjectId`. Exactly one of `Token`, `Topic`, or `Condition`
must be set on each `FcmMessage`.

## Notes

- **Reuse the client.** Create one `FcmClient` per project and keep it for the lifetime of your app so
  the HTTP/2 connection and cached access token are shared.
- **Thread safety.** `FcmAccessTokenProvider`, `FcmClient`, and `SendManyAsync` are safe to use concurrently.
- **Token refresh.** The provider fetches an access token lazily and reuses it until ~60 seconds
  before it expires.
- **Keep the key secret.** Never commit the service-account JSON file to source control.

## License

Licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
