using Microsoft.AspNetCore.Mvc;
using MineFetch.Api.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Telegram.Bot.Types;

namespace MineFetch.Api.Controllers;

/// <summary>
/// Unix 时间戳转换器 - 将 Telegram 的 Unix 时间戳转为 DateTime
/// </summary>
public class UnixTimestampConverter : DateTimeConverterBase
{
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Integer)
        {
            var seconds = (long)reader.Value!;
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        return null;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is DateTime dt)
        {
            writer.WriteValue(new DateTimeOffset(dt).ToUnixTimeSeconds());
        }
    }
}

/// <summary>
/// Telegram Bot Webhook 控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly TelegramBotService _botService;
    
    // Telegram Bot 专用的 JSON 序列化设置（支持 snake_case）
    private static readonly JsonSerializerSettings TelegramJsonSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        },
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        Converters = { new UnixTimestampConverter() }
    };

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
            
            // 使用 Telegram.Bot 的序列化设置反序列化
            var update = JsonConvert.DeserializeObject<Update>(rawJson, TelegramJsonSettings);
            
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

