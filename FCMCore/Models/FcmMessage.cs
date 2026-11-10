using System.Text.Json;
using System.Text.Json.Nodes;

namespace FCMCore.Models;

/// <summary>
/// A single message to deliver through FCM. Exactly one target must be set — a device
/// <see cref="Token"/>, a <see cref="Topic"/>, or a <see cref="Condition"/> — together with any
/// combination of a <see cref="Notification"/>, a <see cref="Data"/> payload, and
/// <see cref="Android"/> overrides.
/// </summary>
/// <remarks>
/// Prefer the static factory methods (<see cref="Alert(string, string, string)"/>,
/// <see cref="DataMessage(string, IDictionary{string, string})"/>, <see cref="ToTopic"/>) for common cases.
/// </remarks>
public sealed class FcmMessage
{
    /// <summary>
    /// The registration token of the single target device. Mutually exclusive with
    /// <see cref="Topic"/> and <see cref="Condition"/>.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The topic to send to (without the <c>/topics/</c> prefix). Every device subscribed to the
    /// topic receives the message. Mutually exclusive with <see cref="Token"/> and <see cref="Condition"/>.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// A boolean condition over topics, for example <c>'news' in topics &amp;&amp; 'sports' in topics</c>.
    /// Mutually exclusive with <see cref="Token"/> and <see cref="Topic"/>.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>The platform-independent notification shown to the user.</summary>
    public FcmNotification? Notification { get; set; }

    /// <summary>
    /// An arbitrary key/value payload delivered to the app. All keys and values must be strings. A
    /// data-only message (no <see cref="Notification"/>) is handled entirely by the app.
    /// </summary>
    public IDictionary<string, string>? Data { get; set; }

    /// <summary>Android-specific delivery options and notification overrides.</summary>
    public AndroidConfig? Android { get; set; }

    /// <summary>
    /// Validates that exactly one target is set, returning it as a <c>(field, value)</c> pair.
    /// </summary>
    /// <exception cref="InvalidOperationException">When zero, or more than one, target is set.</exception>
    public (string Field, string Value) ResolveTarget()
    {
        var count = 0;
        if (Token is not null) count++;
        if (Topic is not null) count++;
        if (Condition is not null) count++;

        if (count != 1)
            throw new InvalidOperationException(
                "An FcmMessage must set exactly one target: Token, Topic, or Condition.");

        if (Token is not null) return ("token", Token);
        if (Topic is not null) return ("topic", Topic);
        return ("condition", Condition!);
    }

    /// <summary>Serializes the message into the <c>message</c> JSON object expected by the FCM HTTP v1 API.</summary>
    public JsonObject ToJsonNode(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (field, value) = ResolveTarget();

        var root = new JsonObject
        {
            [field] = value,
        };

        if (Notification is not null)
            root["notification"] = JsonSerializer.SerializeToNode(Notification, options);

        if (Data is { Count: > 0 })
            root["data"] = JsonSerializer.SerializeToNode(Data, options);

        if (Android is not null)
            root["android"] = JsonSerializer.SerializeToNode(Android, options);

        return root;
    }

    // -- Factory helpers ----------------------------------------------------------------------

    /// <summary>Creates a user-facing notification message to a single device token.</summary>
    public static FcmMessage Alert(string token, string title, string body) =>
        new()
        {
            Token = token,
            Notification = new FcmNotification { Title = title, Body = body },
        };

    /// <summary>
    /// Creates a data-only message to a single device token, handled by the app's background
    /// message handler. Uses high Android priority so the handler is woken reliably.
    /// </summary>
    public static FcmMessage DataMessage(string token, IDictionary<string, string> data) =>
        new()
        {
            Token = token,
            Data = data,
            Android = new AndroidConfig { Priority = AndroidMessagePriority.High },
        };

    /// <summary>Creates a user-facing notification message to every device subscribed to a topic.</summary>
    public static FcmMessage ToTopic(string topic, string title, string body) =>
        new()
        {
            Topic = topic,
            Notification = new FcmNotification { Title = title, Body = body },
        };

    /// <summary>
    /// Creates a user-facing notification message to the devices matching a topic condition, for
    /// example <c>'news' in topics &amp;&amp; 'sports' in topics</c>.
    /// </summary>
    public static FcmMessage ToCondition(string condition, string title, string body) =>
        new()
        {
            Condition = condition,
            Notification = new FcmNotification { Title = title, Body = body },
        };
}
