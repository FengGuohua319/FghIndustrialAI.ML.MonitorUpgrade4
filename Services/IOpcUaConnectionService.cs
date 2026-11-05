/*
 * 内容：OPC UA 连接服务接口
 * 创建时间：[2026-10-19 19:55:35]
 * 创建人：FengGuohua
 */
using FghIndustrialAI.ML.MonitorUpgrade4.Options;
using FghIndustrialAI.ML.MonitorUpgrade4.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Services
{
    /// <summary>
    /// OPC UA 连接服务接口。职责单一：连接、读取、断开。
    /// 不负责异常检测、不负责报警节流。
    /// </summary>
    public interface IOpcUaConnectionService : IAsyncDisposable
    {
        bool IsConnected { get; }
        IReadOnlyList<string> MonitoredNodes { get; }

        void ConfigureNodes(IEnumerable<string> nodeIds);
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SensorReading>> ReadAllAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 停用
    /// </summary>
    public interface IOpcUaConnectionService123
    {
        bool IsConnected { get; }

        IReadOnlyCollection<string> SubscribedNodeIds { get; }

        Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default);
        Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 真实/模拟统一入口，按 UseSimulator 分支
        /// </summary>
        /// <param name="options"></param>
        /// <param name="cToken"></param>
        /// <returns></returns>
        Task<IReadOnlyList<Sample>> ReadSamplesAsync(MonitoringOptions options, CancellationToken cToken = default);

        /// <summary>
        /// 动态更新订阅的节点列表
        /// </summary>
        /// <param name="newNodeIds"></param>
        /// <param name="cToken"></param>
        /// <returns></returns>
        Task SyncSubscriptionAsync(IEnumerable<string> newNodeIds, CancellationToken cToken = default);
    }

}
