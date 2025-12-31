using Microsoft.AspNetCore.Mvc;
using MineFetch.Api.Services;
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
    public async Task<IActionResult> Post([FromBody] Update update, CancellationToken cancellationToken)
    {
        _logger.LogDebug("收到 Webhook 更新: {UpdateId}", update.Id);
        
        await _botService.HandleUpdateAsync(update, cancellationToken);
        
        return Ok();
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
