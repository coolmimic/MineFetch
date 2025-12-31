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
    /// 触发的投注类型
    /// </summary>
    public BetType BetType { get; set; }

    /// <summary>
    /// 连续次数
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

        return $"""
            🎯 扫雷提醒

            群组: {GroupName}
            期号: {PeriodId}

            ⚠️ 【{BetType.ToChineseName()}】{ruleText} {Count} 期！
            当前结果: {DiceNumber} ({sizeText}/{parityText})
            """;
    }
}
