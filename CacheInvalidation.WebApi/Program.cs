using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Scalar.AspNetCore;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.NewtonsoftJson;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddLogging();

string redisConnection = Environment.GetEnvironmentVariable("ConnectionStrings__garnet") ?? throw new ArgumentNullException("problem with garnet");
var redisConnectionMultiplexer = ConnectionMultiplexer.Connect(redisConnection);

// TODO : docker pull ghcr.io/microsoft/garnet:latest

// FusionCache = HybridCache but with fail-safe mode + Auto-handling + backplane (cache invalidation propagation)
// fail-safe mode : If redis is down, FusionCache still working with last memory entry instead of returning error
// auto-handling : If a cache entry expires, FusionCache starts a background request to reload it while continuing to serve the old value until the new one is ready.
// backplane : perf improvements with heavy load (batch)

builder.Services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(1),
        DistributedCacheDuration = TimeSpan.FromMinutes(10)
    })
    .WithSerializer(
        new FusionCacheNewtonsoftJsonSerializer()
    )
    .WithMemoryCache(new MemoryCache(new MemoryCacheOptions()))
    .WithDistributedCache(new RedisCache(new RedisCacheOptions
    {
        Configuration = redisConnection
    }))
    .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
    {
        Configuration = redisConnection
    }));


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        // workaround to tell scalar to use host port instead of container port (when launching on docker mode)
        options.Servers = Array.Empty<ScalarServer>();
    });
}

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        context.Response.Redirect("/scalar/v1");
        return;
    }
    await next();
});

app.UseHttpsRedirection();

app.MapGet("data", async (IFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync("my-key", async token =>
    {
        await Task.Delay(3000, token);
        return new { Message = "Coucou" };
    });

    return Results.Ok(data);
});

app.MapDelete("invalidate", async (IFusionCache cache) =>
{
    await cache.RemoveAsync("my-key");
    return Results.Ok("Cache invalidated on all instances");
});

app.Run();