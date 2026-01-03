using Microsoft.AspNetCore.Mvc;
using MineFetch.Api.Services;
using Newtonsoft.Json;
using Telegram.Bot.Types;

namespace MineFetch.Api.Controllers;

/// <summary>
/// Telegram Bot Webhook 控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly TelegramBotService _botService;

    public WebhookController(ILogger<WebhookController> logger, TelegramBotService botService)
    {
        _logger = logger;
        _botService = botService;
    }

    /// <summary>
    /// Telegram Bot Webhook 入口
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        try
        {
            // 读取原始请求体
            using var reader = new StreamReader(Request.Body);
            var rawJson = await reader.ReadToEndAsync(cancellationToken);
            
            _logger.LogDebug("收到 Webhook 请求，原始内容: {RawJson}", rawJson);
            
            if (string.IsNullOrEmpty(rawJson))
            {
                _logger.LogWarning("Webhook 请求体为空");
                return Ok();
            }
            
            // 使用 Newtonsoft.Json 反序列化（Telegram.Bot 库使用此格式）
            var update = JsonConvert.DeserializeObject<Update>(rawJson);
            
            if (update == null)
            {
                _logger.LogWarning("Webhook 更新反序列化为 null");
                return Ok();
            }
            
            // 详细日志
            if (update.Message != null)
            {
                _logger.LogInformation("收到消息: ChatType={ChatType}, ChatId={ChatId}, Text={Text}",
                    update.Message.Chat.Type,
                    update.Message.Chat.Id,
                    update.Message.Text ?? "(无文本)");
            }
            
            await _botService.HandleUpdateAsync(update, cancellationToken);
            
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 Webhook 更新时发生异常");
            // 返回 200 避免 Telegram 重试
            return Ok();
        }
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}

