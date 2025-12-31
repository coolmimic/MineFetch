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
                await HandleAddSettingAsync(userId, chatId, args, cancellationToken);
                break;
            case "/del":
                await HandleDeleteSettingAsync(userId, chatId, args, cancellationToken);
                break;
            case "/groups":
                await HandleListGroupsAsync(chatId, cancellationToken);
                break;
            default:
                // 忽略非命令消息
                break;
        }
    }

    private async Task HandleStartAsync(long chatId, CancellationToken cancellationToken)
    {
        var text = """
            👋 欢迎使用扫雷数据采集助手！

            我可以帮你监控扫雷游戏的开奖结果，并在满足条件时推送提醒。

            📋 可用命令：
            /help - 查看帮助
            /groups - 查看可监控的群组
            /list - 查看我的推送规则
            /add - 添加推送规则
            /del - 删除推送规则

            📖 快速开始：
            1. 使用 /groups 查看可监控的群组
            2. 使用 /add 命令添加规则
               格式：/add 群组ID 规则类型 投注类型 阈值
               例如：/add -1001234567890 连开 大 5

            规则类型：遗漏、连开
            投注类型：大、小、单、双
            """;

        await _botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }

    private async Task HandleHelpAsync(long chatId, CancellationToken cancellationToken)
    {
        var text = """
            📖 使用帮助

            🎯 推送规则说明：
            - 遗漏：当某个类型连续 N 期未出现时推送
            - 连开：当某个类型连续出现 N 期时推送

            📝 添加规则示例：
            /add -1001234567890 连开 大 5
            → 当「大」连续出现 5 期时推送

            /add -1001234567890 遗漏 小 8
            → 当「小」连续 8 期未出现时推送

            🗑️ 删除规则：
            /del 规则ID
            → 使用 /list 查看规则 ID

            💡 投注类型：
            - 大：4, 5, 6
            - 小：1, 2, 3
            - 单：1, 3, 5
            - 双：2, 4, 6
            """;

        await _botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }

    private async Task HandleListGroupsAsync(long chatId, CancellationToken cancellationToken)
    {
        var groups = await _dbContext.TelegramGroups
            .Where(g => g.IsActive)
            .OrderBy(g => g.Title)
            .ToListAsync(cancellationToken);

        if (!groups.Any())
        {
            await _botClient.SendMessage(chatId, "❌ 暂无可监控的群组", cancellationToken: cancellationToken);
            return;
        }

        var lines = new List<string> { "📋 可监控的群组：", "" };
        foreach (var group in groups)
        {
            lines.Add($"• {group.Title}");
            lines.Add($"  ID: `{group.Id}`");
            lines.Add("");
        }

        await _botClient.SendMessage(
            chatId, 
            string.Join("\n", lines), 
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
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
        // 解析参数：群组ID 规则类型 投注类型 阈值
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length < 4)
        {
            await _botClient.SendMessage(chatId,
                "❌ 参数不正确\n\n格式：/add 群组ID 规则类型 投注类型 阈值\n例如：/add -1001234567890 连开 大 5",
                cancellationToken: cancellationToken);
            return;
        }

        if (!long.TryParse(parts[0], out var groupId))
        {
            await _botClient.SendMessage(chatId, "❌ 群组 ID 无效", cancellationToken: cancellationToken);
            return;
        }

        // 检查群组是否存在
        var group = await _dbContext.TelegramGroups.FindAsync([groupId], cancellationToken);
        if (group == null)
        {
            await _botClient.SendMessage(chatId, "❌ 群组不存在或未启用", cancellationToken: cancellationToken);
            return;
        }

        // 解析规则类型
        RuleType ruleType;
        switch (parts[1])
        {
            case "遗漏":
            case "missing":
                ruleType = RuleType.Missing;
                break;
            case "连开":
            case "consecutive":
                ruleType = RuleType.Consecutive;
                break;
            default:
                await _botClient.SendMessage(chatId, "❌ 规则类型无效，请使用：遗漏、连开", cancellationToken: cancellationToken);
                return;
        }

        // 解析投注类型
        BetType betType;
        switch (parts[2])
        {
            case "大":
            case "big":
                betType = BetType.Big;
                break;
            case "小":
            case "small":
                betType = BetType.Small;
                break;
            case "单":
            case "odd":
                betType = BetType.Odd;
                break;
            case "双":
            case "even":
                betType = BetType.Even;
                break;
            default:
                await _botClient.SendMessage(chatId, "❌ 投注类型无效，请使用：大、小、单、双", cancellationToken: cancellationToken);
                return;
        }

        // 解析阈值
        if (!int.TryParse(parts[3], out var threshold) || threshold < 1 || threshold > 100)
        {
            await _botClient.SendMessage(chatId, "❌ 阈值无效，请使用 1-100 之间的数字", cancellationToken: cancellationToken);
            return;
        }

        // 检查是否已存在相同规则
        var exists = await _dbContext.UserSettings
            .AnyAsync(s => s.UserId == userId && s.GroupId == groupId && 
                          s.RuleType == ruleType && s.BetType == betType, 
                      cancellationToken);

        if (exists)
        {
            await _botClient.SendMessage(chatId, "❌ 已存在相同的规则", cancellationToken: cancellationToken);
            return;
        }

        // 创建规则
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
            $"✅ 规则添加成功！\n\n群组：{group.Title}\n规则：{setting.GetDescription()}",
            cancellationToken: cancellationToken);
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

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        // 暂时不处理回调查询
        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
    }

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
