namespace MineFetch.Entities.Models;

/// <summary>
/// 用户
/// </summary>
public class User
{
    /// <summary>
    /// 主键（Telegram User ID）
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Telegram 用户名（可选）
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 名字
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// 姓氏（可选）
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// 私聊 Chat ID（用于推送消息）
    /// </summary>
    public long ChatId { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 语言代码
    /// </summary>
    public string? LanguageCode { get; set; }

    /// <summary>
    /// 注册时间（UTC）
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活跃时间（UTC）
    /// </summary>
    public DateTime? LastActiveAt { get; set; }

    /// <summary>
    /// 用户的推送设置
    /// </summary>
    public virtual ICollection<UserSetting> Settings { get; set; } = new List<UserSetting>();

    /// <summary>
    /// 获取显示名称
    /// </summary>
    public string DisplayName => Username ?? $"{FirstName} {LastName}".Trim();
}
