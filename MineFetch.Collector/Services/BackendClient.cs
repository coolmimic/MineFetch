using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using MineFetch.Entities.DTOs;
using Serilog;

namespace MineFetch.Collector.Services;

/// <summary>
/// 后端 API 客户端 - 将采集到的数据上报到后端服务器
/// </summary>
public class BackendClient : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<BackendClient>();
    private readonly HttpClient _httpClient;
    private readonly string _reportEndpoint;
    private readonly bool _enabled;

    public BackendClient(IConfiguration configuration)
    {
        var section = configuration.GetSection("Backend");
        var baseUrl = section["BaseUrl"] ?? "http://localhost:5000";
        _reportEndpoint = section["ReportEndpoint"] ?? "/api/lottery/report";
        _enabled = section.GetValue<bool>("Enabled", false);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        Logger.Information("后端客户端初始化: BaseUrl={BaseUrl}, Enabled={Enabled}", baseUrl, _enabled);
    }

    /// <summary>
    /// 上报开奖结果到后端
    /// </summary>
    public async Task<bool> ReportAsync(LotteryReportDto dto, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            Logger.Debug("后端上报已禁用，跳过上报: {PeriodId}", dto.PeriodId);
            return true;
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(_reportEndpoint, dto, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                Logger.Information("✅ 上报成功: 期号={PeriodId}", dto.PeriodId);
                return true;
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.Warning("❌ 上报失败: 期号={PeriodId}, 状态码={StatusCode}, 响应={Response}",
                    dto.PeriodId, response.StatusCode, content);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ 上报异常: 期号={PeriodId}", dto.PeriodId);
            return false;
        }
    }

    /// <summary>
    /// 同步群组列表到后端
    /// </summary>
    public async Task SyncGroupsAsync(List<GroupSyncDto> groups, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            Logger.Debug("后端上报已禁用，跳过群组同步");
            return;
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/groups/sync", groups, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.Information("✅ 群组同步成功: {Count} 个群组", groups.Count);
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.Warning("❌ 群组同步失败: 状态码={StatusCode}, 响应={Response}",
                    response.StatusCode, content);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "❌ 群组同步异常");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
