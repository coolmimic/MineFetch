using MineFetch.Entities.DTOs;
using Telegram.Bot;

namespace MineFetch.Api.Services;

/// <summary>
/// 推送服务 - 向用户发送 Telegram 消息
/// </summary>
public class PushService
{
    private readonly ILogger<PushService> _logger;
    private readonly ITelegramBotClient _botClient;

    public PushService(ILogger<PushService> logger, ITelegramBotClient botClient)
    {
        _logger = logger;
        _botClient = botClient;
    }

    /// <summary>
    /// 发送推送消息
    /// </summary>
    public async Task SendPushAsync(PushMessageDto message, CancellationToken cancellationToken = default)
    {
        try
        {
            var text = message.ToMessageText();
            
            await _botClient.SendMessage(
                chatId: message.ChatId,
                text: text,
                cancellationToken: cancellationToken);

            _logger.LogInformation("✅ 推送成功: ChatId={ChatId}, 期号={PeriodId}", 
                message.ChatId, message.PeriodId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 推送失败: ChatId={ChatId}", message.ChatId);
        }
    }
}
