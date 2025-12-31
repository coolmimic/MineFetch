namespace MineFetch.Entities.DTOs;

/// <summary>
/// 采集端上报开奖结果 DTO
/// </summary>
public class LotteryReportDto
{
    /// <summary>
    /// 期号
    /// </summary>
    public string PeriodId { get; set; } = string.Empty;

    /// <summary>
    /// 骰子号码
    /// </summary>
    public int DiceNumber { get; set; }

    /// <summary>
    /// 群组 ID
    /// </summary>
    public long GroupId { get; set; }

    /// <summary>
    /// 群组名称
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// 消息 ID
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// 采集时间
    /// </summary>
    public DateTime CollectedAt { get; set; }
}
