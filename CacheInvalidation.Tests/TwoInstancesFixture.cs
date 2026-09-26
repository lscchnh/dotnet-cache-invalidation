using CacheInvalidation.WebApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.Memory;

namespace CacheInvalidation.Tests;

/// <summary>
/// Runs two in-process instances of the WebApi. Garnet is replaced by in-memory equivalents shared by both instances:
/// a single <see cref="MemoryDistributedCache"/> as L2 and a <see cref="MemoryBackplane"/> on the same connection id.
/// No container runtime is needed.
/// </summary>
/// <remarks>
/// <see cref="SlowDataSource"/> only identifies the WebApi assembly: <c>Program</c> would be ambiguous with the AppHost one.
/// </remarks>
public sealed class TwoInstancesFixture : IAsyncDisposable
{
    private readonly SharedDistributedCache _sharedL2 = new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
    private readonly string _backplaneConnectionId = Guid.NewGuid().ToString("N");
    private readonly WebApplicationFactory<SlowDataSource> _instanceA;
    private readonly WebApplicationFactory<SlowDataSource> _instanceB;

    public TwoInstancesFixture()
    {
        _instanceA = CreateInstance("instance-a");
        _instanceB = CreateInstance("instance-b");
        ClientA = _instanceA.CreateClient();
        ClientB = _instanceB.CreateClient();
    }

    public HttpClient ClientA { get; }

    public HttpClient ClientB { get; }

    private WebApplicationFactory<SlowDataSource> CreateInstance(string name) =>
        new WebApplicationFactory<SlowDataSource>().WithWebHostBuilder(builder =>
        {
            // Never used (the L2 and the backplane are replaced below), but required by AddRedisDistributedCache.
            builder.UseSetting("ConnectionStrings:garnet", "localhost:1,abortConnect=false");
            builder.UseSetting("Aspire:StackExchange:Redis:DisableHealthChecks", "true");
            builder.UseSetting("Aspire:StackExchange:Redis:DisableTracing", "true");
            builder.UseSetting("SlowDataSource:Latency", "00:00:00");
            builder.UseSetting("SlowDataSource:InstanceName", name);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                services.AddSingleton<IDistributedCache>(_sharedL2);

                services.RemoveAll<IFusionCacheBackplane>();
                services.AddSingleton<IFusionCacheBackplane>(
                    new MemoryBackplane(new MemoryBackplaneOptions { ConnectionId = _backplaneConnectionId }));
            });
        });

    public async ValueTask DisposeAsync()
    {
        ClientA.Dispose();
        ClientB.Dispose();
        await _instanceA.DisposeAsync();
        await _instanceB.DisposeAsync();
    }

    /// <summary>
    /// FusionCache deliberately ignores a registered <see cref="MemoryDistributedCache"/> (it would be useless as a real L2):
    /// hiding it behind a plain <see cref="IDistributedCache"/> makes it usable as a shared L2 between the two instances.
    /// </summary>
    private sealed class SharedDistributedCache(IDistributedCache inner) : IDistributedCache
    {
        public byte[]? Get(string key) => inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            inner.SetAsync(key, value, options, token);

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
    }
}
