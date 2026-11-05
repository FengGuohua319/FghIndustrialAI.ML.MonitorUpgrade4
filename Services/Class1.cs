using FghIndustrialAI.ML.MonitorUpgrade2.Models;
using FghIndustrialAI.ML.MonitorUpgrade2.Options;
using FghIndustrialAI.ML.MonitorUpgrade2.Services;
using Microsoft.Extensions.ML;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using System.Collections.Concurrent;

namespace IndustrialAI.Monitor.Services;

using FghIndustrialAI.ML.MonitorUpgrade2.Options;
using FghIndustrialAI.ML.MonitorUpgrade2.Services;
/// <summary>
/// 基于 ML.NET IidSpikeDetector 的异常检测服务。
///
/// 关键设计（对应之前提出的 5 个问题）：
/// 1. PredictionEngine 非线程安全 → 每个节点独立 PredictionEnginePool
/// 2. 冷启动 p-value 无效 → SampleCount <= EffectiveWarmupSamples 时返回 IsWarmup=true
/// 3. Confidence 是百分比 → 配置注释明确 0-100，直接传给 IidSpikeDetector
/// 4. PvalueHistoryLength 是 p-value 窗口 → 配置项直接对应
/// 5. 节点检测器生命周期 → 每个节点独立持有池，随服务 Dispose 统一释放
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
        _mlContext = new MLContext();
    }

    public DetectionResult Observe(string nodeId, double value)
    {
        var state = _detectors.GetOrAdd(nodeId, CreateDetectorState);
        state.SampleCount++;

        if (state.SampleCount <= _options.EffectiveWarmupSamples)
        {
            state.Pool.Predict(new SpikeInput { Value = (float)value });
            return new DetectionResult(
                IsWarmup: true,
                IsSpike: false,
                PValue: 1.0, // 预热期 p-value 无效，统一给 1.0
                RawScore: 0,
                SampleCount: state.SampleCount,
                RequiredWarmupSamples: _options.EffectiveWarmupSamples);
        }

        var prediction = state.Pool.Predict(new SpikeInput { Value = (float)value });

        return new DetectionResult(
            IsWarmup: false,
            IsSpike: prediction.Alert == 1,
            PValue: prediction.PValue,
            RawScore: prediction.Score,
            SampleCount: state.SampleCount,
            RequiredWarmupSamples: _options.EffectiveWarmupSamples);
    }

    private NodeDetectorState CreateDetectorState(string nodeId)
    {
        var pipeline = _mlContext.Transforms.DetectIidSpike(
            outputColumnName: nameof(SpikePrediction.Prediction),
            inputColumnName: nameof(SpikeInput.Value),
            confidence: _options.Confidence,
            pvalueHistoryLength: _options.PvalueHistoryLength);

        var emptyData = _mlContext.Data.LoadFromEnumerable(Array.Empty<SpikeInput>());
        var model = pipeline.Fit(emptyData);
        var pool = _mlContext.Model.CreatePredictionEnginePool<SpikeInput, SpikePrediction>(model);

        return new NodeDetectorState(pool);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var state in _detectors.Values)
            state.Pool.Dispose();
        _detectors.Clear();
    }

    private class NodeDetectorState
    {
        public PredictionEnginePool<SpikeInput, SpikePrediction> Pool { get; }
        public int SampleCount { get; set; }
        public NodeDetectorState(PredictionEnginePool<SpikeInput, SpikePrediction> pool) => Pool = pool;
    }
}