using FCMCore.Models;

namespace FCMCore;

/// <summary>
/// Sends messages to Firebase Cloud Messaging (FCM).
/// </summary>
public interface IFcmClient
{
    /// <summary>
    /// Sends a single message to FCM and returns the result. Implementations do not throw for
    /// FCM-level rejections (invalid token, etc.); inspect <see cref="FcmResponse.IsSuccess"/> or
    /// call <see cref="FcmResponse.EnsureSuccess"/>.
    /// </summary>
    /// <param name="message">The message to deliver.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<FcmResponse> SendAsync(FcmMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends many messages concurrently, returning a result for each in the same order as the input.
    /// Failures are reported per-message rather than throwing. FCM's HTTP v1 API has no multicast
    /// endpoint, so this delivers each message individually over the shared HTTP/2 connection.
    /// </summary>
    /// <param name="messages">The messages to deliver.</param>
    /// <param name="maxConcurrency">The maximum number of in-flight requests. Defaults to 10.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IReadOnlyList<FcmResponse>> SendManyAsync(
        IReadOnlyList<FcmMessage> messages,
        int maxConcurrency = 10,
        CancellationToken cancellationToken = default);
}
