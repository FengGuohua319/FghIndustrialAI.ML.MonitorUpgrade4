using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Options
{

    public class AnomalyDetectionOptions
    {
        public const string SectionName = "AnomalyDetection";

        /// <summary>
        /// p-value 计算的滑动窗口长度，直接传给 IidSpikeDetector 的 pvalueHistoryLength。
        /// 同时作为冷启动所需的样本数下限（与 WarmupSamples 取较大值）。
        /// </summary>
        public int PvalueHistoryLength { get; set; } = 15;

        /// <summary>
        /// 尖峰检测置信度，百分比范围 0-100。越高越保守。
        /// 注意：不是 0-1 的小数，99.9 表示 99.9%。
        /// </summary>
        public double Confidence { get; set; } = 99.9;

        /// <summary>
        /// 保留字段。ML.NET 的 DetectIidSpike 目前只支持双侧检测，此配置暂不生效。
        /// </summary>
        public string Side { get; set; } = "TwoSided";

        /// <summary>
        /// 冷启动预热样本数。样本数未达到此值时，p-value 无效，不输出异常判断。
        /// 实际生效值为 Max(WarmupSamples, PvalueHistoryLength)。
        /// </summary>
        public int WarmupSamples { get; set; } = 15;

        /// <summary>
        /// 同一节点连续报警的最小间隔秒数
        /// </summary>
        public int AlertThrottleSeconds { get; set; } = 30;

        /// <summary>
        /// FghIndustrialAI.ML.MonitorUpgrade4版新增 [2026-11-02 20:14:06 FengGuohua]
        /// 【完善】 是否在控制台打印预热进度
        /// </summary>
        public bool ShowWarmupProgress { get; set; } = true;

        /// <summary>
        /// FghIndustrialAI.ML.MonitorUpgrade4版新增 [2026-11-02 20:14:06 FengGuohua]
        /// 【完善】 预热日志打印间隔（每 N 个样本打印一次）
        /// </summary>
        public int WarmupLogInterval { get; set; } = 5;

        /// <summary>
        /// FghIndustrialAI.ML.MonitorUpgrade4版新增 [2026-11-03 13:59:00 FengGuohua]
        /// 统计数据的打印间隔（每 N 个样本打印一次）
        /// </summary>
        public int StatsLogInterval { get; set; } = 5;

        /// <summary>
        /// 实际冷启动所需的最小样本数
        /// </summary>
        public int EffectiveWarmupSamples => Math.Max(WarmupSamples, PvalueHistoryLength);

        /// <summary>
        /// FghIndustrialAI.ML.MonitorUpgrade4版新增 [2026-11-03 13:59:00 FengGuohua]
        /// 根据置信度计算Z-Score的阈值（近乎双尾正态分布）。
        /// 例如：Confidence = 99.9% 对应约3.29。
        /// 虽然 ML.NET内部用了更复杂的算法，但显式暴漏这个阈值有助于现场理解。
        /// </summary>
        public double ZScoreThreshold
        {
            get
            {
                return Confidence switch
                {
                    >= 99.99 => 3.89,
                    >= 99.9 => 3.29,
                    >= 99.0 => 2.58,
                    >= 95.0 => 1.96,
                    _ => 3.0 // 默认
                };
            }
        }
    }
}
