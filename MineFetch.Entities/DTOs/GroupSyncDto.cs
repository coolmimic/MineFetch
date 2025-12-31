namespace MineFetch.Entities.DTOs;

/// <summary>
/// 群组同步 DTO
/// </summary>
public class GroupSyncDto
{
    /// <summary>
    /// 群组 ID（Telegram Group ID）
    /// </summary>
    public long GroupId { get; set; }

    /// <summary>
    /// 群组名称
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 群组链接
    /// </summary>
    public string? GroupLink { get; set; }
}
