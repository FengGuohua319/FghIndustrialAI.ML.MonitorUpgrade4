/*
 * 内容：节点的最新统计快照扩展
 * 创建时间：[2026年10月18日 14:14:40]
 * 创建人：FengGuohua
 */
using FghIndustrialAI.ML.Monitor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.Monitor.Extensions
{
    public static class WindowStatisticsExtensions
    {
        /// <summary>
        /// 预热进度： 0.0 - 1.0，返回 1.0 表示预热完成
        /// </summary>
        /// <param name="stats"></param>
        /// <returns></returns>
        public static double GetWarmupProgress(this WindowStatistics stats)
        {
            if (!stats.IsWarmingUp)
            {
                return 1.0;
            }
            else
            {
                var total = stats.SampleCount + stats.WarmupRemaining;
                return total <= 0 ? 0.0 : (double)stats.SampleCount / total;
            }
        }
    }
}
