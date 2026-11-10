using System.Globalization;
using System.Text.Json.Serialization;

namespace FCMCore.Models;

/// <summary>
/// Android-specific options for a message: delivery priority, time-to-live, collapsing, and the
/// Android notification override.
/// </summary>
public sealed class AndroidConfig
{
    /// <summary>
    /// An identifier of a group of messages that can be collapsed, so that only the last message is
    /// sent when delivery can be resumed. A maximum of four distinct collapse keys is allowed at any
    /// given time.
    /// </summary>
    [JsonPropertyName("collapse_key")]
    public string? CollapseKey { get; set; }

    /// <summary>The message's delivery priority. When <see langword="null"/>, FCM uses its default.</summary>
    [JsonIgnore]
    public AndroidMessagePriority? Priority { get; set; }

    /// <summary>The serialized value of <c>android.priority</c> (<c>"normal"</c> or <c>"high"</c>).</summary>
    [JsonInclude]
    [JsonPropertyName("priority")]
    internal string? PriorityValue => Priority switch
    {
        AndroidMessagePriority.High => "high",
        AndroidMessagePriority.Normal => "normal",
        _ => null,
    };

    /// <summary>
    /// How long (up to 4 weeks) the message should be kept in FCM storage if the device is offline.
    /// When <see langword="null"/>, FCM uses its default of 4 weeks.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>The serialized value of <c>android.ttl</c>, a duration string such as <c>"3600s"</c>.</summary>
    [JsonInclude]
    [JsonPropertyName("ttl")]
    internal string? TimeToLiveValue =>
        TimeToLive is { } ttl
            ? string.Create(CultureInfo.InvariantCulture, $"{ttl.TotalSeconds:0.#########}s")
            : null;

    /// <summary>The package name of the application where the registration tokens must match to receive the message.</summary>
    [JsonPropertyName("restricted_package_name")]
    public string? RestrictedPackageName { get; set; }

    /// <summary>
    /// Android-specific data payload. If present, it overrides the message-level
    /// <see cref="FcmMessage.Data"/> on Android. Values must be strings.
    /// </summary>
    [JsonPropertyName("data")]
    public IDictionary<string, string>? Data { get; set; }

    /// <summary>The Android notification override.</summary>
    [JsonPropertyName("notification")]
    public AndroidNotification? Notification { get; set; }

    /// <summary>
    /// When <see langword="true"/>, allows the message to be delivered while the device is in direct
    /// boot mode (before the user has unlocked it after a restart).
    /// </summary>
    [JsonPropertyName("direct_boot_ok")]
    public bool? DirectBootOk { get; set; }
}
