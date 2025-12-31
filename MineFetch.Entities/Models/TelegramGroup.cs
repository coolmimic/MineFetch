namespace MineFetch.Entities.Models;

/// <summary>
/// Telegram 群组
/// </summary>
public class TelegramGroup
{
    /// <summary>
    /// 主键（Telegram Group ID，通常为负数）
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 群组名称
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 群组用户名（用于生成链接，如 @baolu64 中的 baolu64）
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 是否启用采集
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间（UTC）
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    ///该群组的开奖记录
    /// </summary>
    public virtual ICollection<LotteryResult> LotteryResults { get; set; } = new List<LotteryResult>();

    /// <summary>
    /// 订阅该群组的用户设置
    /// </summary>
    public virtual ICollection<UserSetting> UserSettings { get; set; } = new List<UserSetting>();
}
