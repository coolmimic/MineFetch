using Microsoft.EntityFrameworkCore;
using MineFetch.Api.Data;
using MineFetch.Entities.Enums;
using MineFetch.Entities.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = MineFetch.Entities.Models.User;

namespace MineFetch.Api.Services;

/// <summary>
/// Telegram Bot 服务 - 处理 Webhook 更新和用户命令
/// </summary>
public class TelegramBotService
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly AppDbContext _dbContext;

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        ITelegramBotClient botClient,
        AppDbContext dbContext)
    {
        _logger = logger;
        _botClient = botClient;
        _dbContext = dbContext;
    }

    /// <summary>
    /// 处理 Webhook 更新
    /// </summary>
    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken = default)
    {
        try
        {
            if (update.Message is { } message)
            {
                await HandleMessageAsync(message, cancellationToken);
            }
            else if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(callbackQuery, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理更新时发生异常");
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.Text is not { } text)
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;

        _logger.LogInformation("收到消息: ChatId={ChatId}, UserId={UserId}, Text={Text}", 
            chatId, userId, text);

        // 确保用户已注册
        await EnsureUserExistsAsync(message.From!, chatId, cancellationToken);

        // 处理命令
        var command = text.Split(' ')[0].ToLower();
        var args = text.Length > command.Length ? text[(command.Length + 1)..].Trim() : "";

        switch (command)
        {
            case "/start":
                await HandleStartAsync(chatId, cancellationToken);
                break;
            case "/help":
                await HandleHelpAsync(chatId, cancellationToken);
                break;
            case "/settings":
            case "/list":
                await HandleListSettingsAsync(userId, chatId, cancellationToken);
                break;
            case "/add":
                // 兼容旧的参数输入方式，如果带参数则尝试解析，否则显示菜单
                if (!string.IsNullOrEmpty(args))
                {
                    await HandleManualAddAsync(userId, chatId, args, cancellationToken);
                }
                else
                {
                    await HandleAddSettingAsync(userId, chatId, args, cancellationToken);
                }
                break;
            case "/del":
                await HandleDeleteSettingAsync(userId, chatId, args, cancellationToken);
                break;
            default:
                break;
        }
    }

    private async Task HandleStartAsync(long chatId, CancellationToken cancellationToken)
    {
        var text = """
            👋 欢迎使用扫雷数据采集助手！

            🤖 我会自动监控所有群组的开奖结果。

            📋 常用命令：
            /add - 添加推送规则（按钮操作）
            /list - 查看我的规则
            /del - 删除规则
            /help - 查看帮助文档
            """;

        await _botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }

    private async Task HandleHelpAsync(long chatId, CancellationToken cancellationToken)
    {
        var text = """
            📖 使用帮助

            1️⃣ **添加规则**
            发送 /add 命令，通过按钮选择玩法、类型和期数。
            - 遗漏：连续 N 期未出现
            - 连开：连续出现 N 期

            2️⃣ **管理规则**
            发送 /list 查看已添加的规则及其 ID。
            发送 `/del ID` 删除对应规则。

            💡 **玩法说明**
            🔴 大 (4-6) | 🔵 小 (1-3)
            🟢 单 (1,3,5) | 🟡 双 (2,4,6)
            """;

        await _botClient.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 手动添加规则 (兼容自定义输入用)
    /// Args: Big Consecutive 10
    /// </summary>
    private async Task HandleManualAddAsync(long userId, long chatId, string args, CancellationToken cancellationToken)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // /add Big Consecutive 10
        if (parts.Length < 3)
        {
             await _botClient.SendMessage(chatId, "⚠️ 格式错误，建议直接发送 /add 使用按钮添加", cancellationToken: cancellationToken);
             return;
        }

        if (int.TryParse(parts[2], out var threshold))
        {
            try 
            {
                await SaveRuleAsync(userId, chatId, parts[0], parts[1], threshold, cancellationToken);
            }
            catch
            {
                await _botClient.SendMessage(chatId, "⚠️ 参数无效", cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleDeleteSettingAsync(long userId, long chatId, string args, CancellationToken cancellationToken)
    {
        if (!int.TryParse(args.Trim(), out var settingId))
        {
            await _botClient.SendMessage(chatId, "❌ 请提供有效的规则 ID\n\n使用 /list 查看规则 ID",
                cancellationToken: cancellationToken);
            return;
        }

        var setting = await _dbContext.UserSettings
            .FirstOrDefaultAsync(s => s.Id == settingId && s.UserId == userId, cancellationToken);

        if (setting == null)
        {
            await _botClient.SendMessage(chatId, "❌ 规则不存在", cancellationToken: cancellationToken);
            return;
        }

        _dbContext.UserSettings.Remove(setting);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(chatId, "✅ 规则已删除", cancellationToken: cancellationToken);
    }

    private async Task HandleListSettingsAsync(long userId, long chatId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.UserSettings
            .Include(s => s.Group)
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        if (!settings.Any())
        {
            await _botClient.SendMessage(chatId, 
                "📭 你还没有设置任何推送规则\n\n使用 /add 添加规则", 
                cancellationToken: cancellationToken);
            return;
        }

        var lines = new List<string> { "📋 我的推送规则：", "" };
        foreach (var s in settings)
        {
            var status = s.IsEnabled ? "✅" : "❌";
            lines.Add($"{status} [ID:{s.Id}] {s.Group?.Title ?? "未知群组"}");
            lines.Add($"   {s.GetDescription()}");
            lines.Add("");
        }

        lines.Add("使用 /del ID 删除规则");

        await _botClient.SendMessage(chatId, string.Join("\n", lines), cancellationToken: cancellationToken);
    }

    private async Task HandleAddSettingAsync(long userId, long chatId, string args, CancellationToken cancellationToken)
    {
        // 步骤 1: 选择玩法
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔴 大 (4-6)", "step1_Big"),
                InlineKeyboardButton.WithCallbackData("🔵 小 (1-3)", "step1_Small"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🟢 单 (1,3,5)", "step1_Odd"),
                InlineKeyboardButton.WithCallbackData("🟡 双 (2,4,6)", "step1_Even"),
            }
        });

        await _botClient.SendMessage(chatId, 
            "🔢 *第一步：请选择监控玩法*", 
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var userId = callbackQuery.From.Id;

        if (string.IsNullOrEmpty(data)) return;

        try
        {
            // 处理步骤 1: 选择玩法 -> 进入步骤 2 (选择规则类型)
            if (data.StartsWith("step1_"))
            {
                var betType = data.Split('_')[1];
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔥 连开 (连续出现)", $"step2_{betType}_Consecutive"),
                        InlineKeyboardButton.WithCallbackData("❄️ 遗漏 (连续未出)", $"step2_{betType}_Missing"),
                    }
                });

                await _botClient.EditMessageText(chatId, callbackQuery.Message!.MessageId,
                    $"已选择：{GetBetTypeName(betType)}\n\n📋 *第二步：请选择规则类型*",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            // 处理步骤 2: 选择规则类型 -> 进入步骤 3 (选择期数)
            else if (data.StartsWith("step2_"))
            {
                var parts = data.Split('_');
                var betType = parts[1];
                var ruleType = parts[2];
                var prefix = $"step3_{betType}_{ruleType}_";

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] 
                    { 
                        InlineKeyboardButton.WithCallbackData("3 期", prefix + "3"),
                        InlineKeyboardButton.WithCallbackData("5 期", prefix + "5"),
                        InlineKeyboardButton.WithCallbackData("8 期", prefix + "8")
                    },
                    new[] 
                    { 
                        InlineKeyboardButton.WithCallbackData("10 期", prefix + "10"),
                        InlineKeyboardButton.WithCallbackData("15 期", prefix + "15"),
                        InlineKeyboardButton.WithCallbackData("20 期", prefix + "20")
                    },
                    new[] { InlineKeyboardButton.WithCallbackData("✏️ 自定义期数", prefix + "custom") }
                });

                await _botClient.EditMessageText(chatId, callbackQuery.Message!.MessageId,
                    $"已选择：{GetBetTypeName(betType)} -> {GetRuleTypeName(ruleType)}\n\n⏱️ *第三步：请选择触发期数*",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            // 处理步骤 3: 保存规则
            else if (data.StartsWith("step3_"))
            {
                var parts = data.Split('_');
                var betTypeStr = parts[1];
                var ruleTypeStr = parts[2];
                var valStr = parts[3];

                if (valStr == "custom")
                {
                    // 自定义输入提示
                    await _botClient.SendMessage(chatId, 
                        $"请输入自定义期数（格式：`/add {betTypeStr} {ruleTypeStr} 数字`）\n" +
                        $"例如：`/add {betTypeStr} {ruleTypeStr} 12`",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    
                    // 也可以考虑使用 UserState 来记录状态等待用户通过文本输入，这里简单起见让用户用命令补全
                    return; 
                }

                if (int.TryParse(valStr, out var threshold))
                {
                    await SaveRuleAsync(userId, chatId, betTypeStr, ruleTypeStr, threshold, cancellationToken);
                    
                    // 删除原来的按钮消息
                    await _botClient.DeleteMessage(chatId, callbackQuery.Message!.MessageId, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理回调查询异常");
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, "操作失败，请重试", cancellationToken: cancellationToken);
        }
    }

    private async Task SaveRuleAsync(long userId, long chatId, string betTypeStr, string ruleTypeStr, int threshold, CancellationToken cancellationToken)
    {
        var betType = Enum.Parse<BetType>(betTypeStr);
        var ruleType = Enum.Parse<RuleType>(ruleTypeStr);
        long groupId = 0; // 全局规则

        // 检查是否已存在
        var exists = await _dbContext.UserSettings
            .AnyAsync(s => s.UserId == userId && s.GroupId == groupId && 
                           s.RuleType == ruleType && s.BetType == betType && s.Threshold == threshold, 
                      cancellationToken);

        if (exists)
        {
            await _botClient.SendMessage(chatId, "⚠️ 该规则已存在，无需重复添加", cancellationToken: cancellationToken);
            return;
        }

        var setting = new UserSetting
        {
            UserId = userId,
            GroupId = groupId,
            RuleType = ruleType,
            BetType = betType,
            Threshold = threshold,
            IsEnabled = true
        };

        _dbContext.UserSettings.Add(setting);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(chatId,
            $"✅ *规则添加成功！*\n\n" +
            $"监控：所有群\n" +
            $"玩法：{GetBetTypeName(betTypeStr)}\n" +
            $"类型：{GetRuleTypeName(ruleTypeStr)}\n" +
            $"阈值：{threshold} 期",
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private string GetBetTypeName(string type) => type switch
    {
        "Big" => "🔴 大", "Small" => "🔵 小", 
        "Odd" => "🟢 单", "Even" => "🟡 双", _ => type
    };

    private string GetRuleTypeName(string type) => type switch
    {
        "Consecutive" => "🔥 连开", "Missing" => "❄️ 遗漏", _ => type
    };

    private async Task EnsureUserExistsAsync(Telegram.Bot.Types.User telegramUser, long chatId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FindAsync([telegramUser.Id], cancellationToken);
        
        if (user == null)
        {
            user = new User
            {
                Id = telegramUser.Id,
                Username = telegramUser.Username,
                FirstName = telegramUser.FirstName,
                LastName = telegramUser.LastName,
                ChatId = chatId,
                LanguageCode = telegramUser.LanguageCode,
                IsActive = true
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("新用户注册: {UserId} ({DisplayName})", user.Id, user.DisplayName);
        }
        else
        {
            user.ChatId = chatId;
            user.LastActiveAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
