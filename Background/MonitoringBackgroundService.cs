/*
 * 内容： 模拟数据源，后台采用固定线程的工业采集，即单循环采集数据 → AI分析 → 预警。重点 
 * 创建时间：[2026-09-23 08:59:49]
 * 创建人：FengGuohua
 */
using FghIndustrialAI.ML.MonitorUpgrade4.Options;
using FghIndustrialAI.ML.MonitorUpgrade4.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.Transforms.TimeSeries;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Background
{
    /// <summary>
    /// 后台调度服务（预热日志 + 结构化日志 + 节流）
    /// FghIndustrialAI.ML.MonitorUpgrade3版新增 [2026-11-03 13:59:00 FengGuohua]
    /// 新增处理我要求的逻辑： 在 IsWarmup 之前查统计并打印，以及恢复定制化的启动日志并暴露 异常阈值
    /// </summary>
    public class MonitoringBackgroundService : BackgroundService
    {
        readonly IOpcUaConnectionService _opcUa;
        readonly IAnomalyDetectionService _ai;
        readonly ILogger<MonitoringBackgroundService> _logger;
        readonly MonitoringOptions _monitoring;
        readonly AnomalyDetectionOptions _anomaly;

        /// <summary>每个节点最后报警时间，用于节流</summary>
        readonly Dictionary<string, DateTime> _lastAlertTimes = new();

        /// <summary>每个节点是否已完成预热，用于只打印一次“预热完成”日志</summary>
        readonly HashSet<string> _warmupCompletedNodes = new();

        public MonitoringBackgroundService(
            IOpcUaConnectionService opcUa,
            IAnomalyDetectionService ai,
            IOptions<MonitoringOptions> monitoring,
            IOptions<AnomalyDetectionOptions> anomaly,
            ILogger<MonitoringBackgroundService> logger)
        {
            _opcUa = opcUa;
            _ai = ai;
            _logger = logger;
            _monitoring = monitoring.Value;
            _anomaly = anomaly.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _opcUa.ConfigureNodes(_monitoring.MonitoredNodes);

            if (!await _opcUa.ConnectAsync(stoppingToken))
            {
                _logger.LogError("OPC UA 连接失败，监控服务退出 | Endpoint={Endpoint}", _monitoring.OpcUaEndpoint);
                return;
            }

            /*
             *  FghIndustrialAI.ML.MonitorUpgrade3版 修改点：加入 ZScoreThreshold 的打印 [2026-11-03 13:59:00 FengGuohua]
             */
            //_logger.LogInformation(
            //    "监控启动 | NodeCount={NodeCount}个节点 | 采集间隔={IntervalMs}ms | PvalueHistoryLength={PvalueLength} | Confidence={Confidence}% | WarmupSamples={Warmup} | ThrottleSec={Throttle} | 异常阈值(Z-Score)={6:F2}",
            //    _monitoring.MonitoredNodes.Length, _monitoring.IntervalMs,
            //    _anomaly.PvalueHistoryLength, _anomaly.Confidence,
            //    _anomaly.EffectiveWarmupSamples, _anomaly.AlertThrottleSeconds,_anomaly.ZScoreThreshold);

            Console.WriteLine(
              string.Format(@"监控启动 | NodeCount={0}个节点 | 采集间隔={1}ms | PvalueHistoryLength={2} | Confidence={3}% | WarmupSamples={4} | ThrottleSec={5} | 异常阈值(Z-Score)={6:F2}",
               _monitoring.MonitoredNodes.Length,
               _monitoring.IntervalMs,
               _anomaly.PvalueHistoryLength,
               _anomaly.Confidence,
               _anomaly.EffectiveWarmupSamples,
               _anomaly.AlertThrottleSeconds,
               _anomaly.ZScoreThreshold)
              );

            if (_anomaly.Side != AnomalySide.TwoSided.ToString())
            {
                //_logger.LogWarning(
                //    "配置 Side={Side} 当前不被 IidSpikeDetector 支持，实际按双侧检测执行",
                //    _anomaly.Side);
                Console.WriteLine(
                  string.Format(@"配置 Side={0} 当前不被 IidSpikeDetector 支持，实际按双侧检测执行",
                  _anomaly.Side)
                  );
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var readings = await _opcUa.ReadAllAsync(stoppingToken);

                    foreach (var reading in readings)
                    {
                        // 一、先喂数据 （这一步会写入 _window）
                        var result = _ai.Observe(reading.NodeId, reading.Value);

                        /*
                         *  二、统计打印
                         *  FghIndustrialAI.ML.MonitorUpgrade3版 修改点：在 IsWarmup 之前先做统计打印,
                         *                                               通过配置 StatsLogInterval 控制打印频率
                         */
                        if (result.SampleCount % _anomaly.StatsLogInterval == 0)
                        {
                            // _logger.LogInformation(
                            //"统计特征 | NodeId={NodeId} | WinCount={WinCount} | WinMean={Mean:F3} | WinStdDev={StdDev:F3} | LastVal={LastVal:F3} | ZScore={ZScore:F2}",
                            //reading.NodeId, result.WindowSampleCount, result.WindowMean, result.WindowStdDev, result.LastValue, result.ZScore);
                            Console.WriteLine(
                           string.Format(@"统计特征 | NodeId={0} | 窗口样本数={1} | 窗口均值={2:F3} | 窗口标准差={3:F3} | 最近一次采样值={4:F3} | 基于窗口的Z分数={5:F2} | 窗口里的数据=[{6}] ",
                           reading.NodeId, result.WindowSampleCount, result.WindowMean, result.WindowStdDev, result.LastValue, result.ZScore,string.Join(",",result.WindowData.Select(v=>v.ToString("F2"))))
                           );
                        }

                        /*
                         * 三、再做判断和打印
                         */
                        // 1.冷启动预热阶段处理
                        if (result.IsWarmup)
                        {
                            LogWarmupProgress(reading.NodeId, result);
                            continue;
                        }

                        // 首次完成预热时打印一次
                        if (_warmupCompletedNodes.Add(reading.NodeId))
                        {
                            //_logger.LogInformation(
                            //    "预热完成 | NodeId={NodeId} | WarmupSamples={Warmup}",
                            //    reading.NodeId, result.RequiredWarmupSamples);

                            Console.WriteLine(
                                string.Format(@"预热完成 | NodeId={0} | 距离预热完成还差多少个样本={1}",
                                reading.NodeId, result.RequiredWarmupSamples)
                                );
                        }

                        // 2. 尖峰报警（带节流）
                        if (result.IsSpike)
                        {
                            if (!ShouldAlert(reading.NodeId))
                                continue;

                            //_logger.LogWarning(
                            //    "⚠️异常尖峰 | NodeId={NodeId} | Value={Value:F3} | PValue={PValue:F6} | Score={Score:F3} | SampleCount={SampleCount}",
                            //    reading.NodeId, reading.Value, result.PValue,
                            //    result.RawScore, result.SampleCount);

                            Console.WriteLine(
                                string.Format(@"⚠️异常尖峰 | NodeId={0} | Value={1:F3} | PValue={2:F6} | Score={3:F3} | SampleCount={4}",
                                reading.NodeId, reading.Value, result.PValue,
                                result.RawScore, result.SampleCount)
                                );
                        }
                        // 3. 正常数据监控（输出 p-value）
                        else
                        {
                            //_logger.LogDebug(
                            //    "√正常 | NodeId={NodeId} | Value={Value:F3} | PValue={PValue:F4} | WarmupRemaining={Remaining}",
                            //    reading.NodeId, reading.Value, result.PValue, result.RemainingWarmupSamples);

                            Console.WriteLine(
                                string.Format("√正常 | NodeId={0} | Value={1:F3} | PValue={2:F4} | WarmupRemaining={3}",
                                reading.NodeId, reading.Value, result.PValue, result.RemainingWarmupSamples)
                                );
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "监控循环异常");
                }

                await Task.Delay(_monitoring.IntervalMs, stoppingToken);
            }
        }

        /// <summary>
        /// 预热进度日志。根据配置的 WarmupLogInterval 决定打印频率。
        /// -- 预热进度日志。每 5 个样本打印一次，避免刷屏。
        /// </summary>
        private void LogWarmupProgress(string nodeId, DetectionResult result)
        {
            // 如果配置关闭了预热进度显示，直接返回
            if (!_anomaly.ShowWarmupProgress)
                return;

            // 根据配置的间隔打印
            if (result.SampleCount % _anomaly.WarmupLogInterval != 0)
                return;

            //_logger.LogInformation(
            //    "预热中 | NodeId={NodeId} | Sample={SampleCount}/{Required} | Progress={Progress:P0} | Remaining={Remaining}",
            //    nodeId, result.SampleCount, result.RequiredWarmupSamples,result.WarmupProgress，
            //    result.RemainingWarmupSamples);

            Console.WriteLine(
               string.Format("预热中 | NodeId={0} | Sample={1}/{2} | Progress={3:P0} | Remaining={4}",
                             nodeId,
                             result.SampleCount,
                             result.RequiredWarmupSamples,
                             result.WarmupProgress,
                             result.RemainingWarmupSamples)
           );
        }

        /// <summary>
        /// 节流：同一节点在 AlertThrottleSeconds 内只报警一次
        /// </summary>
        private bool ShouldAlert(string nodeId)
        {
            var now = DateTime.UtcNow;
            if (_lastAlertTimes.TryGetValue(nodeId, out var last) &&
                (now - last).TotalSeconds < _anomaly.AlertThrottleSeconds)
            {
                return false;
            }

            _lastAlertTimes[nodeId] = now;
            return true;
        }
    }
}
