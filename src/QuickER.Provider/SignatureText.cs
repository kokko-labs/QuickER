namespace QuickER.Provider;

/// <summary>照合キー・シグネチャの部分の書き方（差分の署名・一意制約の列集合のシグネチャ・同期の計画の照合キーが共有する）</summary>
/// <remarks>
/// 各部分を「長さ:値」の形で書いて連結する。区切り文字だけでつなぐと、区切り文字を含む名前 1 つ（<c>a|b</c>）と 2 つの名前
/// （<c>a</c>, <c>b</c>）が同じキーになり、表名と列名の境目（<c>t</c>.<c>a|b</c> と <c>t|a</c>.<c>b</c>）も見分けられない。
/// プロセス内の照合専用で、保存も表示もしない。
/// </remarks>
internal static class SignatureText
{
    /// <summary>キーの 1 部分（「長さ:値」）</summary>
    public static string Part(string value) =>
        value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;
}
