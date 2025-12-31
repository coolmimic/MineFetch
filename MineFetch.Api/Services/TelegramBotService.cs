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

        // 创建固定菜单键盘
        var menuKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { new("➕ 添加规则"), new("📋 我的规则") },
            new KeyboardButton[] { new("❓ 使用帮助") }
        })
        {
            ResizeKeyboard = true,
            IsPersistent = true
        };

        // 处理按钮点击
        switch (text)
        {
            case "/start":
            case "🏠 主页":
                await HandleStartAsync(chatId, menuKeyboard, cancellationToken);
                break;
            case "➕ 添加规则":
                await HandleAddSettingAsync(userId, chatId, "", cancellationToken);
                break;
            case "📋 我的规则":
                await HandleListSettingsAsync(userId, chatId, cancellationToken);
                break;
            case "❓ 使用帮助":
                await HandleHelpAsync(chatId, cancellationToken);
                break;
            default:
                // 忽略其他消息
                break;
        }
    }

    private async Task HandleStartAsync(long chatId, IReplyMarkup? replyMarkup, CancellationToken cancellationToken)
    {
        var text = """
            👋 欢迎使用扫雷数据采集助手！

            🤖 我会自动监控所有群组的开奖结果。
            """;

        await _botClient.SendMessage(chatId, text, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
    }

    private async Task HandleHelpAsync(long chatId, CancellationToken cancellationToken)
    {
        var text = """
            📖 使用帮助

            📍 点击底部菜单按钮操作：
            
            ➕ **添加规则**
            选择玩法类型，设置触发期数。
            
            📋 **我的规则**
            查看已设置的规则，点击删除按钮可移除。

            💡 **玩法说明**
            🔴 大 (4-6) | 🔵 小 (1-3)
            🟢 单 (1,3,5) | 🟡 双 (2,4,6)
            🧩 组合玩法: 大单、大双、小单、小双
            🐉 花龙: 大小或单双交替出现
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
            var emptyKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("➕ 添加规则", "cmd_add") }
            });

            await _botClient.SendMessage(chatId, 
                "📭 你还没有设置任何推送规则", 
                replyMarkup: emptyKeyboard,
                cancellationToken: cancellationToken);
            return;
        }

        // 为每个规则创建一行（规则描述 + 删除按钮）
        var buttons = new List<InlineKeyboardButton[]>();
        
        foreach (var s in settings)
        {
            var status = s.IsEnabled ? "✅" : "❌";
            var groupName = s.GroupId == null ? "所有群" : (s.Group?.Title ?? "未知群组");
            var ruleText = $"{status} {groupName} - {s.GetDescription()}";
            
            // 每行两个按钮：规则描述（占位）、删除按钮
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"🗑️ 删除 #{s.Id}", $"del_{s.Id}")
            });
        }

        // 添加底部按钮
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ 添加新规则", "cmd_add") });

        var keyboard = new InlineKeyboardMarkup(buttons);

        // 构建规则列表文本
        var lines = new List<string> { "📋 *我的推送规则*", "" };
        foreach (var s in settings)
        {
            var status = s.IsEnabled ? "✅" : "❌";
            var groupName = s.GroupId == null ? "所有群" : (s.Group?.Title ?? "未知群组");
            lines.Add($"{status} *#{s.Id}* {groupName}");
            lines.Add($"   {s.GetDescription()}");
            lines.Add("");
        }

        await _botClient.SendMessage(chatId, 
            string.Join("\n", lines),
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleAddSettingAsync(long userId, long chatId, string args, CancellationToken cancellationToken)
    {
        // 步骤 0: 选择玩法大类
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🔴 大小单双玩法", "cat_Basic") },
            new[] { InlineKeyboardButton.WithCallbackData("🧩 组合玩法 (大单/大双...)", "cat_Combo") },
            new[] { InlineKeyboardButton.WithCallbackData("🐉 花龙玩法", "cat_Dragon") }
        });

        await _botClient.SendMessage(chatId, 
            "📂 *请选择玩法类型*", 
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
            // 主菜单命令
            if (data == "cmd_add")
            {
                await HandleAddSettingAsync(userId, chatId, "", cancellationToken);
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
            else if (data == "cmd_list")
            {
                await HandleListSettingsAsync(userId, chatId, cancellationToken);
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
            else if (data == "cmd_help")
            {
                await HandleHelpAsync(chatId, cancellationToken);
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
            // 步骤 1: 选择玩法组 -> 直接进入 (选择期数)
            else if (data.StartsWith("cat_"))
            {
                var category = data.Split('_')[1];
                var ruleType = "Consecutive"; // 默认规则类型：连开
                var prefix = $"step3_{category}_{ruleType}_";

                // 根据不同分类显示不同的标题，虽然期数选择是一样的
                string title = category switch
                {
                    "Basic" => "🔴 大小单双玩法",
                    "Combo" => "🧩 组合玩法",
                    "Dragon" => "🐉 花龙玩法",
                    _ => "未知玩法"
                };

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("3 期", prefix + "3"), InlineKeyboardButton.WithCallbackData("4 期", prefix + "4"), InlineKeyboardButton.WithCallbackData("5 期", prefix + "5") },
                    new[] { InlineKeyboardButton.WithCallbackData("6 期", prefix + "6"), InlineKeyboardButton.WithCallbackData("7 期", prefix + "7"), InlineKeyboardButton.WithCallbackData("8 期", prefix + "8") },
                    new[] { InlineKeyboardButton.WithCallbackData("10 期", prefix + "10"), InlineKeyboardButton.WithCallbackData("12 期", prefix + "12"), InlineKeyboardButton.WithCallbackData("15 期", prefix + "15") },
                    new[] { InlineKeyboardButton.WithCallbackData("✏️ 自定义", prefix + "custom"), InlineKeyboardButton.WithCallbackData("🔙 返回", "cmd_add") }
                });

                await _botClient.EditMessageText(chatId, callbackQuery.Message!.MessageId,
                    $"已选择：{title}\n\n⏱️ *请选择触发期数*",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
                    
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
            // 删除规则
            else if (data.StartsWith("del_"))
            {
                var settingId = int.Parse(data.Split('_')[1]);
                
                var setting = await _dbContext.UserSettings
                    .FirstOrDefaultAsync(s => s.Id == settingId && s.UserId == userId, cancellationToken);

                if (setting != null)
                {
                    _dbContext.UserSettings.Remove(setting);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    // 删除后刷新规则列表
                    await HandleListSettingsAsync(userId, chatId, cancellationToken);
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ 规则已删除", cancellationToken: cancellationToken);
                }
                else
                {
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, "❌ 规则不存在", showAlert: true, cancellationToken: cancellationToken);
                }
            }
            // 步骤 3: 保存规则
            else if (data.StartsWith("step3_"))
            {
                var parts = data.Split('_');
                var category = parts[1]; // 这里保存的是 category (Basic, Combo, Dragon)
                var ruleTypeStr = parts[2];
                var valStr = parts[3];

                if (valStr == "custom")
                {
                    await _botClient.SendMessage(chatId, 
                        $"请输入自定义期数（格式：`/add {category} {ruleTypeStr} 数字`）\n" +
                        $"例如：`/add {category} {ruleTypeStr} 12`",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                    return; 
                }

                if (int.TryParse(valStr, out var threshold))
                {
                    // 保存单个规则（使用 RuleCategory 标识规则组）
                    await SaveRuleAsync(userId, chatId, category, ruleTypeStr, threshold, cancellationToken);

                    // 用确认消息替换原消息
                    string catName = category switch
                    {
                        "Basic" => "大小单双",
                        "Combo" => "组合",
                        "Dragon" => "花龙",
                        _ => category
                    };

                    await _botClient.EditMessageText(chatId, callbackQuery.Message!.MessageId,
                        $"✅ *规则添加成功！*\n\n" +
                        $"监控：所有群\n" +
                        $"玩法：{catName}\n" +
                        $"类型：{GetRuleTypeName(ruleTypeStr)}\n" +
                        $"阈值：{threshold} 期",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                        
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ 规则添加成功", cancellationToken: cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理回调查询异常");
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, "操作失败，请重试", cancellationToken: cancellationToken);
        }
    }

    private async Task SaveRuleAsync(long userId, long chatId, string category, string ruleTypeStr, int threshold, CancellationToken cancellationToken)
    {
        var ruleType = Enum.Parse<RuleType>(ruleTypeStr);
        long? groupId = null; // 全局规则

        // 检查是否已存在
        var exists = await _dbContext.UserSettings
            .AnyAsync(s => s.UserId == userId && s.GroupId == groupId && 
                           s.RuleType == ruleType && s.RuleCategory == category && s.Threshold == threshold, 
                      cancellationToken);

        if (exists) return; // 静默跳过重复的

        var setting = new UserSetting
        {
            UserId = userId,
            GroupId = groupId,
            RuleType = ruleType,
            RuleCategory = category,
            BetType = BetType.Big, // 保留兼容性，设置默认值
            Threshold = threshold,
            IsEnabled = true
        };

        _dbContext.UserSettings.Add(setting);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private string GetBetTypeName(string type) => type switch
    {
        "Big" => "🔴 大", "Small" => "🔵 小", 
        "Odd" => "🟢 单", "Even" => "🟡 双",
        "BigOdd" => "大单", "BigEven" => "大双",
        "SmallOdd" => "小单", "SmallEven" => "小双",
        "Dragon" => "🐉 花龙",
        _ => type
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
