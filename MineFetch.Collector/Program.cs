using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MineFetch.Collector.Services;
using Serilog;
using System.Text;

namespace MineFetch.Collector;

class Program
{
    static async Task Main(string[] args)
    {
        // 设置控制台编码为 UTF-8，解决中文乱码问题
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        
        // 获取exe所在目录作为基路径（而非当前工作目录）
        var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        
        // 配置 Serilog
        var configuration = new ConfigurationBuilder()
            .SetBasePath(exeDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        try
        {
            Log.Information("========================================");
            Log.Information("  MineFetch 扫雷数据采集器 v1.0");
            Log.Information("========================================");
            Log.Information("启动时间: {Time}", DateTime.Now);

            var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    // 注册服务
                    services.AddSingleton<CollectorMessageParser>();
                    services.AddSingleton<BackendClient>();
                    services.AddHostedService<TelegramCollector>();
                })
                .Build();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "应用程序异常终止");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
