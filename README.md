# MineFetch - Telegram 扫雷游戏数据采集系统

一个完整的 .NET 8 系统，用于采集 Telegram 群组中的扫雷游戏开奖号码，并根据用户设置的规则推送提醒。

## 系统架构

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Telegram 群   │────▶│   采集端        │────▶│   后台 API      │
│   (扫雷游戏)    │     │   Collector     │     │   Api           │
└─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                         │
                                                         ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   用户 TG       │◀────│   Bot 推送      │◀────│   规则引擎      │
│                 │     │                 │     │                 │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

## 项目结构

```
MineFetch/
├── MineFetch.sln
├── MineFetch.Entities/          # 共享实体层
│   ├── Models/                  # 数据模型
│   ├── Enums/                   # 枚举定义
│   └── DTOs/                    # 数据传输对象
│
├── MineFetch.Collector/         # 采集端
│   ├── Program.cs
│   ├── appsettings.json
│   └── Services/
│       ├── TelegramCollector.cs # Telegram 消息监控
│       ├── MessageParser.cs     # 消息解析
│       └── BackendClient.cs     # API 客户端
│
├── MineFetch.Api/               # 后台 API
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Controllers/
│   │   ├── LotteryController.cs # 开奖接口
│   │   └── WebhookController.cs # Bot Webhook
│   ├── Services/
│   │   ├── LotteryService.cs    # 开奖服务
│   │   ├── RuleEngine.cs        # 规则引擎
│   │   ├── PushService.cs       # 推送服务
│   │   └── TelegramBotService.cs# Bot 处理
│   └── Data/
│       └── AppDbContext.cs      # 数据库上下文
│
├── Dockerfile.api               # API Docker 构建
├── Dockerfile.collector         # 采集端 Docker 构建
├── docker-compose.yml           # Docker Compose 配置
├── deploy.sh                    # 部署脚本
└── .env.example                 # 环境变量模板
```

## 功能特性

### 采集端 (Collector)
- 🔌 使用 Telegram 用户账号登录，实时监听群组消息
- 🎯 智能解析期号（如 `SL652726900409832`）和骰子号码（1-6）
- 🚀 将采集数据上报到后台 API

### 后台 API
- 📊 记录所有开奖历史
- 📈 提供统计分析接口
- 🤖 Telegram Bot Webhook 处理用户命令
- ⚡ 规则引擎自动检测遗漏/连开
- 📢 触发规则后自动推送给用户

### 推送规则
- **遗漏检测**：当某类型（大/小/单/双）连续 N 期未出现
- **连开检测**：当某类型连续出现 N 期

## 快速开始

### 本地开发

1. **启动后台 API**

```bash
cd MineFetch.Api
dotnet run
```

API 将在 http://localhost:5000 启动，Swagger 文档在 http://localhost:5000/swagger

2. **启动采集端**

```bash
cd MineFetch.Collector
dotnet run
```

首次运行时，程序会交互式提示输入：
- 📱 **手机号** - 格式如 `+8613800138000`
- 🔢 **验证码** - 发送到你的 Telegram
- 🔐 **两步验证密码** - 如果你启用了两步验证（密码会显示为 `***`）

登录成功后，会话信息保存到 `session.dat`，下次启动无需再次验证。

3. **启用后端上报**

编辑 `MineFetch.Collector/appsettings.json`：
```json
{
  "Backend": {
    "Enabled": true
  }
```

### Docker 部署（Ubuntu 服务器）

1. 将代码上传到服务器

2. 复制环境变量文件并填写配置：
```bash
cp .env.example .env
nano .env
```

3. 运行部署脚本：
```bash
chmod +x deploy.sh
./deploy.sh
```

4. 设置 Webhook（可选）：

如果你有域名和 SSL 证书，在 `.env` 中设置：
```
WEBHOOK_URL=https://your-domain.com/api/webhook
```

## Bot 命令

| 命令 | 说明 |
|------|------|
| /start | 注册并查看欢迎信息 |
| /help | 查看帮助 |
| /groups | 查看可监控的群组 |
| /list | 查看我的推送规则 |
| /add | 添加推送规则 |
| /del | 删除推送规则 |

### 添加规则示例

```
/add -1001234567890 连开 大 5
```
→ 当「大」连续出现 5 期时推送

```
/add -1001234567890 遗漏 小 8
```
→ 当「小」连续 8 期未出现时推送

## API 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | /api/lottery/report | 采集端上报开奖结果 |
| GET | /api/lottery/history | 获取开奖历史 |
| GET | /api/lottery/stats/{groupId} | 获取统计数据 |
| POST | /api/webhook | Telegram Bot Webhook |

### 上报数据格式

```json
{
  "periodId": "SL652726900409832",
  "diceNumber": 6,
  "groupId": -1001234567890,
  "groupName": "扫雷群1",
  "messageId": 12345,
  "collectedAt": "2025-12-31T02:00:00Z"
}
```

## 推送消息示例

```
🎯 扫雷提醒

群组: 扫雷监控群
期号: SL652726900409832

⚠️ 【大】已连开 5 期！
当前结果: 6 (大/双)
```

## 技术栈

- .NET 8
- Entity Framework Core
- SQLite / PostgreSQL
- WTelegramClient
- Telegram.Bot
- Serilog
- Docker

## 安全提示

⚠️ **重要**：

1. 不要提交 `.env` 文件和 `session.dat`
2. 妥善保管 Telegram API 凭据和 Bot Token
3. 定期更换数据库密码

## License

MIT
