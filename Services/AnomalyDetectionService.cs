/*
 * 内容：用 ML.NET 做异常检测
 * 创建时间：[2026-08-15 20:29:46]
 * 创建人：FengGuohua
 */
using FghIndustrialAI.ML.MonitorUpgrade4.Options;
using FghIndustrialAI.ML.MonitorUpgrade4.Models;
using Microsoft.Extensions.ML;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;


namespace FghIndustrialAI.ML.MonitorUpgrade4.Services
{
    /// <summary>
    /// 基于 ML.NET IidSpikeDetector（Independent and Identically Distributed Spike Detector 独立同分布尖峰检测器） 的异常检测服务。
    /// -------------------------------------------------------------------------------
    /// FghIndustrialAI.ML.MonitorUpgrade2版新增 [2026-11-02 20:14:06 FengGuohua]
    /// 关键设计（对应之前提出的 5 个问题）：
    /// 1. PredictionEngine 非线程安全 → 每个节点独立 PredictionEnginePool
    /// 2. 冷启动 p-value 无效 → SampleCount <= EffectiveWarmupSamples 时返回 IsWarmup=true
    /// 3. Confidence 是百分比 → 配置注释明确 0-100，直接传给 IidSpikeDetector
    /// 4. PvalueHistoryLength 是 p-value 窗口 → 配置项直接对应
    /// 5. 节点检测器生命周期 → 每个节点独立持有池，随服务 Dispose 统一释放
    /// -------------------------------------------------------------------------------
    /// FghIndustrialAI.ML.MonitorUpgrade3版新增 [2026-11-03 13:59:00 FengGuohua]
    /// 为每个节点新增一个 Queue<double> 维护滑动窗口，并计算统计量。
    /// </summary>
    public class AnomalyDetectionService : IAnomalyDetectionService
    {
        private readonly AnomalyDetectionOptions _options;
        private readonly MLContext _mlContext;
        private readonly ConcurrentDictionary<string, NodeDetectorState> _detectors = new();
        private bool _disposed;

        public AnomalyDetectionService(IOptions<AnomalyDetectionOptions> options)
        {
            _options = options.Value;
            _mlContext = new MLContext(seed: 42); // 固定种子，保证可复现
        }

        public DetectionResult Observe(string nodeId, double value)
        {
            var state = _detectors.GetOrAdd(nodeId, CreateDetectorState);

            /*
             * FghIndustrialAI.ML.MonitorUpgrade3版新增 [2026-11-03 13:59:00 FengGuohua]
             * 1. 更新我自己的滑动窗口（用于纯统计学展示）
             */
            state.Window.Enqueue(value);
            if (state.Window.Count > _options.EffectiveWarmupSamples)  // 复用2窗口大小或单独配置
            {
                state.Window.Dequeue();
            }

            /*
             * FghIndustrialAI.ML.MonitorUpgrade3版新增 [2026-11-03 13:59:00 FengGuohua]
             *  2. 计算统计特征
             */
            var windowData = state.Window.ToArray();
            var mean = windowData.Length > 0 ? windowData.Average() : 0;
            var variance = windowData.Length > 1 ? windowData.Select(v => Math.Pow(v - mean, 2)).Sum() / (windowData.Length - 1) : 0;
            var stdDev = Math.Sqrt(variance);
            var zScore = stdDev < 1e-6 ? 0 : (value - mean) / stdDev;

            state.SampleCount++;

            /*
             * FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎[2026-11-05 10:56:00 FengGuohua]
             * 【核心升级】：使用 ThreadLocal 的 Value 获取当前线程独有的引擎
             */
            //var engine = state.Engine.Value;
            var engine = state.Engine.Value ?? throw new InvalidOperationException($"节点 {nodeId} 的时序预测引擎初始化失败，无法执行预测。");

            /*
             * 3. 执行 ML.NET 预测
             * 冷启动保护：样本不足时 p-value 无效(由默认值填充，不可用于判断)
             */
            if (state.SampleCount <= _options.EffectiveWarmupSamples)
            {
                /*
                 * // 预热期仍需喂数据，但不需要判断结果
                   //state.PredictSafe(new SpikeInput { Value = (float)value });
                 * FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎[2026-11-05 10:56:00 FengGuohua]
                 */
                engine.Predict(new SpikeInput { Value = (float)value });

                return new DetectionResult(
                    IsWarmup: true,
                    IsSpike: false,
                    PValue: 1.0,  // 预热期 p-value 无效，统一给 1.0
                    RawScore: 0,
                    SampleCount: state.SampleCount,
                    RequiredWarmupSamples: _options.EffectiveWarmupSamples,
                    WindowSampleCount: windowData.Length,
                    WindowMean: mean,
                    WindowStdDev: stdDev,
                    LastValue: value,
                    ZScore: zScore)
                {
                    WindowData = windowData // 把数组塞进去
                };
            }

            /*
             * var prediction = state.PredictSafe(new SpikeInput { Value = (float)value });
             *  FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎[2026-11-05 10:56:00 FengGuohua]
             *  已经获取引擎了，执行预测
             */
            var prediction = engine.Predict(new SpikeInput { Value = (float)value });

            return new DetectionResult(
                IsWarmup: false,
                IsSpike: prediction.Alert == 1,
                PValue: prediction.PValue,
                RawScore: prediction.Score,
                SampleCount: state.SampleCount,
                RequiredWarmupSamples: _options.EffectiveWarmupSamples,
                WindowSampleCount: windowData.Length,
                WindowMean: mean,
                WindowStdDev: stdDev,
                LastValue: value,
                ZScore: zScore)
            {
                WindowData = windowData // 把数组塞进去
            };
        }

