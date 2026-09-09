using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StackExchange.Redis;

namespace ClaimPilot.Infrastructure.Services;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Thin Redis wrapper used for coordination and health checks.</summary>
public interface IRedisCache
{
    ValueTask<bool> IsHealthyAsync(CancellationToken ct = default);
    Task SetStringAsync(string key, string value, TimeSpan? expiry = null);
    Task<string?> GetStringAsync(string key);
    Task<bool> SetIfNotExistsAsync(string key, TimeSpan? expiry = null);
    Task DeleteAsync(string key);
}

public sealed class RedisCache : IRedisCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCache> _logger;

    public RedisCache(IConnectionMultiplexer redis, ILogger<RedisCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async ValueTask<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var pong = await db.PingAsync();
            return pong.TotalMilliseconds >= 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Redis health check failed.");
            return false;
        }
    }

    public async Task SetStringAsync(string key, string value, TimeSpan? expiry = null)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, value, expiry);
    }

    public async Task<string?> GetStringAsync(string key)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<bool> SetIfNotExistsAsync(string key, TimeSpan? expiry = null)
    {
        var db = _redis.GetDatabase();
        return await db.StringSetAsync(key, "1", expiry, When.NotExists);
    }

    public async Task DeleteAsync(string key)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }
}