using System.Net;
using System.Net.Http.Json;
using CacheInvalidation.WebApi;

namespace CacheInvalidation.Tests;

/// <summary>
/// The demo scenario, shared by the in-process tests and the end-to-end Aspire tests.
/// </summary>
internal static class CacheScenario
{
    private static readonly TimeSpan PropagationTimeout = TimeSpan.FromSeconds(10);

    public static async Task<(CachedData Data, string ServedBy)> GetAsync(HttpClient client, string key)
    {
        using var response = await client.GetAsync(new Uri($"/data/{key}", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<CachedData>(TestContext.Current.CancellationToken);
        Assert.NotNull(data);
        return (data, Assert.Single(response.Headers.GetValues(DataEndpoints.ServedByHeader)));
    }

    public static async Task DeleteAsync(HttpClient client, string key)
    {
        using var response = await client.DeleteAsync(new Uri($"/data/{key}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task ExpireAsync(HttpClient client, string key)
    {
        using var response = await client.PostAsync(new Uri($"/data/{key}/expire", UriKind.Relative), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Backplane notifications are asynchronous: polls <paramref name="client"/> until it serves a value it generated itself.
    /// </summary>
    public static async Task<CachedData> WaitUntilRegeneratedLocallyAsync(HttpClient client, string key)
    {
        using var timeout = new CancellationTokenSource(PropagationTimeout);
        while (true)
        {
            var (data, servedBy) = await GetAsync(client, key);
            if (data.GeneratedBy == servedBy)
            {
                return data;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }
}
