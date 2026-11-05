// See https://aka.ms/new-console-template for more information
using FghIndustrialAI.ML.MonitorUpgrade4.Options;
using FghIndustrialAI.ML.MonitorUpgrade4.Background;
using FghIndustrialAI.ML.MonitorUpgrade4.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<MonitoringOptions>(ctx.Configuration.GetSection(MonitoringOptions.SectionName));
        services.Configure<AnomalyDetectionOptions>(ctx.Configuration.GetSection(AnomalyDetectionOptions.SectionName));

        var monitoring = ctx.Configuration
            .GetSection(MonitoringOptions.SectionName)
            .Get<MonitoringOptions>() ?? new MonitoringOptions();

        // 根据配置选择真实的或模拟的 OPC UA 连接服务
        if (monitoring.UseSimulator)
        {
            services.AddSingleton<IOpcUaConnectionService, SimulatedOpcUaConnectionService>();
            Console.WriteLine(">> 运行模式：模拟 OPC UA 连接服务");
        }
        else
        {
            services.AddSingleton<IOpcUaConnectionService, OpcUaConnectionService>();
            Console.WriteLine($">> 运行模式：真实 OPC UA 连接服务 → {monitoring.OpcUaEndpoint}");
        }

        services.AddSingleton<IAnomalyDetectionService, AnomalyDetectionService>();
        services.AddHostedService<MonitoringBackgroundService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss.fff ";
        });
        logging.SetMinimumLevel(LogLevel.Debug);
    })
    .Build();

Console.WriteLine(">>> 配置加载完成，准备运行...");

// 让程序一直运行
await host.RunAsync(); 
