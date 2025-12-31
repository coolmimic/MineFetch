using Microsoft.EntityFrameworkCore;
using MineFetch.Api.Data;
using MineFetch.Api.Services;
using Serilog;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// 配置 Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// 添加服务
builder.Services.AddControllers()
    .AddNewtonsoftJson(); // Telegram.Bot 需要 Newtonsoft.Json

// 配置数据库
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (connectionString?.StartsWith("Host=") == true)
{
    // PostgreSQL
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    // SQLite
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(connectionString ?? "Data Source=minefetch.db"));
}

// 配置 Telegram Bot
var botToken = builder.Configuration["Telegram:BotToken"] 
    ?? throw new Exception("缺少 Telegram:BotToken 配置");

builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));

// 添加内存缓存
builder.Services.AddMemoryCache();

// 注册服务
builder.Services.AddSingleton<LotteryCacheService>(); // 缓存服务使用单例
builder.Services.AddScoped<LotteryService>();
builder.Services.AddScoped<RuleEngine>();
builder.Services.AddScoped<PushService>();
builder.Services.AddScoped<TelegramBotService>();

// 如果没有配置 Webhook，则使用轮询模式
var webhookUrl = builder.Configuration["Telegram:WebhookUrl"];
if (string.IsNullOrEmpty(webhookUrl))
{
    builder.Services.AddHostedService<BotPollingService>();
}

// 添加 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 自动迁移数据库
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
    Log.Information("数据库初始化完成");
}

// 配置中间件
app.UseSwagger();
app.UseSwaggerUI();

app.UseSerilogRequestLogging();
app.MapControllers();

// 设置 Webhook（如果配置了 WebhookUrl）
if (!string.IsNullOrEmpty(webhookUrl))
{
    var botClient = app.Services.GetRequiredService<ITelegramBotClient>();
    await botClient.SetWebhook(webhookUrl);
    Log.Information("Webhook 已设置: {WebhookUrl}", webhookUrl);
}
else
{
    Log.Information("使用轮询模式接收 Bot 消息");
}

Log.Information("========================================");
Log.Information("  MineFetch API 服务已启动");
Log.Information("  Swagger: http://localhost:5000/swagger");
Log.Information("========================================");

await app.RunAsync();

