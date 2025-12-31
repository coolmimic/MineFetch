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
    /// <summary>大单 (5)</summary>
    BigOdd = 5,
    
    /// <summary>大双 (4,6)</summary>
    BigEven = 6,
    
    /// <summary>小单 (1,3)</summary>
    SmallOdd = 7,
    
    /// <summary>小双 (2)</summary>
    SmallEven = 8,

    /// <summary>花龙 - 大小跳或单双跳</summary>
    Dragon = 9
}

public static class BetTypeExtensions
{
    public static bool Matches(this BetType betType, int diceNumber)
    {
        return betType switch
        {
            BetType.Big => diceNumber >= 4,
            BetType.Small => diceNumber <= 3,
            BetType.Odd => diceNumber % 2 == 1,
            BetType.Even => diceNumber % 2 == 0,
            BetType.BigOdd => diceNumber >= 4 && diceNumber % 2 == 1,
            BetType.BigEven => diceNumber >= 4 && diceNumber % 2 == 0,
            BetType.SmallOdd => diceNumber <= 3 && diceNumber % 2 == 1,
            BetType.SmallEven => diceNumber <= 3 && diceNumber % 2 == 0,
            // Dragon 无法通过单期判断，需要在 RuleEngine 中特殊处理
            BetType.Dragon => false,
            _ => false
        };
    }

    public static string ToChineseName(this BetType betType)
    {
        return betType switch
        {
            BetType.Big => "大",
            BetType.Small => "小",
            BetType.Odd => "单",
            BetType.Even => "双",
            BetType.BigOdd => "大单",
            BetType.BigEven => "大双",
            BetType.SmallOdd => "小单",
            BetType.SmallEven => "小双",
            BetType.Dragon => "花龙",
            _ => "未知"
        };
    }
}
