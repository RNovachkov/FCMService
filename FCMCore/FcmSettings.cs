namespace FCMCore;

/// <summary>
/// Bindable configuration for registering FCM services via dependency injection (for example from a
/// <c>Fcm</c> section of <c>appsettings.json</c>). Mutable, with a parameterless constructor, so it
/// binds directly from <c>Microsoft.Extensions.Configuration</c>.
/// </summary>
public sealed class FcmSettings
{
    /// <summary>
    /// Path to the Google service-account JSON key file on disk (downloaded from the Firebase
    /// console under <i>Project settings → Service accounts → Generate new private key</i>). Required.
    /// </summary>
    public string ServiceAccountFilePath { get; set; } = "";

    /// <summary>
    /// Your Firebase / Google Cloud project ID. Optional — when left empty, the <c>project_id</c>
    /// from the service-account key file is used.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>The request timeout, in seconds, applied to the underlying HTTP client. Defaults to 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
