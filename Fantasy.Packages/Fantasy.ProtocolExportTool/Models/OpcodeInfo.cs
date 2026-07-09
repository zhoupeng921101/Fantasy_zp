using Fantasy.Network;

namespace Fantasy.ProtocolExportTool.Models;

/// <summary>
/// OpCode 信息
/// </summary>
public sealed record OpcodeInfo
{
    /// <summary>
    /// 消息名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// OpCode 数值
    /// </summary>
    public uint Code { get; set; }

    /// <summary>
    /// OpCode 协议类型
    /// </summary>
    public uint ProtocolType { get; set; }

    /// <summary>
    /// 消息文档注释(源自 proto message 的 /// 注释,多行以空格拼接),用于在 Opcode 常量后生成行尾注释
    /// </summary>
    public string Comment { get; set; } = string.Empty;
}
