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
/// Telegram Bot 服务 - 极简三按钮界面
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
    /// 设置机器人命令菜单
    /// </summary>
    public async Task SetCommandsAsync(CancellationToken cancellationToken = default)
    {
        var commands = new[]
        {
            new BotCommand { Command = "start", Description = "🎲 扫雷长龙监控" },
            new BotCommand { Command = "threshold", Description = "⚙️ 设置长龙阈值" },
            new BotCommand { Command = "on", Description = "▶️ 开始播报" },
            new BotCommand { Command = "off", Description = "⏸️ 停止播报" }
        };

        await _botClient.SetMyCommands(commands, cancellationToken: cancellationToken);
        _logger.LogInformation("✅ 机器人命令菜单已更新");
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
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

        // 处理按钮点击或命令
        if (text == "/start" || text == "🏠 主页")
        {
            await ShowMainMenu(chatId, userId, cancellationToken);
        }
        else if (text == "/threshold")
        {
            await ShowThresholdSettings(chatId, userId, cancellationToken);
        }
        else if (text == "/on")
        {
            await ToggleEnabled(chatId, userId, true, cancellationToken);
        }
        else if (text == "/off")
        {
            await ToggleEnabled(chatId, userId, false, cancellationToken);
        }
        // 检查是否是数字（用于设置阈值）
        else if (int.TryParse(text, out var threshold) && threshold >= 3 && threshold <= 50)
        {
            await UpdateThreshold(chatId, userId, threshold, cancellationToken);
        }
        // 未识别的输入，显示主菜单
        else
        {
            await ShowMainMenu(chatId, userId, cancellationToken);
        }
    }

    private async Task ShowMainMenu(long chatId, long userId, CancellationToken cancellationToken)
    {
        // 获取当前设置
        var setting = await GetOrCreateUserSetting(userId, cancellationToken);

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData($"⚙️ 设置长龙阈值（当前：{setting.Threshold}）", "set_threshold") },
            setting.IsEnabled 
                ? new[] { InlineKeyboardButton.WithCallbackData("✅ 开始播报", "status"), InlineKeyboardButton.WithCallbackData("⏸️ 停止播报", "toggle_off") }
                : new[] { InlineKeyboardButton.WithCallbackData("▶️ 开始播报", "toggle_on"), InlineKeyboardButton.WithCallbackData("⏸️ 停止播报", "status") }
        });

        var statusIcon = setting.IsEnabled ? "✅" : "⏸️";
        var statusText = setting.IsEnabled ? "开启中" : "已停止";

        var text = $"""
            🎲 扫雷长龙监控

            当前状态：{statusIcon} {statusText}
            当前阈值：{setting.Threshold} 期

            📌 自动监控所有玩法
            大、小、单、双、大单、大双、小单、小双、花龙

            任何玩法达到阈值即推送提醒
            """;

        // 移除底部菜单，只显示内联按钮
        await _botClient.SendMessage(chatId, text, 
            replyMarkup: keyboard, 
            cancellationToken: cancellationToken);
    }

    private async Task ShowThresholdSettings(long chatId, long userId, CancellationToken cancellationToken)
    {
        var setting = await GetOrCreateUserSetting(userId, cancellationToken);

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { 
                InlineKeyboardButton.WithCallbackData("5", "th_5"), 
                InlineKeyboardButton.WithCallbackData("8", "th_8"), 
                InlineKeyboardButton.WithCallbackData("10", "th_10"),
                InlineKeyboardButton.WithCallbackData("12", "th_12")
            },
            new[] { 
                InlineKeyboardButton.WithCallbackData("15", "th_15"), 
                InlineKeyboardButton.WithCallbackData("20", "th_20"),
                InlineKeyboardButton.WithCallbackData("25", "th_25"), 
                InlineKeyboardButton.WithCallbackData("30", "th_30")
            },
            new[] { InlineKeyboardButton.WithCallbackData("✏️ 自定义 (输入 3-50)", "th_custom") }
        });

        await _botClient.SendMessage(chatId,
            $"⚙️ *设置长龙阈值*\n\n当前：{setting.Threshold} 期\n\n快速选择或直接输入数字：",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task UpdateThreshold(long chatId, long userId, int threshold, CancellationToken cancellationToken)
    {
        var setting = await GetOrCreateUserSetting(userId, cancellationToken);
        setting.Threshold = threshold;
        setting.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(chatId,
            $"✅ 阈值已更新为 {threshold} 期",
            cancellationToken: cancellationToken);
    }

    private async Task ToggleEnabled(long chatId, long userId, bool enabled, CancellationToken cancellationToken)
    {
        var setting = await GetOrCreateUserSetting(userId, cancellationToken);
        setting.IsEnabled = enabled;
        setting.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var statusText = enabled ? "✅ 播报已开启" : "⏸️ 播报已停止";
        await _botClient.SendMessage(chatId, statusText, cancellationToken: cancellationToken);
    }

    private async Task<UserSetting> GetOrCreateUserSetting(long userId, CancellationToken cancellationToken)
    {
        var setting = await _dbContext.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (setting == null)
        {
            setting = new UserSetting
            {
                UserId = userId,
                GroupId = null, // 全局监控
                RuleType = RuleType.Consecutive,
                RuleCategory = "All",
                Threshold = 10,
                IsEnabled = true
            };
            _dbContext.UserSettings.Add(setting);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return setting;
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var userId = callbackQuery.From.Id;

        if (string.IsNullOrEmpty(data)) return;

        try
        {
            // 阈值设置
            if (data == "set_threshold")
            {
                await ShowThresholdSettings(chatId, userId, cancellationToken);
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
            // 快速设置阈值
            else if (data.StartsWith("th_"))
            {
                var thresholdStr = data.Substring(3);
                
                if (thresholdStr == "custom")
                {
                    await _botClient.SendMessage(chatId,
                        "✏️ 请直接输入 3-50 之间的数字",
                        cancellationToken: cancellationToken);
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                }
                else if (int.TryParse(thresholdStr, out var threshold))
                {
                    var setting = await GetOrCreateUserSetting(userId, cancellationToken);
                    setting.Threshold = threshold;
                    setting.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    // 删除原消息并返回主菜单
                    await _botClient.DeleteMessage(chatId, callbackQuery.Message!.MessageId, cancellationToken);
                    await ShowMainMenu(chatId, userId, cancellationToken);
                        
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, $"✅ 阈值已设置为 {threshold} 期", cancellationToken: cancellationToken);
                }
            }
            // 开启播报
            else if (data == "toggle_on")
            {
                var setting = await GetOrCreateUserSetting(userId, cancellationToken);
                setting.IsEnabled = true;
                setting.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _botClient.EditMessageText(chatId, callbackQuery.Message!.MessageId,
                    $"""
                    ✅ 播报已开启

                    当前阈值：{setting.Threshold} 期
                    监控范围：所有群组
                    监控玩法：所有长龙

                    开始监控中...
                    """,
                    cancellationToken: cancellationToken);

                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ 已开启播报", cancellationToken: cancellationToken);
            }
            // 停止播报
            else if (data == "toggle_off")
            {
                var setting = await GetOrCreateUserSetting(userId, cancellationToken);
                setting.IsEnabled = false;
                setting.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _botClient.EditMessageText(chatId, callbackQuery.Message!.MessageId,
                    "⏸️ 播报已停止\n\n点击 /start 重新开始",
                    cancellationToken: cancellationToken);

                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "⏸️ 已停止播报", cancellationToken: cancellationToken);
            }
            // 显示状态（点击已激活的按钮）
            else if (data == "status")
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "当前状态", showAlert: false, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理回调查询异常");
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, "操作失败，请重试", cancellationToken: cancellationToken);
        }
    }

    private async Task EnsureUserExistsAsync(Telegram.Bot.Types.User from, long chatId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FindAsync([from.Id], cancellationToken);
        
        if (user == null)
        {
            user = new User
            {
                Id = from.Id,
                Username = from.Username ?? "",
                FirstName = from.FirstName,
                LastName = from.LastName,
                ChatId = chatId,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("新用户注册: {Username} (ID: {UserId})", user.Username, user.Id);
        }
        else if (user.ChatId != chatId)
        {
            user.ChatId = chatId;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
