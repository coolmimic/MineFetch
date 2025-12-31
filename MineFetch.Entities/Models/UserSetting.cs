using MineFetch.Entities.Enums;

namespace MineFetch.Entities.Models;

/// <summary>
/// 用户推送设置
/// </summary>
public class UserSetting
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 用户 ID
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// 监控的群组 ID
    /// </summary>
    public long GroupId { get; set; }

    /// <summary>
    /// 规则类型（遗漏/连开）
    /// </summary>
    public RuleType RuleType { get; set; }

    /// <summary>
    /// 投注类型（大/小/单/双）
    /// </summary>
    public BetType BetType { get; set; }

    /// <summary>
    /// 触发阈值（连续 N 次）
    /// </summary>
    public int Threshold { get; set; } = 5;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间（UTC）
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 关联的用户
    /// </summary>
    public virtual User? User { get; set; }

    /// <summary>
    /// 关联的群组
    /// </summary>
    public virtual TelegramGroup? Group { get; set; }

    /// <summary>
    /// 获取规则描述
    /// </summary>
    public string GetDescription()
    {
        var ruleDesc = RuleType == RuleType.Missing ? "遗漏" : "连开";
        return $"【{BetType.ToChineseName()}】{ruleDesc} {Threshold} 期";
    }
}
