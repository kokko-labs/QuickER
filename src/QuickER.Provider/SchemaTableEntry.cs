using System.Diagnostics.CodeAnalysis;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// スキーマ取込処理中にテーブルとその列を索引付きで保持する作業用エントリ（DB 方言横断の共通表現）
/// </summary>
/// <remarks>
/// テーブル→カラム→主キー→説明→外部キーの段階ロードで、後続ステップが列を引けるよう
/// <see cref="ColumnsByName"/>（大文字小文字無視）を保持する。テーブルを一意に識別する
/// <see cref="Key"/> の書式は方言ごとに異なる（SQL Server は <c>[schema].[name]</c>、
/// PostgreSQL / MySQL / Oracle は素のテーブル名）ため、生成側が値を設定する。
/// </remarks>
public sealed class SchemaTableEntry
{
    /// <summary>構築中のエンティティ</summary>
    public Entity Entity { get; init; } = new();

    /// <summary>列名からカラムを引くための索引（後続の PK / 説明 / FK 反映に用いる）</summary>
    public Dictionary<string, Column> ColumnsByName { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>テーブルを一意に識別するキー（書式は方言依存）</summary>
    public string Key { get; init; } = "";

    /// <summary>取込対象として採用したテーブルを、大文字小文字まで一致させて引く</summary>
    /// <param name="tables">キー → エントリの辞書（5 方言共通で大文字小文字を区別しない）</param>
    /// <param name="key">引くテーブルのキー（書式は <see cref="Key"/> と同じ）</param>
    /// <param name="entry">見つかったエントリ</param>
    /// <returns>キーが序数一致で採用済みのテーブルにあれば <c>true</c></returns>
    /// <remarks>
    /// テーブル辞書は大文字小文字を区別しないので、素で引くと「大文字小文字だけ違うために衝突して捨てたほうの
    /// テーブル」の列・主キー・説明・一意制約・外部キーが、採用したほうのエンティティへ吸い寄せられて混ざる。
    /// 取込の各クエリがカタログから返す名前はどのクエリでも同じ実体名なので、序数一致まで求めても
    /// 正当な行を取りこぼすことはない。取込の全解決点（列・主キー・説明・一意制約・外部キーの両端）でこれを通す。
    /// </remarks>
    public static bool TryGetExact(
        IReadOnlyDictionary<string, SchemaTableEntry> tables,
        string key,
        [NotNullWhen(true)] out SchemaTableEntry? entry
    ) =>
        tables.TryGetValue(key, out entry)
        && string.Equals(entry.Key, key, StringComparison.Ordinal);
}
