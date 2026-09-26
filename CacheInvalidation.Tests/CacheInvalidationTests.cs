namespace CacheInvalidation.Tests;

public sealed class CacheInvalidationTests(TwoInstancesFixture instances) : IClassFixture<TwoInstancesFixture>
{
    private readonly string _key = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Value_computed_by_one_instance_is_served_by_the_other_from_L2()
    {
        var (fromA, servedByA) = await CacheScenario.GetAsync(instances.ClientA, _key);
        var (fromB, servedByB) = await CacheScenario.GetAsync(instances.ClientB, _key);

        Assert.Equal(servedByA, fromA.GeneratedBy);
        Assert.NotEqual(servedByA, servedByB);
        Assert.Equal(fromA, fromB);
    }

    [Fact]
    public async Task Removing_on_one_instance_evicts_the_L1_of_the_other()
    {
        var (fromA, _) = await CacheScenario.GetAsync(instances.ClientA, _key);
        var (cachedOnB, _) = await CacheScenario.GetAsync(instances.ClientB, _key);
        Assert.Equal(fromA, cachedOnB);

        await CacheScenario.DeleteAsync(instances.ClientA, _key);

        var regeneratedOnB = await CacheScenario.WaitUntilRegeneratedLocallyAsync(instances.ClientB, _key);
        Assert.True(regeneratedOnB.GeneratedAt >= fromA.GeneratedAt);
    }

    [Fact]
    public async Task Expiring_on_one_instance_expires_the_L1_of_the_other()
    {
        var (fromA, _) = await CacheScenario.GetAsync(instances.ClientA, _key);
        await CacheScenario.GetAsync(instances.ClientB, _key);

        await CacheScenario.ExpireAsync(instances.ClientA, _key);

        var regeneratedOnB = await CacheScenario.WaitUntilRegeneratedLocallyAsync(instances.ClientB, _key);
        Assert.True(regeneratedOnB.GeneratedAt >= fromA.GeneratedAt);
    }
}
