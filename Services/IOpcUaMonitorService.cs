using FghIndustrialAI.ML.Monitor.Models;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FghIndustrialAI.ML.Monitor.Services
{
    /// <summary>
    /// 客户端服务   [2026-08-10 21:44:37 FengGuohua]
    /// 通过 OPC UA 客户端链接工业设备（PLC/传感器），实时采集数据
    /// 使用ML.NET的异常检测算法对数据进行实时分析
    /// </summary>
    public interface IOpcUaMonitorService : IDisposable
    {
        IReadOnlyList<string> MonitoredNodes { get;}

        void ConfigureNodes(IEnumerable<string> nodeIds);

        Task<bool> ConnectAsnc(string endpointUrl,CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SensorReading>> ReadAllAsync(CancellationToken cancellationToken = default);

        #region 暂时丢弃

        //Task<bool> ConnectAsync(string endpointUrl);

        //Task<double> ReadNumericValueAsync(string nodeID);

        //Task<IEnumerable<double>> ReadMultipleValuesAsync();

        //// ---------------------- Begin [Opc.Ua]----------------------------
        //// 需要应用包 Opc.Ua
        //event EventHandler<DataValue> OnDataChanged;
        //// ---------------------- End [Opc.Ua]----------------------------

        #endregion
    }
}
