/*
 * 内容：封装 OPC UA 连接、读取、订阅
 * 创建时间：[2026-09-25 20:42:54]
 * 创建人：FengGuohua
 */
using FghIndustrialAI.ML.Monitor.Extensions;
using FghIndustrialAI.ML.Monitor.Models;
using FghIndustrialAI.ML.Monitor.Options;
using FghIndustrialAI.ML.Monitor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace FghIndustrialAI.ML.Monitor.Services
{
    /// <summary>
    /// 真实OPC UA 客户端
    /// [2026-09-25 FengGuohua]
    /// </summary>
    public class OpcUaMonitorService : BackgroundService
    {
        readonly IOpcUaConnectionService _connectionService;
        readonly IOptionsMonitor<MonitoringOptions> _monitoringOptions;
        readonly IAnomalyDetectionService _anomalyService;
        readonly ILogger<OpcUaMonitorService> _logger;

        CancellationTokenSource? _cTokenSource;
        IDisposable? _configReload;
        int ReconnectDelaySeconds = 3000;

        /// <summary>
        /// [2026-09-22 15:09:09 FengGuohua]
        /// </summary>
        /// <param name="logger"></param>
        public OpcUaMonitorService(IOpcUaConnectionService connectionService,
            IAnomalyDetectionService anomalyService,
            IOptionsMonitor<MonitoringOptions> monitoringOptions,
            ILogger<OpcUaMonitorService> logger)
        {
            _connectionService = connectionService;
            _anomalyService = anomalyService;
            _monitoringOptions = monitoringOptions;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //_logger.LogInformation("监测服务 OpcUaMonitorService 启动");
            Console.WriteLine(">>> 配置加载完成，准备运行...");

            _cTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            // 配置热重载：订阅配置 MonitoringOptions 变更时，重新订阅节点
            _configReload = _monitoringOptions.OnChange(OnMonitoringOptionsChanged);

            // 主循环：连接 -> 采集 -> 断线重连(异常检测) -> 日志记录
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await _connectionService.EnsureConnectedAsync(_cTokenSource.Token))
                    {
                        _logger.LogWarning("OpcUaMonitorService 无法连接到 OPC UA 服务器，{Delay}s 后重试", ReconnectDelaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), stoppingToken);
                        continue;
                    }
                    await CollectLoopAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("OpcUaMonitorService 收到取消请求，正在停止...");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OpcUaMonitorService 执行过程中发生异常，准备重连");
                    await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), stoppingToken);
                }
            }
            _logger.LogInformation("监控服务 OpcUaMonitorService 停止，活跃节点数 ={Count}", _anomalyService.ListNodes().Count);
        }

        void OnMonitoringOptionsChanged(MonitoringOptions newOptions)
        {
            if (_cTokenSource != null)
            {
                _cTokenSource.Cancel();
                _cTokenSource.Dispose();
                _cTokenSource = new CancellationTokenSource();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _connectionService.SyncSubscriptionAsync(newOptions.MonitoredNodes, _cTokenSource.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "OPC UA 同步配置失败");
                    }
                });
            }
        }

        async Task CollectLoopAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var options = _monitoringOptions.CurrentValue;
                var samples = await _connectionService.ReadSamplesAsync(options, stoppingToken);
                foreach (var sample in samples)
                {
                    ProcessSample(sample.NodeId, sample.Value);
                }
                await Task.Delay(options.IntervalMs, stoppingToken);
            }
        }

        void ProcessSample(string nodeId, object value)
        {
            // 1. 先喂数据 （这一步会写入 _window）
            var isAnomaly = _anomalyService.Observe(nodeId, (double)value);
            // 2. 再查统计（这一步才能查到）
            var stats = _anomalyService.GetStatistics(nodeId);
            // 3. 再做判断和打印
            if (stats == null)
            {
                /* 
                 * 极端情况： observe 刚创建 detector, GetStatistics 理论上有值 
                 * 兜底防御性写法 
                 */
                // _logger.LogDebug("统计快照暂不可用 | 节点={NodeId}", nodeId);
                Console.WriteLine($"统计快照暂不可用 | 节点={nodeId}");
            }
            else if (stats is { IsWarmingUp: true }) // == if(stats.IsWarmingUp)
            {
                /*
                 * 如果采样频率很高，预热日志会刷屏，尤其是预热时间较长的情况。
                 * 解决方案：
                 *         1）只在预热剩余采样点数为5的倍数时打印一次，或者预热剩余采样点数为1时打印一次。
                 *         2）将输出日志级别由 LogInformation 为 LogDebug，或者只在预热完成时打印一次。
                 */
                // if (stats.WarmupRemaining % 5 == 0 || stats.WarmupRemaining == 1)
                {
                    // 预热中：既不是“正常”，亦不是“异常”，预热中才需要体现进度
                    var progress = stats.WarmupRemaining == 0 ? 1.0 : (double)stats.SampleCount / (stats.SampleCount + stats.WarmupRemaining);
                    double dProgress = WindowStatisticsExtensions.GetWarmupProgress(stats);

                    _logger.LogDebug("模型预热中 | 节点={NodeId} | 进度={Progress:P0} | 已采集= {Done}/{Total} ",
                        nodeId,
                        dProgress,
                        stats.SampleCount,
                        stats.SampleCount + stats.WarmupRemaining);
                }
            }
            else if (isAnomaly)
            {
                //_logger.LogWarning("⚠️异常预警 | 节点={NodeId} | 当前值= {Value:F3} | 均值={Mean:F3} | Z={ZScore:F2} | 累计={Count} ",
                //  reading.NodeId,
                //  reading.Value,
                //  stats.Mean,
                //  stats.ZScore,
                //  stats.PValue);
                Console.WriteLine($"⚠️异常预警 | 节点={nodeId} | 当前值= {value:F3} | 均值={stats.Mean:F3} | Z={stats.ZScore:F2} | 累计={stats.PValue} ");
            }
            else if (stats != null)
            {
                //_logger.LogDebug("√正常 | 节点={NodeId} | 值= {Value:F3} | 均值={Mean:F3} | Z={ZScore:F2} | 累计={Count}",
                //     reading.NodeId,
                //   reading.Value,
                //   stats.Mean,
                //   stats.ZScore,
                //   stats.PValue);

                Console.WriteLine($"√正常 | 节点={nodeId} | 值= {value:F3} | 均值={stats.Mean:F3} | Z={stats.ZScore:F2} | P={stats.PValue:F3}");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OpcUaMonitorService 停止中...");
            _configReload?.Dispose();
            _cTokenSource?.Cancel();
            _configReload?.Dispose();
            await base.StopAsync(cancellationToken);
            await _connectionService.DisconnectAsync(cancellationToken);
        }
    }
}
