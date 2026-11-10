using System.Text.Json;
using FCMCore;
using FCMCore.Models;
using Xunit;

namespace FCMService.Tests;

public sealed class FcmMessageSerializationTests
{
    private static readonly JsonSerializerOptions Options = FcmClientOptions.CreateDefaultJsonOptions();

    private static JsonElement Serialize(FcmMessage message) =>
        message.ToJsonNode(Options).Deserialize<JsonElement>(Options);

    [Fact]
    public void Alert_sets_token_target_and_notification()
    {
        var message = FcmMessage.Alert("tok", "Hi", "There");
        var root = Serialize(message);

        Assert.Equal("tok", root.GetProperty("token").GetString());
        var notification = root.GetProperty("notification");
        Assert.Equal("Hi", notification.GetProperty("title").GetString());
        Assert.Equal("There", notification.GetProperty("body").GetString());
        Assert.False(root.TryGetProperty("topic", out _));
        Assert.False(root.TryGetProperty("condition", out _));
    }

    [Fact]
    public void ToTopic_sets_topic_target()
    {
        var root = Serialize(FcmMessage.ToTopic("news", "T", "B"));
        Assert.Equal("news", root.GetProperty("topic").GetString());
        Assert.False(root.TryGetProperty("token", out _));
    }

    [Fact]
    public void ToCondition_sets_condition_target()
    {
        var root = Serialize(FcmMessage.ToCondition("'a' in topics", "T", "B"));
        Assert.Equal("'a' in topics", root.GetProperty("condition").GetString());
    }

    [Fact]
    public void Data_message_has_no_notification_and_high_android_priority()
    {
        var message = FcmMessage.DataMessage("tok", new Dictionary<string, string> { ["k"] = "v" });
        var root = Serialize(message);

        Assert.False(root.TryGetProperty("notification", out _));
        Assert.Equal("v", root.GetProperty("data").GetProperty("k").GetString());
        Assert.Equal("high", root.GetProperty("android").GetProperty("priority").GetString());
    }

    [Fact]
    public void Android_config_uses_snake_case_and_duration_ttl()
    {
        var message = new FcmMessage
        {
            Token = "tok",
            Android = new AndroidConfig
            {
                Priority = AndroidMessagePriority.High,
                TimeToLive = TimeSpan.FromHours(1),
                CollapseKey = "group-1",
                RestrictedPackageName = "com.example.app",
                DirectBootOk = true,
                Notification = new AndroidNotification
                {
                    ChannelId = "chat",
                    ClickAction = "OPEN_CHAT",
                    Color = "#4285F4",
                },
            },
        };

        var android = Serialize(message).GetProperty("android");

        Assert.Equal("high", android.GetProperty("priority").GetString());
        Assert.Equal("3600s", android.GetProperty("ttl").GetString());
        Assert.Equal("group-1", android.GetProperty("collapse_key").GetString());
        Assert.Equal("com.example.app", android.GetProperty("restricted_package_name").GetString());
        Assert.True(android.GetProperty("direct_boot_ok").GetBoolean());

        var notification = android.GetProperty("notification");
        Assert.Equal("chat", notification.GetProperty("channel_id").GetString());
        Assert.Equal("OPEN_CHAT", notification.GetProperty("click_action").GetString());
        Assert.Equal("#4285F4", notification.GetProperty("color").GetString());
    }

    [Fact]
    public void Fractional_ttl_is_formatted_with_a_decimal()
    {
        var message = new FcmMessage
        {
            Token = "tok",
            Android = new AndroidConfig { TimeToLive = TimeSpan.FromMilliseconds(3500) },
        };

        var android = Serialize(message).GetProperty("android");
        Assert.Equal("3.5s", android.GetProperty("ttl").GetString());
    }

    [Fact]
    public void Null_fields_are_omitted()
    {
        var message = new FcmMessage { Token = "tok" };
        var root = Serialize(message);

        Assert.False(root.TryGetProperty("notification", out _));
        Assert.False(root.TryGetProperty("data", out _));
        Assert.False(root.TryGetProperty("android", out _));
    }

    [Fact]
    public void Resolving_target_requires_exactly_one()
    {
        Assert.Throws<InvalidOperationException>(() => new FcmMessage().ResolveTarget());
        Assert.Throws<InvalidOperationException>(() =>
            new FcmMessage { Token = "a", Topic = "b" }.ResolveTarget());
    }
}
