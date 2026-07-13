using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameConfig;
using Luban;

namespace Fantasy;

/// <summary>
/// 服务端 Luban 配置进程级单例:启动期把 GameConfigBytes\*.bytes 反序列化进 GameConfig.Tables,
/// 业务侧按 id/键命名常量取值。与客户端 ConfigSystem 同源(同一份 luban.conf,server target),
/// 替代手抄成 C# 常量的客户端值——改 .xlsx 重跑 gen 即生效,无需改代码。
///
/// 加载失败(.bytes 缺失/路径错/格式坏):Log.Error,Tables = null;
/// 取值方一律走调用方默认值(同客户端 GlobalConfigMgr 的回退语义),不抛、不阻断启动。
/// </summary>
public static class GameConfigSystem
{
    /// <summary>已成功加载的 Tables;加载失败为 null。业务侧应通过 <see cref="GlobalCfg"/> 取 A 类值,非 null 检查由便捷类内部完成。</summary>
    public static Tables? Tables { get; private set; }

    private static bool _loaded;

    /// <summary>
    /// 从指定根目录加载 .bytes(默认 = 进程工作目录下的 GameConfigBytes,与 Entity.csproj 的复制规则对齐)。
    /// 已加载则跳过(幂等)。
    /// </summary>
    public static void Load(string? bytesRoot = null)
    {
        if (_loaded) return;
        _loaded = true;

        string root = bytesRoot ?? Path.Combine(AppContext.BaseDirectory, "GameConfigBytes");
        // 用 Console.WriteLine 不调 Log.*:Load 在 Entry.Start 之前由 Program.cs 调,此时 Fantasy.Log 还未初始化,
        // 调 Log.Info/Error 会 NRE。改 Log 后,NLog 也会捕获 Console 输出,等价显示。
        try
        {
            Tables = new Tables(fileName =>
            {
                string path = Path.Combine(root, fileName + ".bytes");
                byte[] bytes = File.ReadAllBytes(path);
                return new ByteBuf(bytes);
            });
            Console.WriteLine($"[GameConfigSystem] 加载完成:root={root},表数=12(含 TbGlobal / TbMergeOrder / TbTarotCard)。");
        }
        catch (Exception e)
        {
            Tables = null;
            Console.Error.WriteLine($"[GameConfigSystem] 加载失败:root={root},err={e.Message};业务侧将回退到代码内默认值。");
        }
    }
}

/// <summary>
/// global.xlsx(键值型 id→value)便捷取值:镜像客户端 GlobalConfigMgr 的 id 常量 + parse 语义。
/// 缺表/缺键/解析失败 → 调用方默认值,不抛。改 global.xlsx 重跑 server gen 即生效。
/// </summary>
public static class GlobalCfg
{
    // ── 参数 id 常量(与 global.xlsx 主键、客户端 GlobalConfigMgr 一致)──
    public const int OrderCount = 1;
    public const int OrderRefreshSeconds = 2;
    public const int EnergyRecoverSeconds = 3;
    public const int EnergyRecoverCap = 4;
    public const int ClearToolEnergyCost = 5;
    public const int GoddessMaxCount = 7;
    public const int RenamePrice = 8;

    public static string GetString(int id, string defaultValue = "")
    {
        var tb = GameConfigSystem.Tables?.TbGlobal;
        if (tb == null) return defaultValue;
        var row = tb.GetOrDefault(id);
        return row != null ? row.Value : defaultValue;
    }

    public static int GetInt(int id, int defaultValue = 0)
    {
        var raw = GetString(id, null!);
        return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
            ? r : defaultValue;
    }

    // ── 体力恢复(id=3,复合格式 "amount#interval")──
    // value 形态:
    //   ① "amount#interval"(新格式)每 interval 秒恢复 amount 点
    //   ② bare int(旧格式)作 interval、amount=1(向后兼容)
    //   ③ 缺键/空/整段非法 → amount=1、interval=360(与客户端 GlobalConfigMgr.EnergyRecover*Default 一致)
    // 局部非法(如 "1#"、"#10"、"a#10")各自半边独立回退,不互相牵连。
    public const int EnergyRecoverAmountDefault = 1;
    public const int EnergyRecoverIntervalDefault = 360;

    public static (int amount, int interval) ParseEnergyRecover()
    {
        string raw = GetString(EnergyRecoverSeconds, "");
        if (string.IsNullOrEmpty(raw))
            return (EnergyRecoverAmountDefault, EnergyRecoverIntervalDefault);

        int sep = raw.IndexOf('#');
        if (sep < 0)
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv)
                ? (EnergyRecoverAmountDefault, iv)
                : (EnergyRecoverAmountDefault, EnergyRecoverIntervalDefault);
        }
        string amountStr = raw.Substring(0, sep);
        string intervalStr = raw.Substring(sep + 1);
        int amount = int.TryParse(amountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
            ? a : EnergyRecoverAmountDefault;
        int interval = int.TryParse(intervalStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i : EnergyRecoverIntervalDefault;
        return (amount, interval);
    }
}
