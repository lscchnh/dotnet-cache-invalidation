using CacheInvalidation.ServiceDefaults;
using CacheInvalidation.WebApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddOptions<SlowDataSourceOptions>()
    .BindConfiguration(SlowDataSourceOptions.SectionName);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SlowDataSource>();

// Registers a single, shared IConnectionMultiplexer (with health check + tracing) and an IDistributedCache (L2) on top of it.
// The "garnet" connection string is injected by the Aspire AppHost.
builder.AddRedisDistributedCache("garnet");

// The backplane reuses that same multiplexer instead of opening its own connection.
builder.Services.AddFusionCacheStackExchangeRedisBackplane();
builder.Services.AddOptions<RedisBackplaneOptions>()
    .Configure<IServiceProvider>((options, sp) =>
        options.ConnectionMultiplexerFactory = () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>()));

// FusionCache = L1 (memory) + L2 (distributed) + backplane.
// - L1: each instance keeps its own in-memory copy for very fast reads.
// - L2: Garnet, shared by every instance, so a value computed once is reused everywhere.
// - Backplane: when an entry is set/removed/expired on one instance, a notification is published (Redis pub/sub)
//   and every other instance evicts its L1 copy. This is what propagates the invalidation.
// - Fail-safe: if the factory fails (e.g. the data source is down), the last known value is served instead of an error.
// - Eager refresh: once an entry reaches 90% of its duration, it is refreshed in the background while the current value keeps being served.
builder.Services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(1),
        DistributedCacheDuration = TimeSpan.FromMinutes(10),
        IsFailSafeEnabled = true,
        FailSafeMaxDuration = TimeSpan.FromHours(2),
        FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
        EagerRefreshThreshold = 0.9f,
    })
    .WithSerializer(new FusionCacheSystemTextJsonSerializer())
    .WithRegisteredDistributedCache()
    .WithRegisteredBackplane();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddFusionCacheInstrumentation())
    .WithMetrics(metrics => metrics.AddFusionCacheInstrumentation());

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        // Behind the Aspire proxy the app listens on an internal port: let Scalar use the browser's origin instead.
        options.Servers = [];
    });
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

app.UseHttpsRedirection();

app.MapDefaultEndpoints();
app.MapDataEndpoints();

app.Run();
