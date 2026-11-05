/*
 * 内容： 演示模式 —— 模拟数据源，重点 
 * 创建时间：[2026-09-22 21:57:04]
 * 创建人：FengGuohua
 */
using FghIndustrialAI.ML.MonitorUpgrade4.Models;
using FghIndustrialAI.ML.MonitorUpgrade4.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Services
{

    /// <summary>
    /// 模拟 OPC UA 连接服务。与真实服务实现同一接口，可直接替换。
    /// 生成：正弦基线 + 噪声 + 周期性 3.5σ 持续尖峰（持续 2~3 个采样点）。
    /// </summary>
    public class SimulatedOpcUaConnectionService : IOpcUaConnectionService
    {
        private readonly ILogger<SimulatedOpcUaConnectionService> _logger;
        private readonly List<string> _monitoredNodes = new();
        private readonly Random _random = new();
        private int _tick;
        private int _spikeRemainingTicks;
        private double _spikeDirection;

        public bool IsConnected { get; private set; }
        public IReadOnlyList<string> MonitoredNodes => _monitoredNodes;

        public SimulatedOpcUaConnectionService(ILogger<SimulatedOpcUaConnectionService> logger)
            => _logger = logger;

        public void ConfigureNodes(IEnumerable<string> nodeIds)
        {
            _monitoredNodes.Clear();
            _monitoredNodes.AddRange(nodeIds);
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            _logger.LogInformation("[模拟] 模拟 OPC UA 连接已建立");
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<SensorReading>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("模拟连接未建立");

            var now = DateTime.UtcNow;
            var readings = _monitoredNodes
                .Select(nodeId => new SensorReading(nodeId, GenerateValue(nodeId), now))
                .ToList();

            _tick++;
            return Task.FromResult<IReadOnlyList<SensorReading>>(readings);
        }

        private double GenerateValue(string nodeId)
        {
            var (baseline, stdDev, frequency) = nodeId switch
            {
                var n when n.Contains("Temperature") => (75.0, 2.0, 0.08),
                var n when n.Contains("Pressure") => (1.5, 0.15, 0.12),
                var n when n.Contains("Current") => (12.0, 0.8, 0.18),
                _ => (50.0, 2.5, 0.10)
            };

            var noise = (_random.NextDouble() - 0.5) * stdDev * 0.5;
            var value = baseline + stdDev * Math.Sin(_tick * frequency) + noise;

            // 尖峰：3.5σ 偏移，持续 2~3 个采样点，模拟真实传感器故障
            if (_spikeRemainingTicks > 0)
            {
                value = baseline + _spikeDirection * stdDev * 3.5 + noise;
                _spikeRemainingTicks--;
            }
            else if (_tick > 0 && _tick % 20 == 0)
            {
                _spikeRemainingTicks = _random.Next(2, 4);
                _spikeDirection = _random.NextDouble() > 0.5 ? 1 : -1;
                value = baseline + _spikeDirection * stdDev * 3.5 + noise;
                _spikeRemainingTicks--;

                //_logger.LogDebug(
                //    "[模拟] 尖峰注入 | NodeId={NodeId} | 方向={Direction} | 幅度={Sigma}σ",
                //    nodeId, _spikeDirection > 0 ? "正向" : "负向", 3.5);
                Console.WriteLine(
                    string.Format("[模拟] 尖峰注入 | NodeId={0} | 方向={1} | 幅度={2}σ",
                   nodeId, _spikeDirection > 0 ? "正向" : "负向", 3.5)
                    );
            }

            return value;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
