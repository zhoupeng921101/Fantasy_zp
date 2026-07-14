using System.Text;
using System.Text.Json;

namespace Fantasy.ProtocolExportTool.Generators;

/// <summary>
/// OpCode 保号账本:消息名 → 计数器 的持久映射(按 Outer/Inner 侧与消息类别分组),
/// 与协议源同目录入库(OpCode.Lock.json)。
/// 规则:已记录的消息永远复用旧号;新消息取该类别「已记录最大号 + 1」;
/// 源中删除的消息条目保留在账本(坟位),其编号永不复用。
/// 目的:让 OpCode 成为「协议源 + 账本」的纯函数,与文件扫描顺序解耦——
/// 插入/删除/移动消息不再改变其他任何消息的编号(增量兼容前提),
/// 并行分支各自新增消息时冲突落在本文件同一区域,由 git 显式报出而非生成物静默同号。
/// </summary>
public sealed class OpCodeLock
{
    public const string FileName = "OpCode.Lock.json";

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, uint>>> _map;

    private OpCodeLock(string filePath, Dictionary<string, Dictionary<string, Dictionary<string, uint>>> map)
    {
        _filePath = filePath;
        _map = map;
    }

    /// <summary>
    /// 从协议目录载入账本;不存在时返回空账本(首次导出即基线固化)。
    /// </summary>
    public static OpCodeLock Load(string protocolDirectory)
    {
        var filePath = Path.Combine(protocolDirectory, FileName);

        if (!File.Exists(filePath))
        {
            return new OpCodeLock(filePath, new Dictionary<string, Dictionary<string, Dictionary<string, uint>>>());
        }

        var json = File.ReadAllText(filePath);
        var map = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, uint>>>>(json)
                  ?? new Dictionary<string, Dictionary<string, Dictionary<string, uint>>>();
        return new OpCodeLock(filePath, map);
    }

    /// <summary>
    /// 取得消息的计数器值:已记录 → 复用;未记录 → 该(侧, 类别)已记录最大号 + 1(不低于 start)并记录。
    /// </summary>
    public uint Acquire(string side, string category, string messageName, uint start)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(side, out var categories))
            {
                categories = new Dictionary<string, Dictionary<string, uint>>();
                _map[side] = categories;
            }

            if (!categories.TryGetValue(category, out var entries))
            {
                entries = new Dictionary<string, uint>();
                categories[category] = entries;
            }

            if (entries.TryGetValue(messageName, out var existing))
            {
                return existing;
            }

            var next = start;

            foreach (var value in entries.Values)
            {
                if (value >= next)
                {
                    next = value + 1;
                }
            }

            entries[messageName] = next;
            return next;
        }
    }

    /// <summary>
    /// 回写账本:侧与类别按名称排序、条目按编号排序,保证同一状态序列化字节稳定(幂等导出零 diff)。
    /// </summary>
    public void Save()
    {
        lock (_gate)
        {
            var shape = new SortedDictionary<string, SortedDictionary<string, Dictionary<string, uint>>>(StringComparer.Ordinal);

            foreach (var (side, categories) in _map)
            {
                var orderedCategories = new SortedDictionary<string, Dictionary<string, uint>>(StringComparer.Ordinal);

                foreach (var (category, entries) in categories)
                {
                    var orderedEntries = new Dictionary<string, uint>();

                    foreach (var (name, value) in entries.OrderBy(e => e.Value))
                    {
                        orderedEntries[name] = value;
                    }

                    orderedCategories[category] = orderedEntries;
                }

                shape[side] = orderedCategories;
            }

            var json = JsonSerializer.Serialize(shape, new JsonSerializerOptions { WriteIndented = true });
            // 行尾统一 LF,与生成物同策略(WriteIndented 在 Windows 产出 CRLF)
            File.WriteAllText(_filePath, json.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }
    }
}
