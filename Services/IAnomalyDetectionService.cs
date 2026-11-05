using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Services
{
    public interface IAnomalyDetectionService : IDisposable
    {
        /// <summary>
        /// 观测一个值。冷启动期返回 IsWarmup=true，此时 p-value 无效。
        /// </summary>
        DetectionResult Observe(string nodeId, double value);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="IsWarmup">是否是预热状态</param>
    /// <param name="IsSpike">是否是尖峰</param>
    /// <param name="PValue"> ML.NET计算出的 p-value (预热期为 -1 , 表示无效) </param>
    /// <param name="RawScore"> 代表的是 ML.NET 内部计算出来的原始异常分数。在 IidSpikeDetector 中，这个值实际上就等于当前输入的值（因为你是把 Value 喂进去的）。</param>
    /// <param name="SampleCount"> 当前窗口滑动样本数 （表示从程序启动到现在，该节点总共发来了多少次数据）</param>
    /// <param name="RequiredWarmupSamples">距离预热完成还差多少个样本（完成时为0）</param>
    /// 【FghIndustrialAI.ML.MonitorUpgrade4版新增】 [2026-11-03 13:59:00 FengGuohua]
    /// <param name="WindowSampleCount"> 滑动窗口样本数（表示当前统计窗口中容纳了多少个样本） </param>
    /// <param name="WindowMean"> 窗口均值 </param>
    /// <param name="WindowStdDev"> 窗口标准差 </param>
    /// <param name="LastValue">最近一次采样值</param>
    /// <param name="ZScore"> 基于窗口的Z分数 </param>
    public record DetectionResult(
        bool IsWarmup,
        bool IsSpike,
        double PValue,
        double RawScore,
        int SampleCount,
        int RequiredWarmupSamples,
        int WindowSampleCount,
        double WindowMean,
        double WindowStdDev,
        double LastValue,
        double ZScore)
    {
        /// <summary>
        /// 距离预热完成还差多少个样本（完成时为0）
        /// </summary>
        public int RemainingWarmupSamples => IsWarmup ? Math.Max(0, RequiredWarmupSamples - SampleCount) : 0;

        /// <summary>预热进度（0.0 - 1.0）</summary>
        public double WarmupProgress => RequiredWarmupSamples == 0 ? 1.0 : Math.Min(1.0, (double)SampleCount / RequiredWarmupSamples);

        /// <summary>
        /// 为实现“可视化滑动窗口” 【FghIndustrialAI.ML.MonitorUpgrade4版新增】 [2026-11-03 19:55:00 FengGuohua]
        /// 增加一个属性，用来传窗口里的具体数据
        /// </summary>
        public IReadOnlyList<double> WindowData { get; init; } = new List<double>();
    };

}
