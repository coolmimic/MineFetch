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
    /// 群组 ID
    /// </summary>
    public long GroupId { get; set; }

    /// <summary>
    /// 群组用户名（用于生成链接）
    /// </summary>
    public string? GroupUsername { get; set; }

    /// <summary>
    /// 群组链接（如 https://t.me/yundinghuyule1）
    /// </summary>
    public string? GroupLink { get; set; }

    /// <summary>
    /// 消息 ID（用于生成链接）
    /// </summary>
    public long MessageId { get; set; }

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
    /// 最近的开奖记录（用于显示）
    /// </summary>
    public List<int> RecentNumbers { get; set; } = new();

    /// <summary>
    /// 连续次数（单个触发条件时使用）
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 采集时间
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 生成推送消息文本
    /// </summary>
    public string ToMessageText()
    {
        var timeStr = CollectedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
        
        // 确定类型描述
        string typeDesc = "长龙";
        if (TriggeredBetTypes.Any())
        {
            var maxCount = TriggeredBetTypes.Max(t => t.Count);
            var mainType = TriggeredBetTypes.First(t => t.Count == maxCount);
            
            // 判断是否是跳龙
            if (mainType.Type == BetType.Dragon)
            {
                typeDesc = "花龙";
            }
            else
            {
                typeDesc = mainType.Type.ToChineseName();
            }
        }

        // 连续期数
        var maxPeriods = TriggeredBetTypes.Any() ? TriggeredBetTypes.Max(t => t.Count) : Count;

        // 最近记录
        var recentStr = RecentNumbers.Any() 
            ? string.Join(" ", RecentNumbers.Take(maxPeriods).Select(n => n.ToString()))
            : "";

        // 生成群链接(带消息ID)
        var groupLink = "";
        if (!string.IsNullOrEmpty(GroupLink) && MessageId > 0)
        {
            var username = GroupLink.Replace("https://t.me/", "");
            groupLink = $"\n链接：https://t.me/{username}/{MessageId}";
        }
        else if (!string.IsNullOrEmpty(GroupLink))
        {
            groupLink = $"\n链接：{GroupLink}";
        }

        return $"""
            云顶互娱
            时间：{timeStr}
            群组：{GroupName}
            类型：{typeDesc}
            连续：{maxPeriods} 把
            最近{maxPeriods}把：{recentStr}{groupLink}
            """;
    }
}
