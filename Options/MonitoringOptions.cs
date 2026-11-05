using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Options
{
    public class MonitoringOptions
    {
        public const string SectionName = "Monitoring";


        public string OpcUaEndpoint { get; set; } = "opc.tcp://localhost:4840";
        /// <summary>
        /// 监控节点数量
        /// </summary>
        public string[] MonitoredNodes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 采样间隔
        /// </summary>
        public int IntervalMs { get; set; } = 1000;

        /// <summary>
        /// 
        /// </summary>
        public bool UseSimulator { get; set; } = true;

        /// <summary>保留字段，当前 IidSpike 实现不使用</summary>
        public int WindowSize { get; set; } = 30;

        /// <summary>保留字段，当前 IidSpike 实现不使用</summary>
        public double ZScoreThreshold { get; set; } = 3.0;


        // public double Confidence = 99.9;


    }
}
