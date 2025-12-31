using System.Text.RegularExpressions;
using MineFetch.Entities.DTOs;
using Serilog;

namespace MineFetch.Collector.Services;

/// <summary>
/// 消息解析器 - 从 Telegram 消息中提取扫雷开奖信息
/// </summary>
public partial class MessageParser
{
    private static readonly ILogger Logger = Log.ForContext<MessageParser>();

    // 匹配期号：SL + 数字
    [GeneratedRegex(@"第(SL\d+)期", RegexOptions.Compiled)]
    private static partial Regex PeriodIdRegex();

    // 匹配骰子号码
    [GeneratedRegex(@"骰子为:\s*(\d)", RegexOptions.Compiled)]
    private static partial Regex DiceNumberRegex();

    // 备选：匹配可能的其他格式
    [GeneratedRegex(@"骰子[：:]\s*(\d)", RegexOptions.Compiled)]
    private static partial Regex DiceNumberAltRegex();

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
        var periodMatch = PeriodIdRegex().Match(message);
        if (!periodMatch.Success)
        {
            Logger.Debug("消息包含关键词但未匹配到期号: {Message}", TruncateMessage(message));
            return null;
        }

        // 提取骰子号码
        var diceMatch = DiceNumberRegex().Match(message);
        if (!diceMatch.Success)
        {
            diceMatch = DiceNumberAltRegex().Match(message);
        }

        if (!diceMatch.Success)
        {
            Logger.Debug("消息包含期号但未匹配到骰子号码: {PeriodId}", periodMatch.Groups[1].Value);
            return null;
        }

        var periodId = periodMatch.Groups[1].Value;
        var diceNumber = int.Parse(diceMatch.Groups[1].Value);

        // 验证骰子号码有效性
        if (diceNumber < 1 || diceNumber > 6)
        {
            Logger.Warning("骰子号码无效: {DiceNumber}, 期号: {PeriodId}", diceNumber, periodId);
            return null;
        }

        var result = new LotteryReportDto
        {
            PeriodId = periodId,
            DiceNumber = diceNumber,
            GroupId = groupId,
            GroupName = groupName,
            MessageId = messageId,
            CollectedAt = DateTime.UtcNow
        };

        // 简化群名显示
        var shortName = GetShortGroupName(groupName);
        Logger.Information("✅ [{GroupName}] 期号={PeriodId}, 骰子={DiceNumber}, 群ID={GroupId}", 
            shortName, result.PeriodId, result.DiceNumber, groupId);
        return result;
    }

    /// <summary>
    /// 获取简化的群组名称
    /// 例如: "xxx公群151xxx" -> "公群151"
    /// </summary>
    private static string GetShortGroupName(string groupName)
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

    /// <summary>
    /// 截断过长的消息用于日志显示
    /// </summary>
    private static string TruncateMessage(string message, int maxLength = 100)
    {
        if (message.Length <= maxLength)
            return message;
        return message[..maxLength] + "...";
    }
}
