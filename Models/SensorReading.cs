/*
 * 内容：传感器读书实体
 * 创建时间：[2026-09-25 20:42:53]
 * 创建人：FengGuohua
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// 模型层  此版本 停用[2026-10-23 Feng Guohua]
namespace FghIndustrialAI.ML.MonitorUpgrade4.Models
{
    /// <summary>
    /// // C# 语法糖把类体隐藏了，实际是一个类 `SensorReading`，继承 `Record` 基类，
    /// // 并自定义了 **3 个 Init‑only 属性**：`NodeId`、`Value`、`Timestamp`（只能构造时赋值，之后不可修改）
    /// 传感器读数 [2026-09-22 13:29:59 FengGuohua]
    /// </summary>
    /// <param name="NodeId"></param>
    /// <param name="Value"></param>
    /// <param name="Timestamp"></param>
    public record SensorReading(string NodeId, double Value, DateTime Timestamp);
}
