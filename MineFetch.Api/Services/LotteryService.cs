using Microsoft.EntityFrameworkCore;
using MineFetch.Api.Data;
using MineFetch.Entities.DTOs;
using MineFetch.Entities.Models;

namespace MineFetch.Api.Services;

/// <summary>
/// 开奖服务 - 处理开奖数据的保存和查询
/// </summary>
public class LotteryService
{
    private readonly ILogger<LotteryService> _logger;
    private readonly AppDbContext _dbContext;
    private readonly RuleEngine _ruleEngine;
    private readonly LotteryCacheService _cacheService;

    public LotteryService(
        ILogger<LotteryService> logger,
        AppDbContext dbContext,
        RuleEngine ruleEngine,
        LotteryCacheService cacheService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _ruleEngine = ruleEngine;
        _cacheService = cacheService;
    }

    /// <summary>
    /// 处理采集端上报的开奖数据
    /// </summary>
    public async Task<LotteryResult?> ReportAsync(LotteryReportDto dto, CancellationToken cancellationToken = default)
    {
        // 检查是否已存在
        var exists = await _dbContext.LotteryResults
            .AnyAsync(r => r.PeriodId == dto.PeriodId, cancellationToken);

        if (exists)
        {
            _logger.LogDebug("期号已存在，跳过: {PeriodId}", dto.PeriodId);
            return null;
        }

        // 确保群组存在
        var group = await _dbContext.TelegramGroups
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId, cancellationToken);

        if (group == null)
        {
            group = new TelegramGroup
            {
                Id = dto.GroupId,
                Title = dto.GroupName ?? $"群组 {dto.GroupId}",
                IsActive = true
            };
            _dbContext.TelegramGroups.Add(group);
        }
        else if (!string.IsNullOrEmpty(dto.GroupName) && group.Title != dto.GroupName)
        {
            group.Title = dto.GroupName;
            group.UpdatedAt = DateTime.UtcNow;
        }

        // 创建开奖记录
        var result = new LotteryResult
        {
            PeriodId = dto.PeriodId,
            DiceNumber = dto.DiceNumber,
            GroupId = dto.GroupId,
            MessageId = dto.MessageId,
            CollectedAt = dto.CollectedAt,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.LotteryResults.Add(result);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("✅ 开奖记录已保存: {Result}", result);

        // 立即添加到缓存
        _cacheService.AddResult(result);

        // 触发规则检查
        await _ruleEngine.CheckRulesAsync(result, cancellationToken);

        return result;
    }

    /// <summary>
    /// 获取开奖历史
    /// </summary>
    public async Task<List<LotteryResult>> GetHistoryAsync(
        long? groupId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.LotteryResults.AsQueryable();

        if (groupId.HasValue)
            query = query.Where(r => r.GroupId == groupId.Value);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public async Task<object> GetStatsAsync(long groupId, int count = 50, CancellationToken cancellationToken = default)
    {
        var results = await _dbContext.LotteryResults
            .Where(r => r.GroupId == groupId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

        if (!results.Any())
            return new { total = 0 };

        var total = results.Count;
        var big = results.Count(r => r.DiceNumber >= 4);
        var small = results.Count(r => r.DiceNumber <= 3);
        var odd = results.Count(r => r.DiceNumber % 2 == 1);
        var even = results.Count(r => r.DiceNumber % 2 == 0);

        return new
        {
            total,
            big,
            small,
            odd,
            even,
            bigRate = Math.Round((double)big / total * 100, 1),
            smallRate = Math.Round((double)small / total * 100, 1),
            oddRate = Math.Round((double)odd / total * 100, 1),
            evenRate = Math.Round((double)even / total * 100, 1)
        };
    }
}
