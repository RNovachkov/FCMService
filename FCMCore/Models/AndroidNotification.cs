using System.Text.Json.Serialization;

namespace FCMCore.Models;

/// <summary>
/// Android-specific notification options. Any value set here overrides the matching field of the
/// message's platform-independent <see cref="FcmNotification"/> on Android devices.
/// </summary>
public sealed class AndroidNotification
{
    /// <summary>The notification's title. Overrides <see cref="FcmNotification.Title"/> on Android.</summary>
    public string? Title { get; set; }

    /// <summary>The notification's body. Overrides <see cref="FcmNotification.Body"/> on Android.</summary>
    public string? Body { get; set; }

    /// <summary>The URL of an image to display. Overrides <see cref="FcmNotification.Image"/> on Android.</summary>
    public string? Image { get; set; }

    /// <summary>The notification icon resource (a drawable in your app, e.g. <c>ic_stat_notify</c>).</summary>
    public string? Icon { get; set; }

    /// <summary>The notification's accent color, in <c>#rrggbb</c> format.</summary>
    public string? Color { get; set; }

    /// <summary>The sound to play when the device receives the notification (a resource name, or <c>default</c>).</summary>
    public string? Sound { get; set; }

    /// <summary>An identifier used to replace an existing notification in the drawer with the same tag.</summary>
    public string? Tag { get; set; }

    /// <summary>The action associated with a user tap (an intent action your app registers).</summary>
    [JsonPropertyName("click_action")]
    public string? ClickAction { get; set; }

    /// <summary>
    /// The <c>NotificationChannel</c> ID this notification is posted to (required to be visible on
    /// Android 8.0+ / API level 26 and above; the channel must already exist in the app).
    /// </summary>
    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; set; }

    /// <summary>The key of a string resource in the app used to localize the body text.</summary>
    [JsonPropertyName("body_loc_key")]
    public string? BodyLocKey { get; set; }

    /// <summary>The key of a string resource in the app used to localize the title text.</summary>
    [JsonPropertyName("title_loc_key")]
    public string? TitleLocKey { get; set; }
}
