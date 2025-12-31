using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MineFetch.Entities.DTOs;
using Serilog;
using TL;
using WTelegram;

namespace MineFetch.Collector.Services;

/// <summary>
/// Telegram 采集服务 - 监控群组消息并提取开奖信息
/// </summary>
public class TelegramCollector : BackgroundService
{
    private static readonly ILogger Logger = Log.ForContext<TelegramCollector>();
    
    private readonly IConfiguration _configuration;
    private readonly MessageParser _parser;
    private readonly BackendClient _backendClient;
    
    private Client? _client;
    private User? _myself;
    private readonly Dictionary<long, string> _monitorGroups = new();
    private readonly HashSet<string> _processedPeriods = new(); // 防止重复处理

    public TelegramCollector(
        IConfiguration configuration,
        MessageParser parser,
        BackendClient backendClient)
    {
        _configuration = configuration;
        _parser = parser;
        _backendClient = backendClient;

        // 加载监控群组配置
        var groups = _configuration.GetSection("MonitorGroups").Get<List<MonitorGroupConfig>>() ?? new();
        foreach (var group in groups)
        {
            _monitorGroups[group.GroupId] = group.GroupName;
        }

        Logger.Information("已配置 {Count} 个监控群组", _monitorGroups.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.Information("🚀 Telegram 采集服务启动...");

        try
        {
            await InitializeClientAsync(stoppingToken);
            
            if (_client == null)
            {
                Logger.Error("Telegram 客户端初始化失败");
                return;
            }

            // 保持服务运行
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Information("采集服务收到停止信号");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "采集服务发生异常");
            throw;
        }
        finally
        {
            _client?.Dispose();
            Logger.Information("🛑 Telegram 采集服务已停止");
        }
    }

    private async Task InitializeClientAsync(CancellationToken stoppingToken)
    {
        var section = _configuration.GetSection("Telegram");
        var sessionPath = section["SessionPath"] ?? "session.dat";
        var phonePath = sessionPath + ".phone"; // 手机号缓存文件

        // 检查是否有已保存的会话
        var hasSession = File.Exists(sessionPath) && new FileInfo(sessionPath).Length > 0;
        
        // 尝试读取缓存的手机号
        string? savedPhone = null;
        if (File.Exists(phonePath))
        {
            savedPhone = File.ReadAllText(phonePath).Trim();
        }

        // 如果有已保存的账户，让用户选择
        if (hasSession && !string.IsNullOrEmpty(savedPhone))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"检测到已保存的账户: {MaskPhone(savedPhone)}");
            Console.ResetColor();
            Console.Write("是否使用此账户登录? (Y/n): ");
            
            var choice = Console.ReadLine()?.Trim().ToLower();
            
