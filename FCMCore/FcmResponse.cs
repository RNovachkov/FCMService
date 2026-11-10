namespace FCMCore;

/// <summary>
/// The result of attempting to send a message to Firebase Cloud Messaging (FCM).
/// </summary>
public sealed record FcmResponse
{
    /// <summary><see langword="true"/> when FCM accepted the message (HTTP 200).</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>The HTTP status code returned by FCM.</summary>
    public required int StatusCode { get; init; }

    /// <summary>
    /// On success, the server-assigned message name from the response body, of the form
    /// <c>projects/{project_id}/messages/{message_id}</c>. <see langword="null"/> on failure.
    /// </summary>
    public string? MessageName { get; init; }

    /// <summary>
    /// On failure, the canonical Google API error status from the response body (for example
    /// <c>NOT_FOUND</c>, <c>INVALID_ARGUMENT</c> or <c>UNAVAILABLE</c>). <see langword="null"/> on success.
    /// </summary>
    public string? ErrorStatus { get; init; }

    /// <summary>
    /// On failure, the FCM-specific error code from <c>error.details[].errorCode</c> (for example
    /// <c>UNREGISTERED</c>, <c>SENDER_ID_MISMATCH</c> or <c>THIRD_PARTY_AUTH_ERROR</c>). This is the
    /// most precise signal for how to react; <see langword="null"/> when FCM did not supply one.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// On failure, the human-readable <c>error.message</c> string from the response body.
    /// <see langword="null"/> on success.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// <see langword="true"/> when the response indicates the registration token is permanently
    /// invalid and should be removed from your database. This is the case for a
    /// <c>UNREGISTERED</c> error code (the app was uninstalled or the token expired), a
    /// <c>SENDER_ID_MISMATCH</c> (the token belongs to a different Firebase project), or an
    /// HTTP 404.
    /// </summary>
    public bool ShouldRemoveToken =>
        StatusCode == 404 ||
        string.Equals(ErrorCode, "UNREGISTERED", StringComparison.Ordinal) ||
        string.Equals(ErrorCode, "SENDER_ID_MISMATCH", StringComparison.Ordinal);

    /// <summary>Throws an <see cref="FcmException"/> when <see cref="IsSuccess"/> is <see langword="false"/>.</summary>
    public FcmResponse EnsureSuccess()
    {
        if (!IsSuccess)
            throw new FcmException(this);
        return this;
    }
}
