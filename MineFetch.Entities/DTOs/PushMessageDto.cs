using MineFetch.Entities.Enums;

namespace MineFetch.Entities.DTOs;

/// <summary>
/// 推送消息 DTO
/// </summary>
public class PushMessageDto
{
    /// <summary>
    /// 目标用户 Chat ID
    /// </summary>
    public long ChatId { get; set; }

    /// <summary>
    /// 群组名称
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// 期号
    /// </summary>
    public string PeriodId { get; set; } = string.Empty;

    /// <summary>
    /// 当前骰子号码
    /// </summary>
    public int DiceNumber { get; set; }

    /// <summary>
    /// 触发的规则类型
    /// </summary>
    public RuleType RuleType { get; set; }

    /// <summary>
    /// 规则分类（Basic/Combo/Dragon）
    /// </summary>
    public string RuleCategory { get; set; } = "Basic";

    /// <summary>
    /// 触发的投注类型（已弃用，保留兼容性）
    /// </summary>
    [Obsolete("Use TriggeredBetTypes instead")]
    public BetType BetType { get; set; }

    /// <summary>
    /// 触发的多个投注类型
    /// </summary>
    public List<(BetType Type, int Count)> TriggeredBetTypes { get; set; } = new();

    /// <summary>
    /// 连续次数（单个触发条件时使用）
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 生成推送消息文本
    /// </summary>
    public string ToMessageText()
    {
        var sizeText = DiceNumber >= 4 ? "大" : "小";
        var parityText = DiceNumber % 2 == 1 ? "单" : "双";
        var ruleText = RuleType == RuleType.Missing ? "已遗漏" : "已连开";

        var categoryDesc = RuleCategory switch
        {
            "Basic" => "大小单双",
            "Combo" => "组合",
            "Dragon" => "花龙",
            _ => RuleCategory
        };

        // 如果有多个触发条件，列出所有
        string triggerDetails;
        if (TriggeredBetTypes.Any())
        {
            var triggers = TriggeredBetTypes
                .Select(t => $"{t.Type.ToChineseName()} ({t.Count}期)")
                .ToList();
            triggerDetails = $"⚠️ 【{categoryDesc}】{ruleText}：\n   " + string.Join("\n   ", triggers);
        }
        else
        {
            // 兼容旧格式
#pragma warning disable CS0618
            triggerDetails = $"⚠️ 【{BetType.ToChineseName()}】{ruleText} {Count} 期！";
#pragma warning restore CS0618
        }

        return $"""
            🎯 扫雷提醒

            群组: {GroupName}
            期号: {PeriodId}

            {triggerDetails}
            当前结果: {DiceNumber} ({sizeText}/{parityText})
            """;
    }
}
