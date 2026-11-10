using FCMCore;
using FCMCore.Auth;
using FCMService.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FCMService.Tests;

public sealed class AddFcmTests
{
    [Fact]
    public void AddFcm_delegate_registers_IFcmClient_as_a_singleton()
    {
        var keyFile = TestCredentials.WriteTempJson();
        try
        {
            var services = new ServiceCollection();
            services.AddFcm(fcm => fcm.ServiceAccountFilePath = keyFile);

            using var provider = services.BuildServiceProvider();

            var client = provider.GetRequiredService<IFcmClient>();
            Assert.IsType<FcmClient>(client);
            Assert.Same(client, provider.GetRequiredService<IFcmClient>());
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public void AddFcm_registers_the_token_provider_and_settings()
    {
        var keyFile = TestCredentials.WriteTempJson();
        try
        {
            var services = new ServiceCollection();
            services.AddFcm(fcm =>
            {
                fcm.ServiceAccountFilePath = keyFile;
                fcm.TimeoutSeconds = 45;
            });

            using var provider = services.BuildServiceProvider();

            var tokenProvider = provider.GetRequiredService<FcmAccessTokenProvider>();
            Assert.Equal(TestCredentials.ProjectId, tokenProvider.ProjectId);

            var settings = provider.GetRequiredService<FcmSettings>();
            Assert.Equal(keyFile, settings.ServiceAccountFilePath);
            Assert.Equal(45, settings.TimeoutSeconds);
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public void AddFcm_binds_settings_from_configuration()
    {
        var keyFile = TestCredentials.WriteTempJson();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Fcm:ServiceAccountFilePath"] = keyFile,
                    ["Fcm:TimeoutSeconds"] = "45",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddFcm(configuration.GetSection("Fcm"));

            using var provider = services.BuildServiceProvider();

            var settings = provider.GetRequiredService<FcmSettings>();
            Assert.Equal(keyFile, settings.ServiceAccountFilePath);
            Assert.Equal(45, settings.TimeoutSeconds);
            Assert.IsType<FcmClient>(provider.GetRequiredService<IFcmClient>());
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public void AddFcm_throws_when_the_service_account_path_is_missing()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddFcm(fcm => { }));
    }
}
