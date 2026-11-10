namespace FCMCore.Models;

/// <summary>
/// The platform-independent notification shown to the user. On Android these values are used unless
/// overridden by an <see cref="AndroidNotification"/> on the message's <see cref="AndroidConfig"/>.
/// </summary>
public sealed class FcmNotification
{
    /// <summary>The notification's title.</summary>
    public string? Title { get; set; }

    /// <summary>The notification's body text.</summary>
    public string? Body { get; set; }

    /// <summary>The URL of an image to display in the notification.</summary>
    public string? Image { get; set; }
}
