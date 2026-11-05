/*
 * 内容：OPC UA 连接服务 
 * 创建时间：[2026-10-19 20:00:50]
 * 创建人：FengGuohua
 */
using FghIndustrialAI.ML.MonitorUpgrade4.Options;
using FghIndustrialAI.ML.MonitorUpgrade4.Services;
using FghIndustrialAI.ML.MonitorUpgrade4.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.MonitorUpgrade4.Services
{

    /// <summary>
    /// 真实 OPC UA 客户端连接服务。
    /// 每次 ReadAllAsync 按节点逐个读取，单节点失败不影响其他节点。
    /// </summary>
    public class OpcUaConnectionService : IOpcUaConnectionService
    {
        private readonly ILogger<OpcUaConnectionService> _logger;
        private readonly MonitoringOptions _options;
        private readonly List<string> _monitoredNodes = new();
        private readonly ApplicationConfiguration _appConfig;
        private Session? _session;

        public bool IsConnected => _session != null;
        public IReadOnlyList<string> MonitoredNodes => _monitoredNodes;

        public OpcUaConnectionService(
            IOptions<MonitoringOptions> options,
            ILogger<OpcUaConnectionService> logger)
        {
            _options = options.Value;
            _logger = logger;

            _appConfig = new ApplicationConfiguration
            {
                ApplicationName = "IndustrialAI.Monitor",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 }
            };
            _appConfig.Validate(ApplicationType.Client).GetAwaiter().GetResult();
        }

        public void ConfigureNodes(IEnumerable<string> nodeIds)
        {
            _monitoredNodes.Clear();
            _monitoredNodes.AddRange(nodeIds);
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var endpoint = CoreClientUtils.SelectEndpoint(_options.OpcUaEndpoint, useSecurity: false);
                var configuredEndpoint = new ConfiguredEndpoint(
                    null, endpoint, EndpointConfiguration.Create(_appConfig));

                _session = await Session.Create(
                    _appConfig, configuredEndpoint, updateBeforeConnect: false,
                    sessionName: "IndustrialAI.Monitor", sessionTimeout: 60000,
                    identity: new UserIdentity(), preferredLocales: null);

                _logger.LogInformation("OPC UA 连接成功 | Endpoint={Endpoint}", _options.OpcUaEndpoint);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OPC UA 连接失败 | Endpoint={Endpoint}", _options.OpcUaEndpoint);
                return false;
            }
        }

        public async Task<IReadOnlyList<SensorReading>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            if (_session == null)
                throw new InvalidOperationException("尚未连接到 OPC UA 服务器");

            var readings = new List<SensorReading>();
            var now = DateTime.UtcNow;

            foreach (var nodeId in _monitoredNodes)
            {
                try
                {
                    var value = await _session.ReadValueAsync(new NodeId(nodeId), cancellationToken);
                    readings.Add(new SensorReading(nodeId, ConvertToDouble(value), now));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "读取节点失败 | NodeId={NodeId}", nodeId);
                }
            }
            return readings;
        }

        private static double ConvertToDouble(DataValue value)
        {
            if (StatusCode.IsBad(value.StatusCode))
                throw new InvalidOperationException($"节点状态异常: {value.StatusCode}");

            return value.Value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                short s => s,
                _ => throw new InvalidOperationException($"不支持的数据类型: {value.Value?.GetType().Name}")
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (_session != null)
            {
                await _session.CloseAsync();
                _session.Dispose();
                _session = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}