        private NodeDetectorState CreateDetectorState(string nodeId)
        {
            /*
             * ML.NET 黑盒算法，顾名思义：
             *   当我们把数据喂给它，它内部会做很多极其复杂的数学运算（马尔可夫链、P-value 计算、双尾检验等），最后它只吐给你三个值：
             *   Alert（0 或 1）：告不报警？
             *   Score：原始分数。
             *   PValue：概率值。
             */
            var pipeline = _mlContext.Transforms.DetectIidSpike(
                outputColumnName: nameof(SpikePrediction.Prediction),
                inputColumnName: nameof(SpikeInput.Value),
                confidence: _options.Confidence,                    // 99.9%  
                pvalueHistoryLength: _options.PvalueHistoryLength   // 15
                );

            var emptyData = _mlContext.Data.LoadFromEnumerable(Array.Empty<SpikeInput>());
            var model = pipeline.Fit(emptyData);

            /*
             *  // // 关键修复：时序检测必须用 CreateTimeSeriesEngine
             *  // var engine = model.CreateTimeSeriesEngine<SpikeInput, SpikePrediction>(_mlContext);
             *  // return new NodeDetectorState(engine);
             *  FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎[2026-11-05 10:56:00 FengGuohua]
             * 【核心升级点】 构建 ThreadLocal 容器，每个线程拥有自己的预测引擎副本
             */
            var threadLocalEngine = new ThreadLocal<TimeSeriesPredictionEngine<SpikeInput, SpikePrediction>>(
                () => model.CreateTimeSeriesEngine<SpikeInput, SpikePrediction>(_mlContext),
                trackAllValues: true
                );  // 设为true 以便 Dispose 时能清理所有线程的实例
            return new NodeDetectorState(threadLocalEngine);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var state in _detectors.Values)
            {
                /*
                 * state.Engine.Dispose();
                 * FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎 [2026-11-05 10:56:00 FengGuohua]
                 * 释放所有线程创建的引擎
                 */
                foreach (var engine in state.Engine.Values)
                {
                    engine?.Dispose();
                }

            }
            _detectors.Clear();
        }

        private class NodeDetectorState
        {
            private readonly object _lock = new();
            /*
             * public TimeSeriesPredictionEngine<SpikeInput, SpikePrediction> Engine { get; }
             * FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎 [2026-11-05 10:56:00 FengGuohua]
             */
            public ThreadLocal<TimeSeriesPredictionEngine<SpikeInput, SpikePrediction>> Engine { get; }
            public int SampleCount { get; set; }

            /*
             * FghIndustrialAI.ML.MonitorUpgrade3版新增 [2026-11-03 13:59:00 FengGuohua]
             * 为每个节点新增一个 Queue<double> 维护滑动窗口，并计算统计量。
             */
            public Queue<double> Window { get; } = new();

            /*
             * public NodeDetectorState(TimeSeriesPredictionEngine<SpikeInput, SpikePrediction> engine) => Engine = engine; 
             * FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎 [2026-11-05 10:56:00 FengGuohua]
             */
            public NodeDetectorState(ThreadLocal<TimeSeriesPredictionEngine<SpikeInput, SpikePrediction>> engine) => Engine = engine;

            /*
             *  // 加锁保证 PredictionEngine 的线程安全
                public SpikePrediction PredictSafe(SpikeInput input)
                {
                    lock (_lock)
                    {
                        return Engine.Predict(input);
                    }
                }
             * FghIndustrialAI.ML.MonitorUpgrade4版 【核心升级点】升级为 ThreadLocal 多线程并发安全引擎 [2026-11-05 10:56:00 FengGuohua]
             * 【核心升级点】 弃用 方法PredictSafe()
             */
        }
    }
}
