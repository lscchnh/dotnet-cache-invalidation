using ZiggyCreatures.Caching.Fusion;

namespace CacheInvalidation.WebApi;

public static class DataEndpoints
{
    public const string ServedByHeader = "X-Served-By";

    public static IEndpointRouteBuilder MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/data/{key}")
            .WithTags("Data")
            .AddEndpointFilter(async (context, next) =>
            {
                var source = context.HttpContext.RequestServices.GetRequiredService<SlowDataSource>();
                context.HttpContext.Response.Headers[ServedByHeader] = source.InstanceName;
                return await next(context);
            });

        group.MapGet("/", async (string key, IFusionCache cache, SlowDataSource source, CancellationToken cancellationToken) =>
        {
            var data = await cache.GetOrSetAsync<CachedData>(
                key,
                (_, token) => source.LoadAsync(key, token),
                token: cancellationToken);

            return TypedResults.Ok(data);
        })
        .WithSummary("Reads a value through L1 → L2 → data source.")
        .WithDescription("Compare GeneratedBy with the X-Served-By header to tell whether the value was computed locally or by another instance.");

        group.MapDelete("/", async (string key, IFusionCache cache, CancellationToken cancellationToken) =>
        {
            await cache.RemoveAsync(key, token: cancellationToken);
            return TypedResults.NoContent();
        })
        .WithSummary("Removes a value from L1, L2 and, through the backplane, from the L1 of every other instance.");

        group.MapPost("/expire", async (string key, IFusionCache cache, CancellationToken cancellationToken) =>
        {
            await cache.ExpireAsync(key, token: cancellationToken);
            return TypedResults.NoContent();
        })
        .WithSummary("Marks a value as expired everywhere, but keeps it as a fail-safe fallback if the data source fails.");

        return app;
    }
}
