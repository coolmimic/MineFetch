namespace MineFetch.Entities.Enums;

/// <summary>
/// 推送规则类型
/// </summary>
public enum RuleType
{
    /// <summary>遗漏（N 期未出现）</summary>
    Missing = 1,
    
    /// <summary>连开（连续出现 N 次）</summary>
    Consecutive = 2
}

/// <summary>
/// RuleType 扩展方法
/// </summary>
public static class RuleTypeExtensions
{
    /// <summary>
    /// 获取类型的中文名称
    /// </summary>
    public static string ToChineseName(this RuleType ruleType)
    {
        return ruleType switch
        {
            RuleType.Missing => "遗漏",
            RuleType.Consecutive => "连开",
            _ => "未知"
        };
    }
}