            if (choice == "n" || choice == "no")
            {
                // 用户选择新账户，清除旧的 session
                Logger.Information("用户选择使用新账户，清除旧会话...");
                if (File.Exists(sessionPath)) File.Delete(sessionPath);
                if (File.Exists(phonePath)) File.Delete(phonePath);
                savedPhone = null;
                hasSession = false;
            }
        }

        // 创建配置函数
        string? Config(string what)
        {
            switch (what)
            {
                case "api_id":
                    return section["ApiId"] ?? throw new Exception("缺少 ApiId 配置");
                case "api_hash":
                    return section["ApiHash"] ?? throw new Exception("缺少 ApiHash 配置");
                case "phone_number":
                    // 如果有缓存的手机号，直接使用
                    if (!string.IsNullOrEmpty(savedPhone))
                    {
                        return savedPhone;
                    }
                    // 否则提示用户输入并保存
                    var phone = PromptInput("请输入手机号 (格式: +86xxxxxxxxx): ");
                    File.WriteAllText(phonePath, phone);
                    savedPhone = phone;
                    return phone;
                case "verification_code":
                    return PromptInput("请输入验证码: ");
                case "password":
                    return PromptPassword("请输入两步验证密码: ");
                case "session_pathname":
                    return sessionPath;
                default:
                    return null;
            }
        }

        // 关闭 WTelegram 内部日志
        WTelegram.Helpers.Log = (level, message) => { };

        _client = new Client(Config);

        if (hasSession && !string.IsNullOrEmpty(savedPhone))
        {
            Logger.Information("自动登录中...");
        }
        else
        {
            Logger.Information("请按提示输入登录信息");
        }

        _myself = await _client.LoginUserIfNeeded();
        Logger.Information("✅ 登录成功: {Username} ({UserId})", _myself.username ?? _myself.first_name, _myself.id);

        // 获取所有对话，建立群组映射
        var dialogs = await _client.Messages_GetAllDialogs();
        Logger.Information("已获取 {Count} 个对话", dialogs.dialogs.Length);

        // 读取 GroupLink.txt 中的群组链接
        var groupLinksFile = "GroupLink.txt";
        var whitelistLinks = new HashSet<string>();
        var joinedGroupIds = new HashSet<long>(); // 记录通过白名单加入的群组 ID
        
        if (File.Exists(groupLinksFile))
        {
            var lines = File.ReadAllLines(groupLinksFile);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed.StartsWith("https://t.me/"))
                {
                    whitelistLinks.Add(trimmed);
                }
            }
            Logger.Information("📋 从 GroupLink.txt 读取到 {Count} 个群组链接", whitelistLinks.Count);
        }
        else
        {
            Logger.Warning("⚠️ 未找到 GroupLink.txt 文件，将监控所有群组");
        }

        // 尝试加入 GroupLink.txt 中的群组
        if (whitelistLinks.Any())
        {
            Logger.Information("🔗 开始加入白名单群组...");
            var successCount = 0;
            
            foreach (var link in whitelistLinks)
            {
                try
                {
                    // 提取邀请哈希
                    string inviteHash = "";
                    if (link.Contains("/+"))
                    {
                        inviteHash = link.Split("/+")[1];
                    }
                    else if (link.Contains("/joinchat/"))
                    {
                        inviteHash = link.Split("/joinchat/")[1];
                    }
                    else
                    {
                        // 公开频道链接
                        var username = link.Replace("https://t.me/", "");
                        var resolved = await _client.Contacts_ResolveUsername(username);
                        if (resolved.Chat is Channel channel && channel.IsGroup)
                        {
                            var groupId = -1000000000000 - channel.id;
                            joinedGroupIds.Add(groupId);
                            Logger.Information("✅ 已加入公开群组: {Title}", channel.title);
                            successCount++;
                        }
                        await Task.Delay(500);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(inviteHash))
                    {
                        // 检查邀请链接
                        var chatInvite = await _client.Messages_CheckChatInvite(inviteHash);
                        
                        if (chatInvite is ChatInvite invite)
                        {
                            // 还未加入，尝试加入
                            var updates = await _client.Messages_ImportChatInvite(inviteHash);
                            
                            // 从更新中提取群组 ID
                            if (updates.Chats.Count > 0)
                            {
                                var chat = updates.Chats.Values.First();
                                long groupId = 0;
                                
                                if (chat is Channel channel)
                                {
                                    groupId = -1000000000000 - channel.id;
                                    Logger.Information("✅ 成功加入群组: {Title} (ID: {Id})", channel.title, groupId);
                                }
                                else if (chat is Chat groupChat)
                                {
                                    groupId = -groupChat.id;
                                    Logger.Information("✅ 成功加入群组: {Title} (ID: {Id})", groupChat.title, groupId);
                                }
                                
                                if (groupId != 0)
                                {
                                    joinedGroupIds.Add(groupId);
                                    successCount++;
                                }
                            }
                        }
                        else if (chatInvite is ChatInviteAlready alreadyJoined)
                        {
                            // 已经加入
                            var chat = alreadyJoined.chat;
                            long groupId = 0;
                            
                            if (chat is Channel channel)
                            {
                                groupId = -1000000000000 - channel.id;
                                Logger.Debug("已在群组中: {Title} (ID: {Id})", channel.title, groupId);
                            }
                            else if (chat is Chat groupChat)
                            {
                                groupId = -groupChat.id;
                                Logger.Debug("已在群组中: {Title} (ID: {Id})", groupChat.title, groupId);
                            }
                            
                            if (groupId != 0)
                            {
                                joinedGroupIds.Add(groupId);
                                successCount++;
                            }
                        }
                    }
                    
                    await Task.Delay(1000); // 避免请求过快
                }
                catch (Exception ex)
                {
                    Logger.Debug("处理群组链接失败 {Link}: {Error}", link, ex.Message);
                }
            }
            
            Logger.Information("✅ 成功处理 {Success}/{Total} 个白名单群组", successCount, whitelistLinks.Count);
        }

        // 筛选群组并同步到服务器
        var targetGroups = new List<GroupSyncDto>();
        
        foreach (var (id, chat) in dialogs.chats)
        {
            string? title = null;
            string? username = null;
            long groupId = 0;

            if (chat is Channel channel && channel.IsGroup)
            {
                title = channel.title;
                username = channel.username;
                groupId = -1000000000000 - channel.id;
            }
            else if (chat is Chat groupChat)
            {
                title = groupChat.title;
                groupId = -groupChat.id;
            }

            if (title != null)
            {
                bool shouldMonitor = false;
                
                // 如果有白名单，只监控白名单中的群组
                if (joinedGroupIds.Any())
                {
                    shouldMonitor = joinedGroupIds.Contains(groupId);
                }
                else
                {
                    // 没有白名单，监控所有包含"公群"或"扫雷"的群组（降级方案）
                    shouldMonitor = title.Contains("公群") || title.Contains("扫雷");
                }
                
                if (shouldMonitor)
                {
                    Logger.Information("✅ 监控群组: {Title} (ID: {Id})", title, groupId);
                    targetGroups.Add(new GroupSyncDto { GroupId = groupId, Title = title });
                    
                    // 添加到监控列表
                    _monitorGroups[groupId] = title;
                }
            }
        }

        // 同步群组到服务器
        if (targetGroups.Count > 0)
        {
            Logger.Information("正在同步 {Count} 个群组到服务器...", targetGroups.Count);
            await _backendClient.SyncGroupsAsync(targetGroups);
        }
        else
        {
            Logger.Warning("⚠️ 未发现任何符合条件的群组");
        }

        // 在获取完群组信息后再订阅消息
        _client.OnUpdates += OnUpdatesAsync;
        
        Logger.Information("📡 开始监控 {Count} 个群组的消息...", _monitorGroups.Count);
    }

    private async Task OnUpdatesAsync(IObject updates)
    {
        if (updates is not UpdatesBase updatesBase)
            return;
            
        foreach (var update in updatesBase.UpdateList)
        {
            try
            {
                await ProcessUpdateAsync(update);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "处理更新时发生异常");
            }
        }
    }

    private async Task ProcessUpdateAsync(Update update)
    {
        // 处理新消息
        if (update is UpdateNewMessage { message: Message message })
        {
            await ProcessMessageAsync(message);
        }
        // 处理频道/超级群组消息
        else if (update is UpdateNewChannelMessage { message: Message channelMessage })
        {
            await ProcessMessageAsync(channelMessage);
        }
    }

    private async Task ProcessMessageAsync(Message message)
    {
        if (string.IsNullOrEmpty(message.message))
            return;

        // 获取群组信息
        long groupId = 0;
        string groupName = "未知群组";

        if (message.peer_id is PeerChannel peerChannel)
        {
            groupId = -1000000000000 - peerChannel.channel_id;
        }
        else if (message.peer_id is PeerChat peerChat)
        {
            groupId = -peerChat.chat_id;
        }
        else
        {
            // 不是群组消息，跳过
            return;
        }

        // 检查是否是监控的群组（如果配置为空则监控所有群组）
        if (_monitorGroups.Count > 0 && !_monitorGroups.ContainsKey(groupId))
        {
            return;
        }

        if (_monitorGroups.TryGetValue(groupId, out var name))
        {
            groupName = name;
        }

        // 解析消息
        var result = _parser.TryParse(message.message, groupId, groupName, message.id);
        if (result == null)
            return;

        // 检查是否已处理过该期号
        if (_processedPeriods.Contains(result.PeriodId))
        {
            Logger.Debug("期号已处理过，跳过: {PeriodId}", result.PeriodId);
            return;
        }

        _processedPeriods.Add(result.PeriodId);

        // 限制缓存大小，防止内存泄漏
        if (_processedPeriods.Count > 10000)
        {
            _processedPeriods.Clear();
            Logger.Information("已清理期号缓存");
        }

        // 上报到后端
        await _backendClient.ReportAsync(result);

        // 触发事件通知（可扩展）
        OnLotteryResultCollected(result);
    }

    /// <summary>
    /// 开奖结果采集完成事件
    /// </summary>
    public event Action<LotteryReportDto>? LotteryResultCollected;

    protected virtual void OnLotteryResultCollected(LotteryReportDto result)
    {
        LotteryResultCollected?.Invoke(result);
    }

    private static string PromptInput(string prompt)
    {
        Console.Write(prompt);
        Console.ForegroundColor = ConsoleColor.Cyan;
        var input = Console.ReadLine() ?? string.Empty;
        Console.ResetColor();
        return input;
    }

    /// <summary>
    /// 安全输入密码（显示为星号）
    /// </summary>
    private static string PromptPassword(string prompt)
    {
        Console.Write(prompt);
        Console.ForegroundColor = ConsoleColor.Cyan;
        
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        
        Console.ResetColor();
        return password.ToString();
    }

    /// <summary>
    /// 隐藏手机号中间几位，如 +8613****8000
    /// </summary>
    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 8)
            return phone;
        
        // 保留前4位和后4位
        var prefix = phone[..Math.Min(5, phone.Length - 4)];
        var suffix = phone[^4..];
        return $"{prefix}****{suffix}";
    }
}

/// <summary>
/// 监控群组配置
/// </summary>
public class MonitorGroupConfig
{
    public long GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
}
