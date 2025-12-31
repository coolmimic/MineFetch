namespace MineFetch.Entities.Enums;

/// <summary>
/// 投注类型
/// </summary>
public enum BetType
{
    /// <summary>大 (4,5,6)</summary>
    Big = 1,
    
    /// <summary>小 (1,2,3)</summary>
    Small = 2,
    
    /// <summary>单 (1,3,5)</summary>
    Odd = 3,
    
    /// <summary>双 (2,4,6)</summary>
    Even = 4
}

/// <summary>
/// BetType 扩展方法
/// </summary>
public static class BetTypeExtensions
{
    /// <summary>
    /// 判断骰子号码是否匹配该投注类型
    /// </summary>
    public static bool Matches(this BetType betType, int diceNumber)
    {
        return betType switch
        {
            BetType.Big => diceNumber >= 4 && diceNumber <= 6,
            BetType.Small => diceNumber >= 1 && diceNumber <= 3,
            BetType.Odd => diceNumber % 2 == 1,
            BetType.Even => diceNumber % 2 == 0,
            _ => false
        };
    }

    /// <summary>
    /// 获取类型的中文名称
    /// </summary>
    public static string ToChineseName(this BetType betType)
    {
        return betType switch
        {
            BetType.Big => "大",
            BetType.Small => "小",
            BetType.Odd => "单",
            BetType.Even => "双",
            _ => "未知"
        };
    }
}
