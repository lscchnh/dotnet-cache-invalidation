using Microsoft.Extensions.Options;

namespace CacheInvalidation.WebApi;

public sealed class SlowDataSourceOptions
{
    public const string SectionName = "SlowDataSource";

    /// <summary>
    /// Latency simulated on every read, to make cache hits and misses obvious.
    /// </summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Name reported in <see cref="CachedData.GeneratedBy"/>. Defaults to the Aspire resource name, then to the machine name.
    /// </summary>
    public string? InstanceName { get; set; }
}

/// <summary>
/// Stands for an expensive dependency (database, remote API...) whose results are worth caching.
/// </summary>
public sealed class SlowDataSource(IOptions<SlowDataSourceOptions> options, IConfiguration configuration, TimeProvider timeProvider)
{
    public string InstanceName { get; } =
        options.Value.InstanceName ?? configuration["OTEL_SERVICE_NAME"] ?? Environment.MachineName;

    public async Task<CachedData> LoadAsync(string key, CancellationToken cancellationToken)
    {
        await Task.Delay(options.Value.Latency, timeProvider, cancellationToken);

        return new CachedData(key, $"Value of '{key}'", timeProvider.GetUtcNow(), InstanceName);
    }
}

/// <param name="Key">The cache key.</param>
/// <param name="Value">The payload.</param>
/// <param name="GeneratedAt">When the data source produced this value.</param>
/// <param name="GeneratedBy">The instance that called the data source, i.e. the one that had the cache miss.</param>
public sealed record CachedData(string Key, string Value, DateTimeOffset GeneratedAt, string GeneratedBy);
