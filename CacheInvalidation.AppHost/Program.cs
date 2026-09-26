var builder = DistributedApplication.CreateBuilder(args);

// Garnet: Microsoft's Redis-compatible cache server, used both as L2 (distributed cache) and backplane (pub/sub).
var garnet = builder.AddGarnet("garnet", port: 6379);

// Garnet Insight: Redis Insight, pre-connected to Garnet, to browse the L2 entries.
builder.AddContainer("garnet-insight", "redis/redisinsight", "latest")
    .WithHttpEndpoint(targetPort: 5540, name: "http")
    .WithEnvironment("RI_REDIS_HOST", garnet.Resource.Name)
    .WithEnvironment("RI_REDIS_PORT", "6379")
    .WithEnvironment("RI_REDIS_ALIAS", garnet.Resource.Name)
    .WithEnvironment(context =>
    {
        if (garnet.Resource.PasswordParameter is { } password)
        {
            context.EnvironmentVariables["RI_REDIS_PASSWORD"] = password;
        }
    })
    .WithParentRelationship(garnet)
    .WaitFor(garnet);

AddWebApiInstance("webapi1", httpsPort: 5001);
AddWebApiInstance("webapi2", httpsPort: 5002);

builder.Build().Run();

// Each instance is a separate resource with a fixed port, so that requests can target one instance or the other
// (WithReplicas would put them behind a single load-balanced endpoint).
void AddWebApiInstance(string name, int httpsPort) =>
    builder.AddProject<Projects.CacheInvalidation_WebApi>(name)
        .WithHttpsEndpoint(port: httpsPort, name: "https")
        .WithUrlForEndpoint("https", url =>
        {
            url.Url = "/scalar";
            url.DisplayText = "Scalar";
        })
        .WithHttpHealthCheck("/health")
        .WithReference(garnet)
        .WaitFor(garnet);
