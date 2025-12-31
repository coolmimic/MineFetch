using Microsoft.AspNetCore.Mvc;
using MineFetch.Api.Services;
using System.Text.Json;
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
            
            _logger.LogInformation("收到 Webhook 请求，原始内容: {RawJson}", rawJson);
            
            if (string.IsNullOrEmpty(rawJson))
            {
                _logger.LogWarning("Webhook 请求体为空");
                return Ok();
            }
            
            // 手动反序列化
            var update = JsonSerializer.Deserialize<Update>(rawJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (update == null)
            {
                _logger.LogWarning("Webhook 更新反序列化为 null");
                return Ok();
            }
            
            _logger.LogInformation("Webhook 更新解析成功: UpdateId={UpdateId}, Type={Type}", 
                update.Id, 
                update.Type);
            
            await _botService.HandleUpdateAsync(update, cancellationToken);
            
            _logger.LogDebug("Webhook 更新处理完成: {UpdateId}", update.Id);
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

