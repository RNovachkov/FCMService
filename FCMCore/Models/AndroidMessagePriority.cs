namespace FCMCore.Models;

/// <summary>
/// The delivery priority of an Android message, controlling whether FCM wakes a dozing device.
/// </summary>
public enum AndroidMessagePriority
{
    /// <summary>
    /// Normal priority. Delivery may be delayed to conserve the device's battery, and messages are
    /// batched. Appropriate for notifications that are not time-critical.
    /// </summary>
    Normal,

    /// <summary>
    /// High priority. FCM attempts to deliver the message immediately, waking a sleeping device if
    /// necessary. Required to reliably trigger a data message's background handler.
    /// </summary>
    High,
}
