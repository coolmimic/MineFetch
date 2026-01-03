using System.Text.RegularExpressions;
using MineFetch.Entities.DTOs;

namespace MineFetch.Entities.Services;

/// <summary>
/// 消息解析器 - 从 Telegram 消息中提取扫雷开奖信息
/// </summary>
public partial class MessageParser
{
    // 匹配期号：SL + 数字
    private static readonly Regex PeriodIdPattern = new(@"第(SL\d+)期", RegexOptions.Compiled);

    // 匹配骰子号码
    private static readonly Regex DiceNumberPattern = new(@"骰子为:\s*(\d)", RegexOptions.Compiled);

    // 备选：匹配可能的其他格式
    private static readonly Regex DiceNumberAltPattern = new(@"骰子[：:]\s*(\d)", RegexOptions.Compiled);

    /// <summary>
    /// 尝试解析消息，提取开奖信息
    /// </summary>
    /// <param name="message">原始消息文本</param>
    /// <param name="groupId">群组 ID</param>
    /// <param name="groupName">群组名称</param>
    /// <param name="messageId">消息 ID</param>
    /// <returns>解析成功返回 LotteryReportDto，否则返回 null</returns>
    public LotteryReportDto? TryParse(string message, long groupId, string groupName, int messageId)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        // 检查是否包含关键词，快速过滤
        if (!message.Contains("期") || !message.Contains("骰子"))
            return null;

        // 提取期号
        var periodMatch = PeriodIdPattern.Match(message);
        if (!periodMatch.Success)
            return null;

        // 提取骰子号码
        var diceMatch = DiceNumberPattern.Match(message);
        if (!diceMatch.Success)
        {
            diceMatch = DiceNumberAltPattern.Match(message);
        }

        if (!diceMatch.Success)
            return null;

        var periodId = periodMatch.Groups[1].Value;
        var diceNumber = int.Parse(diceMatch.Groups[1].Value);

        // 验证骰子号码有效性
        if (diceNumber < 1 || diceNumber > 6)
            return null;

        return new LotteryReportDto
        {
            PeriodId = periodId,
            DiceNumber = diceNumber,
            GroupId = groupId,
            GroupName = groupName,
            MessageId = messageId,
            CollectedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 获取简化的群组名称
    /// 例如: "xxx公群151xxx" -> "公群151"
    /// </summary>
    public static string GetShortGroupName(string groupName)
    {
        // 匹配 "公群" 后面跟着的数字
        var match = Regex.Match(groupName, @"公群(\d+)");
        if (match.Success)
        {
            return $"公群{match.Groups[1].Value}";
        }
        
        // 匹配 "扫雷" 后面跟着的数字
        match = Regex.Match(groupName, @"扫雷(\d+)");
        if (match.Success)
        {
            return $"扫雷{match.Groups[1].Value}";
        }
        
        // 如果群名过长，截断显示
        if (groupName.Length > 15)
        {
            return groupName[..12] + "...";
        }
        
        return groupName;
    }
}
