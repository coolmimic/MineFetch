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
    private readonly LotteryCacheService _cacheService;

    public RuleEngine(
        ILogger<RuleEngine> logger,
        AppDbContext dbContext,
        PushService pushService,
        LotteryCacheService cacheService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _pushService = pushService;
        _cacheService = cacheService;
    }

    /// <summary>
    /// 检查新开奖结果是否触发任何规则
    /// </summary>
    public async Task CheckRulesAsync(LotteryResult result, CancellationToken cancellationToken = default)
    {
        // 优先从缓存获取该群组最近的开奖记录
        var recentResults = _cacheService.GetRecentResults(result.GroupId);
        
        // 如果缓存未命中，从数据库加载并初始化缓存
        if (recentResults == null || recentResults.Count < 2)
        {
            _logger.LogDebug("缓存未命中，从数据库加载群组 {GroupId} 的历史记录", result.GroupId);
            
            recentResults = await _dbContext.LotteryResults
                .Where(r => r.GroupId == result.GroupId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(50)
                .ToListAsync(cancellationToken);

            if (recentResults.Count >= 2)
            {
                _cacheService.InitializeCache(result.GroupId, recentResults);
            }
        }

        if (recentResults.Count < 2)
        {
            _logger.LogDebug("历史记录不足，跳过规则检查");
            return;
        }

        // 获取群信息
        var group = await _dbContext.TelegramGroups
            .FirstOrDefaultAsync(g => g.Id == result.GroupId, cancellationToken);
        
        if (group == null)
        {
            _logger.LogDebug("群组不存在，跳过规则检查: {GroupId}", result.GroupId);
            return; // 新群组暂时没有用户订阅，正常跳过
        }

        // 获取所有启用的用户设置（全局监控）
        var settings = await _dbContext.UserSettings
            .Include(s => s.User)
            .Where(s => s.GroupId == null && s.IsEnabled)
            .ToListAsync(cancellationToken);

        if (!settings.Any())
        {
            _logger.LogDebug("没有用户启用监控");
            return;
        }

        // 计算各类型的统计数据
        var stats = CalculateStats(recentResults);

        // 为每个用户检测所有玩法
        foreach (var setting in settings)
        {
            var chatId = setting.User?.ChatId ?? 0;
            if (chatId == 0) continue;

            // 检测所有玩法（使用统一阈值）
            var triggered = CheckAllPlayTypes(setting.Threshold, stats, result, group, recentResults, chatId);
            
            if (triggered != null && triggered.TriggeredBetTypes.Any())
            {
                await _pushService.SendPushAsync(triggered, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 检测所有玩法类型
    /// </summary>
    private PushMessageDto? CheckAllPlayTypes(
        int threshold,
        Dictionary<(RuleType, BetType), int> stats,
        LotteryResult result,
        TelegramGroup group,
        List<LotteryResult> recentResults,
        long chatId)
    {
        var allBetTypes = new[] 
        { 
            BetType.Big, BetType.Small, BetType.Odd, BetType.Even,
            BetType.BigOdd, BetType.BigEven, BetType.SmallOdd, BetType.SmallEven,
            BetType.Dragon
        };

        var triggered = new List<(BetType Type, int Count)>();

        // 检测连开
        foreach (var betType in allBetTypes)
        {
            var key = (RuleType.Consecutive, betType);
            if (stats.TryGetValue(key, out var count) && count >= threshold)
            {
                triggered.Add((betType, count));
            }
        }

        if (!triggered.Any())
            return null;

        // 获取最大连续期数
        var maxCount = triggered.Max(t => t.Count);

        // 提取最近的开奖号码
        var recentNumbers = recentResults
            .OrderByDescending(r => r.CollectedAt)
            .Take(maxCount)
            .Select(r => r.DiceNumber)
            .Reverse()
            .ToList();

        _logger.LogInformation(
            "触发推送: 群组={GroupName}, 阈值={Threshold}, 触发数={Count}",
            group.Title, threshold, triggered.Count);

        return new PushMessageDto
        {
            ChatId = chatId,
            GroupId = result.GroupId,
            GroupName = group.Title ?? $"群组 {result.GroupId}",
            GroupUsername = group.Username,
            GroupLink = group.GroupLink,
            MessageId = result.MessageId,
            PeriodId = result.PeriodId,
            DiceNumber = result.DiceNumber,
            RuleType = RuleType.Consecutive,
            RuleCategory = "All",
            TriggeredBetTypes = triggered,
            RecentNumbers = recentNumbers,
            CollectedAt = result.CollectedAt
        };
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
    /// 检查规则组是否触发（新版，支持多条件合并）
    /// </summary>
    private PushMessageDto? CheckRuleGroup(
        string category,
        RuleType ruleType,
        int threshold,
        Dictionary<(RuleType, BetType), int> stats,
        LotteryResult result,
        string groupName,
        long chatId)
    {
        // 根据 category 获取需要检测的 BetTypes
        var betTypesToCheck = category switch
        {
            "Basic" => new[] { BetType.Big, BetType.Small, BetType.Odd, BetType.Even },
            "Combo" => new[] { BetType.BigOdd, BetType.BigEven, BetType.SmallOdd, BetType.SmallEven },
            "Dragon" => new[] { BetType.Dragon },
            _ => Array.Empty<BetType>()
        };

        // 检测所有触发的条件
        var triggeredTypes = new List<(BetType Type, int Count)>();

        foreach (var betType in betTypesToCheck)
        {
            var key = (ruleType, betType);
            if (stats.TryGetValue(key, out var count) && count >= threshold)
            {
                triggeredTypes.Add((betType, count));
            }
        }

        // 如果没有触发，返回 null
        if (!triggeredTypes.Any())
            return null;

        // 触发规则，记录日志
        _logger.LogInformation(
            "规则组触发: 分类={Category}, 类型={RuleType}, 阈值={Threshold}, 触发项={Triggers}",
            category, ruleType, threshold, string.Join(", ", triggeredTypes.Select(t => $"{t.Type}({t.Count})")));

        return new PushMessageDto
        {
            ChatId = chatId,
            GroupName = groupName,
            PeriodId = result.PeriodId,
            DiceNumber = result.DiceNumber,
            RuleType = ruleType,
            RuleCategory = category,
            TriggeredBetTypes = triggeredTypes
        };
    }

    /// <summary>
    /// 检查单个设置是否触发（已弃用，保留兼容性）
    /// </summary>
    [Obsolete("Use CheckRuleGroup instead")]
    private PushMessageDto? CheckSetting(
        UserSetting setting, 
        Dictionary<(RuleType, BetType), int> stats,
        LotteryResult result)
    {
#pragma warning disable CS0618
        var key = (setting.RuleType, setting.BetType);
#pragma warning restore CS0618
        if (!stats.TryGetValue(key, out var count))
            return null;

        if (count < setting.Threshold)
            return null;

        // 触发规则
        _logger.LogInformation(
            "规则触发: 用户={UserId}, 规则={RuleType} {BetType} >= {Threshold}, 实际={Count}",
            setting.UserId, setting.RuleType, setting.BetType, setting.Threshold, count);

#pragma warning disable CS0618
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
#pragma warning restore CS0618
    }
}
