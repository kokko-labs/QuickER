using System.Reflection;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 生成オプション・設定の型から「キー」になるプロパティを取り出す（設定 JSON・CLI のフラグ表・GUI の設定の照合で共有する）
/// </summary>
internal static class OptionKeys
{
    /// <summary>
    /// 公開のインスタンスのプロパティのうち、公開の setter か init を持つもの（設定 JSON の逆直列化が値を入れられるもの。
    /// 計算だけの get 専用は除く）
    /// </summary>
    /// <param name="type">オプション・設定の型</param>
    public static IReadOnlyList<PropertyInfo> Writable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(IsWritable).ToArray();

    /// <summary>プロパティがキーになるか（公開の setter か init を持つ）</summary>
    /// <param name="property">公開のインスタンスのプロパティ</param>
    public static bool IsWritable(PropertyInfo property) =>
        property.SetMethod is { IsPublic: true };
}
