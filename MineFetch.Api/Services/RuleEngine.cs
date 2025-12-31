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

        // 获取订阅该群组的所有用户设置（包括全局规则 GroupId = 0）
        var settings = await _dbContext.UserSettings
            .Include(s => s.User)
            .Include(s => s.Group)
            .Where(s => (s.GroupId == result.GroupId || s.GroupId == 0) && s.IsEnabled)
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
        
        // 1. 常规类型的连开和遗漏（包括组合）
        foreach (BetType betType in Enum.GetValues<BetType>())
        {
            if (betType == BetType.Dragon) continue; // 花龙单独计算

            // 连开
            int consecutive = 0;
            foreach (var r in results)
            {
                if (betType.Matches(r.DiceNumber)) consecutive++;
                else break;
            }
            stats[(RuleType.Consecutive, betType)] = consecutive;

            // 遗漏
            int missing = 0;
            foreach (var r in results)
            {
                if (betType.Matches(r.DiceNumber)) break;
                missing++;
            }
            stats[(RuleType.Missing, betType)] = missing;
        }

        // 2. 花龙（跳龙）计算 - 只算连开
        // 逻辑：检测大小跳或单双跳，取最大值
        int dragonCount = CalculateDragonCount(results);
        stats[(RuleType.Consecutive, BetType.Dragon)] = dragonCount;
        stats[(RuleType.Missing, BetType.Dragon)] = 0; // 花龙暂不计算遗漏

        return stats;
    }

    /// <summary>
    /// 计算花龙连开期数
    /// 定义：连续的单跳（如 大-小-大-小 或 单-双-单-双）
    /// </summary>
    private int CalculateDragonCount(List<LotteryResult> results)
    {
        if (results.Count < 2) return 0;

        // 计算大小花龙
        int bsDragon = 1;
        bool? lastIsBig = IsBig(results[0].DiceNumber);
        for (int i = 1; i < results.Count; i++)
        {
            bool? currentIsBig = IsBig(results[i].DiceNumber);
            if (currentIsBig != lastIsBig) // 发生跳变
            {
                bsDragon++;
                lastIsBig = currentIsBig;
            }
            else
            {
                break; // 连开断了
            }
        }

        // 计算单双花龙
        int oeDragon = 1;
        bool? lastIsOdd = IsOdd(results[0].DiceNumber);
        for (int i = 1; i < results.Count; i++)
        {
            bool? currentIsOdd = IsOdd(results[i].DiceNumber);
            if (currentIsOdd != lastIsOdd) // 发生跳变
            {
                oeDragon++;
                lastIsOdd = currentIsOdd;
            }
            else
            {
                break; // 连开断了
            }
        }

        // 只要满足一种花龙，取最大值
        // 但要注意：如果只有1期，不能叫花龙（没跳），至少要2期才算跳
        // 实际上单期也可以算作花龙的起点，但为了推送意义，阈值通常>1
        return Math.Max(bsDragon, oeDragon);
    }

    private bool IsBig(int n) => n >= 4;
    private bool IsOdd(int n) => n % 2 == 1;

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
