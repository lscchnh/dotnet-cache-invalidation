using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CacheInvalidation.Tests;

/// <summary>
/// End-to-end tests against the real AppHost: two WebApi instances and a Garnet container.
/// Skipped when no container runtime (Docker or Podman) is available.
/// </summary>
public sealed class AppHostTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Invalidation_propagates_between_instances_through_Garnet()
    {
        Assert.SkipUnless(ContainerRuntime.IsAvailable, "Requires Docker or Podman.");
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CacheInvalidation_AppHost>(cancellationToken);
        appHost.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        await using var app = await appHost.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("webapi1", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("webapi2", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        using var client1 = app.CreateHttpClient("webapi1", "https");
        using var client2 = app.CreateHttpClient("webapi2", "https");
        var key = Guid.NewGuid().ToString("N");

        var (from1, _) = await CacheScenario.GetAsync(client1, key);
        var (cachedOn2, _) = await CacheScenario.GetAsync(client2, key);
        Assert.Equal(from1, cachedOn2);

        await CacheScenario.DeleteAsync(client1, key);

        var regeneratedOn2 = await CacheScenario.WaitUntilRegeneratedLocallyAsync(client2, key);
        Assert.Equal("webapi2", regeneratedOn2.GeneratedBy);
    }
}

internal static class ContainerRuntime
{
    public static bool IsAvailable { get; } = IsOnPath("docker") || IsOnPath("podman");

    private static bool IsOnPath(string executable)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd" } : [string.Empty];
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => extensions.Select(extension => Path.Combine(directory, executable + extension)))
            .Any(File.Exists);
    }
}
