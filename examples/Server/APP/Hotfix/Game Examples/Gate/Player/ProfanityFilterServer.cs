using System;
using System.Collections.Generic;

namespace Fantasy;

/// <summary>
/// 改名昵称屏蔽字服务端终审(改名合规底线:前端预检不作数,服务端裁定)。
/// 匹配口径与客户端 GameLogic.ProfanityFilter 完全一致:大小写不敏感子串包含(含任一屏蔽词即不洁)、
/// 空词表恒通过、空名恒通过。词表进程级加载(<see cref="Load"/>),本轮无词源留空 = 不拦截
/// (机制就位、词表待填:真实词源(运营配置 / 敏感词库 / 第三方服务)接入时调 Load 注入即可,调用方不返工)。
/// </summary>
public static class ProfanityFilterServer
{
    /// <summary>是否大小写不敏感(默认 true,= 客户端 ProfanityFilter.IgnoreCase)。</summary>
    public static bool IgnoreCase = true;

    // 屏蔽词表(进程级)。本轮空 = 不拦;接入时 Load 注入。
    private static IReadOnlyCollection<string> _words = Array.Empty<string>();

    /// <summary>加载 / 替换屏蔽词表(启动期或热改时调;本轮无词源,留接口)。null 视作空表。</summary>
    public static void Load(IEnumerable<string> words)
    {
        _words = words == null ? Array.Empty<string>() : new List<string>(words);
    }

    /// <summary>名称是否「干净」(不含任何屏蔽词)。空词表 / 空名恒通过。</summary>
    public static bool IsClean(string name)
    {
        if (_words.Count == 0) return true;               // 无词表 = 不拦
        if (string.IsNullOrEmpty(name)) return true;
        var cmp = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var w in _words)
        {
            if (string.IsNullOrEmpty(w)) continue;
            if (name.IndexOf(w, cmp) >= 0) return false;
        }
        return true;
    }
}
