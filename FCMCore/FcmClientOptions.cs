using System.Text.Json;
using System.Text.Json.Serialization;
using FCMCore.Auth;

namespace FCMCore;

/// <summary>
/// Configuration for an <c>FcmClient</c>.
/// </summary>
public sealed class FcmClientOptions
{
    /// <summary>
    /// The token provider that exchanges your service-account key for a short-lived OAuth2 access
    /// token. Required.
    /// </summary>
    public required FcmAccessTokenProvider TokenProvider { get; init; }

    /// <summary>
    /// Your Firebase / Google Cloud project ID, used in the request path
    /// (<c>/v1/projects/{ProjectId}/messages:send</c>). When <see langword="null"/>, the project ID
    /// parsed from the service-account key (<see cref="FcmAccessTokenProvider.ProjectId"/>) is used.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>The request timeout applied to the underlying <see cref="HttpClient"/>. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The JSON serializer options used for the message body. Defaults to omitting
    /// <see langword="null"/> values and <c>snake_case</c>-ing property names, as the FCM HTTP v1
    /// API expects.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; init; } = CreateDefaultJsonOptions();

    /// <summary>The base address of the FCM HTTP v1 API.</summary>
    public static Uri DefaultEndpoint { get; } = new("https://fcm.googleapis.com");

    /// <summary>Creates the default serializer options (<c>snake_case</c>, null values omitted) used for the message body.</summary>
    public static JsonSerializerOptions CreateDefaultJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
