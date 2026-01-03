using System.Text.RegularExpressions;
using MineFetch.Entities.DTOs;
using MineFetch.Entities.Services;
using Serilog;

namespace MineFetch.Collector.Services;

/// <summary>
/// 消息解析器包装类 - 添加日志输出
/// </summary>
public class CollectorMessageParser
{
    private static readonly ILogger Logger = Log.ForContext<CollectorMessageParser>();
    private readonly MessageParser _parser = new();

    /// <summary>
    /// 尝试解析消息，提取开奖信息
    /// </summary>
    public LotteryReportDto? TryParse(string message, long groupId, string groupName, int messageId)
    {
        var result = _parser.TryParse(message, groupId, groupName, messageId);
        
        if (result != null)
        {
            var shortName = MessageParser.GetShortGroupName(groupName);
            Logger.Information("✅ [{GroupName}] 期号={PeriodId}, 骰子={DiceNumber}, 群ID={GroupId}", 
                shortName, result.PeriodId, result.DiceNumber, groupId);
        }
        
        return result;
    }
}
