var builder = DistributedApplication.CreateBuilder(args);

var garnet = builder.AddRedis("garnet")
    .WithImage("ghcr.io/microsoft/garnet")
    .WithEndpoint(port: 6379, targetPort: 6379, name: "garnet")
    .WithRedisInsight();

builder.AddProject<Projects.CacheInvalidation_WebApi>("cache-invalidation-webapi1")
    .WithReference(garnet)
    .WithEndpoint(port: 5001, scheme: "https", name: "first-instance");

builder.AddProject<Projects.CacheInvalidation_WebApi>("cache-invalidation-webapi2")
    .WithReference(garnet)
    .WithEndpoint(port: 5002, scheme: "https", name: "second-instance");

builder.Build().Run();
