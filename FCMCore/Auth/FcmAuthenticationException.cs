namespace FCMCore.Auth;

/// <summary>
/// Thrown when a Google OAuth2 access token cannot be obtained for FCM — for example when the
/// service-account key is invalid or the token endpoint rejects the assertion.
/// </summary>
public sealed class FcmAuthenticationException : Exception
{
    /// <summary>Creates the exception with a message and optional inner exception.</summary>
    public FcmAuthenticationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
