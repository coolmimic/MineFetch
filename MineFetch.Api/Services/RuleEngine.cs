using Microsoft.EntityFrameworkCore;
using MineFetch.Api.Data;
using MineFetch.Entities.DTOs;
using MineFetch.Entities.Enums;
using MineFetch.Entities.Models;

namespace MineFetch.Api.Services;

/// <summary>
/// 规则引擎 - 检测遗漏/连开并触发推送
/// </summary>
public class RuleEngine
{
    private readonly ILogger<RuleEngine> _logger;
    private readonly AppDbContext _dbContext;
    private readonly PushService _pushService;

    public RuleEngine(
        ILogger<RuleEngine> logger,
        AppDbContext dbContext,
        PushService pushService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _pushService = pushService;
    }

    /// <summary>
    /// 检查新开奖结果是否触发任何规则
    /// </summary>
    public async Task CheckRulesAsync(LotteryResult result, CancellationToken cancellationToken = default)
    {
        // 获取该群组最近的开奖记录（用于计算连续次数）
        var recentResults = await _dbContext.LotteryResults
            .Where(r => r.GroupId == result.GroupId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (recentResults.Count < 2)
        {
            _logger.LogDebug("历史记录不足，跳过规则检查");
            return;
        }

        // 获取订阅该群组的所有用户设置
        var settings = await _dbContext.UserSettings
            .Include(s => s.User)
            .Include(s => s.Group)
            .Where(s => s.GroupId == result.GroupId && s.IsEnabled)
            .ToListAsync(cancellationToken);

        if (!settings.Any())
        {
            _logger.LogDebug("没有用户订阅该群组: {GroupId}", result.GroupId);
            return;
        }

        // 计算各类型的统计数据
        var stats = CalculateStats(recentResults);

        // 检查每个用户设置
        foreach (var setting in settings)
        {
            var triggered = CheckSetting(setting, stats, result);
            if (triggered != null)
            {
                await _pushService.SendPushAsync(triggered, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 计算统计数据
    /// </summary>
    private Dictionary<(RuleType, BetType), int> CalculateStats(List<LotteryResult> results)
    {
        var stats = new Dictionary<(RuleType, BetType), int>();
        
        // 初始化所有组合
        foreach (BetType betType in Enum.GetValues<BetType>())
        {
            stats[(RuleType.Consecutive, betType)] = 0;
            stats[(RuleType.Missing, betType)] = 0;
        }

        if (!results.Any())
            return stats;

        // 计算连开次数（从最新开始）
        foreach (BetType betType in Enum.GetValues<BetType>())
        {
            int consecutive = 0;
            foreach (var r in results)
            {
                if (betType.Matches(r.DiceNumber))
                    consecutive++;
                else
                    break;
            }
            stats[(RuleType.Consecutive, betType)] = consecutive;
        }

        // 计算遗漏次数（从最新开始，直到出现该类型）
        foreach (BetType betType in Enum.GetValues<BetType>())
        {
            int missing = 0;
            foreach (var r in results)
            {
                if (betType.Matches(r.DiceNumber))
                    break;
                missing++;
            }
            stats[(RuleType.Missing, betType)] = missing;
        }

        return stats;
    }

    /// <summary>
    /// 检查单个设置是否触发
    /// </summary>
    private PushMessageDto? CheckSetting(
        UserSetting setting, 
        Dictionary<(RuleType, BetType), int> stats,
        LotteryResult result)
    {
        var key = (setting.RuleType, setting.BetType);
        if (!stats.TryGetValue(key, out var count))
            return null;

        if (count < setting.Threshold)
            return null;

        // 触发规则
        _logger.LogInformation(
            "规则触发: 用户={UserId}, 规则={RuleType} {BetType} >= {Threshold}, 实际={Count}",
            setting.UserId, setting.RuleType, setting.BetType, setting.Threshold, count);

        return new PushMessageDto
        {
            ChatId = setting.User!.ChatId,
            GroupName = setting.Group?.Title ?? "未知群组",
            PeriodId = result.PeriodId,
            DiceNumber = result.DiceNumber,
            RuleType = setting.RuleType,
            BetType = setting.BetType,
            Count = count
        };
    }
}
