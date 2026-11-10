namespace FCMCore;

/// <summary>
/// Thrown when FCM rejects a message and the caller requested that failures throw (for example via
/// <see cref="FcmResponse.EnsureSuccess"/>).
/// </summary>
public sealed class FcmException : Exception
{
    /// <summary>Creates the exception from a failed <see cref="FcmResponse"/>.</summary>
    public FcmException(FcmResponse response)
        : base($"FCM rejected the message with status {response.StatusCode}" +
               ((response.ErrorCode ?? response.ErrorStatus) is { } code ? $" ({code})." : "."))
    {
        Response = response;
    }

    /// <summary>The failed response returned by FCM.</summary>
    public FcmResponse Response { get; }
}
