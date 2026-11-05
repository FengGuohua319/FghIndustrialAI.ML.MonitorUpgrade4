/*
 * 内容：ML 输入
 * 创建时间：[2026-09-25 21:00:23]
 * 创建人：FengGuohua
 */
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Models
{
    /// <summary>
    /// --<summary>IidSpikeDetector 输入类型，必须包含 float 类型的 Value 属性</summary>
    /// </summary>
    // internal class TimeSeriesData // 时序数据
    public class SpikeInput
    {
        public float Value { get; set; }
    }

    /// <summary>
    /// IidSpikeDetector 输出。Prediction 数组为 [Alert, Score, PValue]
    /// </summary>
    public class SpikePrediction
    {
        public double[] Prediction { get; set; } = Array.Empty<double>();
        public int Alert => (int)Prediction[0];
        public double Score => Prediction[1];
        public double PValue => Prediction[2];
    }
}
