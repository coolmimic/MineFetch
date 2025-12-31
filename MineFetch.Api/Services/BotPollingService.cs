using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MineFetch.Api.Services;

/// <summary>
/// Telegram Bot 轮询服务 - 开发环境使用
/// 生产环境应使用 Webhook
/// </summary>
public class BotPollingService : BackgroundService
{
    private readonly ILogger<BotPollingService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceProvider _serviceProvider;

    public BotPollingService(
        ILogger<BotPollingService> logger,
        ITelegramBotClient botClient,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _botClient = botClient;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🤖 Bot 轮询服务启动...");

        // 设置机器人命令菜单
        using (var scope = _serviceProvider.CreateScope())
        {
            var botService = scope.ServiceProvider.GetRequiredService<TelegramBotService>();
            await botService.SetCommandsAsync(stoppingToken);
        }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        try
        {
            // 使用 StartReceiving 而不是 ReceiveAsync
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);

            // 等待取消
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Bot 轮询服务停止");
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var botService = scope.ServiceProvider.GetRequiredService<TelegramBotService>();
            await botService.HandleUpdateAsync(update, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 Bot 消息时发生错误");
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Bot 轮询发生错误");
        return Task.CompletedTask;
    }
}
