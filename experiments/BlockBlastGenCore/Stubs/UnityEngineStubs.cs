using System.Collections.Generic;
using System.Text.Json;

// .NET 侧为客户端生成核心链接的 Unity-only 依赖提供最小 stub。
// 仅覆盖生成核心闭包实际触达的成员;非生产实现,只为让链接源在 .NET 编译运行。
namespace UnityEngine
{
    /// <summary>
    /// DynamicWeightDiff.Save/Load 用其序列化两 int 的调度态。
    /// 中立 System.Text.Json 实现:对该 [Serializable] POCO 行为等价(字段名/值往返一致),
    /// 使内存持久化往返忠实,不引入运行时相关差异。
    /// </summary>
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
        };

        public static string ToJson(object obj) => JsonSerializer.Serialize(obj, obj.GetType(), Options);

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            return JsonSerializer.Deserialize<T>(json, Options);
        }
    }

    /// <summary>
    /// Persistence.PlayerPrefsProvider 引用 PlayerPrefs。harness 全程注入
    /// InMemoryPersistenceProvider,本 stub 不会被触达,仅为链接源编译通过。
    /// 进程内存兜底实现,保证即便被调用也不抛、行为可预期。
    /// </summary>
    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Store = new Dictionary<string, string>();

        public static bool HasKey(string key) => Store.ContainsKey(key);
        public static string GetString(string key) => Store.TryGetValue(key, out var v) ? v : string.Empty;
        public static void SetString(string key, string value) => Store[key] = value;
        public static void DeleteKey(string key) => Store.Remove(key);
        public static void Save() { }
    }
}
