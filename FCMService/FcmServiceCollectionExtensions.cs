using FCMCore;
using FCMCore.Auth;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency-injection extensions for registering FCM services.
/// </summary>
public static class FcmServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IFcmClient"/> (and its supporting <see cref="FcmAccessTokenProvider"/>)
    /// as singletons, configured by the supplied delegate.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">A delegate that populates the <see cref="FcmSettings"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    public static IServiceCollection AddFcm(this IServiceCollection services, Action<FcmSettings> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var settings = new FcmSettings();
        configure(settings);
        return AddFcmCore(services, settings);
    }

    /// <summary>
    /// Registers an <see cref="IFcmClient"/> (and its supporting <see cref="FcmAccessTokenProvider"/>)
    /// as singletons, bound from a configuration section (for example <c>Configuration.GetSection("Fcm")</c>).
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration section to bind an <see cref="FcmSettings"/> from.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    public static IServiceCollection AddFcm(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new FcmSettings();
        configuration.Bind(settings);
        return AddFcmCore(services, settings);
    }

    private static IServiceCollection AddFcmCore(IServiceCollection services, FcmSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ServiceAccountFilePath, "FcmSettings.ServiceAccountFilePath");

        services.AddSingleton(settings);

        // The provider and client are thread-safe and long-lived; the container disposes both.
        services.AddSingleton(_ =>
            FcmAccessTokenProvider.FromServiceAccountFile(settings.ServiceAccountFilePath));

        services.AddSingleton<IFcmClient>(sp => new FCMService.FcmClient(new FcmClientOptions
        {
            TokenProvider = sp.GetRequiredService<FcmAccessTokenProvider>(),
            ProjectId = string.IsNullOrWhiteSpace(settings.ProjectId) ? null : settings.ProjectId,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
        }));

        return services;
    }
}
