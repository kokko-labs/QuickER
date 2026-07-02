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
}
