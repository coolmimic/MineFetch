using MineFetch.Entities.Enums;

namespace MineFetch.Entities.Models;

/// <summary>
/// 开奖结果
/// </summary>
public class LotteryResult
{
    /// <summary>
    /// 主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 期号，如 SL652726900409832
    /// </summary>
    public string PeriodId { get; set; } = string.Empty;

    /// <summary>
    /// 开奖号码（骰子结果 1-6）
    /// </summary>
    public int DiceNumber { get; set; }

    /// <summary>
    /// 大小（自动计算）
    /// </summary>
    public BetType Size => DiceNumber >= 4 ? BetType.Big : BetType.Small;

    /// <summary>
    /// 单双（自动计算）
    /// </summary>
    public BetType Parity => DiceNumber % 2 == 1 ? BetType.Odd : BetType.Even;

    /// <summary>
    /// 来源群组 ID
    /// </summary>
    public long GroupId { get; set; }

    /// <summary>
    /// Telegram 消息 ID
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// 采集时间（UTC）
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 入库时间（UTC）
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 关联的群组
    /// </summary>
    public virtual TelegramGroup? Group { get; set; }

    /// <summary>
    /// 获取结果描述
    /// </summary>
    public string GetDescription()
    {
        return $"{DiceNumber} ({Size.ToChineseName()}/{Parity.ToChineseName()})";
    }

    public override string ToString()
    {
        return $"期号: {PeriodId}, 骰子: {GetDescription()}";
    }
}
