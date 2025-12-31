using Microsoft.Extensions.Caching.Memory;
using MineFetch.Entities.Models;

namespace MineFetch.Api.Services;

/// <summary>
/// 开奖数据缓存服务 - 优化性能，减少数据库查询
/// </summary>
public class LotteryCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<LotteryCacheService> _logger;
    private const int MaxHistoryPerGroup = 50; // 每个群最多缓存的历史记录数
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(2); // 缓存过期时间

    public LotteryCacheService(IMemoryCache cache, ILogger<LotteryCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 添加新开奖记录到缓存
    /// </summary>
    public void AddResult(LotteryResult result)
    {
        var cacheKey = GetCacheKey(result.GroupId);
        
        // 获取或创建该群组的缓存列表
        var history = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            _logger.LogDebug("为群组 {GroupId} 创建新的缓存", result.GroupId);
            return new List<LotteryResult>();
        });

        if (history == null)
        {
            history = new List<LotteryResult>();
        }

        // 添加到列表开头（最新的在前面）
        history.Insert(0, result);

        // 限制缓存大小
        if (history.Count > MaxHistoryPerGroup)
        {
            history.RemoveRange(MaxHistoryPerGroup, history.Count - MaxHistoryPerGroup);
        }

        // 更新缓存
        _cache.Set(cacheKey, history, CacheExpiration);
        
        _logger.LogDebug("已缓存群组 {GroupId} 的开奖记录，当前缓存数量: {Count}", 
            result.GroupId, history.Count);
    }

    /// <summary>
    /// 获取群组的历史开奖记录（从缓存）
    /// </summary>
    public List<LotteryResult>? GetRecentResults(long groupId, int count = 50)
    {
        var cacheKey = GetCacheKey(groupId);
        var history = _cache.Get<List<LotteryResult>>(cacheKey);
        
        if (history == null || !history.Any())
        {
            _logger.LogDebug("群组 {GroupId} 的缓存为空", groupId);
            return null;
        }

        var result = history.Take(count).ToList();
        _logger.LogDebug("从缓存获取群组 {GroupId} 的 {Count} 条记录", groupId, result.Count);
        return result;
    }

    /// <summary>
    /// 批量初始化群组缓存（从数据库加载）
    /// </summary>
    public void InitializeCache(long groupId, List<LotteryResult> results)
    {
        if (!results.Any()) return;

        var cacheKey = GetCacheKey(groupId);
        var toCache = results.OrderByDescending(r => r.CreatedAt).Take(MaxHistoryPerGroup).ToList();
        
        _cache.Set(cacheKey, toCache, CacheExpiration);
        
        _logger.LogInformation("已初始化群组 {GroupId} 的缓存，记录数: {Count}", groupId, toCache.Count);
    }

    /// <summary>
    /// 清除指定群组的缓存
    /// </summary>
    public void ClearGroupCache(long groupId)
    {
        var cacheKey = GetCacheKey(groupId);
        _cache.Remove(cacheKey);
        _logger.LogDebug("已清除群组 {GroupId} 的缓存", groupId);
    }

    private static string GetCacheKey(long groupId) => $"lottery_history_{groupId}";
}
