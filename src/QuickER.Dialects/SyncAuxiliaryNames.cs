namespace QuickER.Dialects;

/// <summary>生成コードの双方向同期が端末の DB に作る補助の物（表・索引・トリガー）の名前の前置きの宣言</summary>
/// <remarks>
/// <para>
/// 双方向同期が端末の SQLite の DB に作る物の名前は、種類（表・索引・トリガー）に依らず <see cref="Prefix"/> で始める。
/// SQLite の取込は、この前置きで始まる表を図に無い利用者の表として持ち帰らずに外す
/// （図で表せない定義の種類 <see cref="UnsupportedDefinitionKind.SystemTable"/> の位置づけ。告知しない）。
/// 外さないと、スキーマ同期が補助の表を図に無い表の削除として差分に出し、全部を選んだ同期が補助の表を消す。
/// </para>
/// <para>
/// 比べ方は SQLite の名前の規則に合わせて ASCII の大文字小文字を無視する（<see cref="StringComparison.OrdinalIgnoreCase"/> の前方一致）。
/// 生成ランタイムはこのアセンブリを参照できない（ランタイムのパッケージは依存ゼロ）ので、生成テンプレートは名前を直書きで持つ。
/// 対象は SQLite の取込だけで、ほかの 4 方言の取込はこの宣言を読まない。
/// </para>
/// </remarks>
public static class SyncAuxiliaryNames
{
    /// <summary>双方向同期の補助の物の名前の前置き</summary>
    public const string Prefix = "quicker_sync_";

    /// <summary>名前が双方向同期の補助の物か（<see cref="Prefix"/> で始まるか。ASCII の大文字小文字を無視する）</summary>
    public static bool IsAuxiliary(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}
