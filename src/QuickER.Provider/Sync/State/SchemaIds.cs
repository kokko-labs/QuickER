using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider.Sync.State;

/// <summary>同期計画が表の列を照合する識別（表と列の名前の組）</summary>
/// <remarks>
/// <para>
/// 等値は、表の名前と列の名前をそれぞれ <see cref="SchemaNames.Key"/> で畳んだ文字列の序数一致で決まる
/// （前後の空白と大文字小文字の違いは同じ列とみなす）。表と列の境目は成分ごとに持つので、境目をずらした名前どうしは等しくならない。
/// </para>
/// <para><see cref="Of"/> で作る（コンストラクターは受け取った値を畳まずに持つので、畳む前の名前を直接渡さない）。</para>
/// </remarks>
/// <param name="Table">畳んだ表の名前</param>
/// <param name="Column">畳んだ列の名前</param>
internal sealed record ColumnId(string Table, string Column)
{
    /// <summary>表と列の名前から識別を作る</summary>
    /// <param name="table">表の名前</param>
    /// <param name="column">列の名前</param>
    public static ColumnId Of(string table, string column) =>
        new(SchemaNames.Key(table), SchemaNames.Key(column));
}

/// <summary>同期計画が外部キーの組（子の列と親の列の 1 組）を照合する識別</summary>
/// <remarks>
/// 等値は、子の端の <see cref="ColumnId"/> と親の端の <see cref="ColumnId"/> の両方の一致で決まる（向きを見る＝子と親を
/// 入れ替えた組は等しくない）。名前の畳み方は <see cref="ColumnId"/> と同じ。
/// </remarks>
/// <param name="Child">子の表の列</param>
/// <param name="Parent">親の表の列</param>
internal sealed record ForeignKeyPairId(ColumnId Child, ColumnId Parent)
{
    /// <summary>子と親の表・列の名前から識別を作る</summary>
    /// <param name="childTable">子の表</param>
    /// <param name="childColumn">子の列</param>
    /// <param name="parentTable">親の表</param>
    /// <param name="parentColumn">親の列</param>
    public static ForeignKeyPairId Of(
        string childTable,
        string childColumn,
        string parentTable,
        string parentColumn
    ) => new(ColumnId.Of(childTable, childColumn), ColumnId.Of(parentTable, parentColumn));
}

/// <summary>同期計画が DB の一意制約を照合する識別（表・DB の名前・列集合）</summary>
/// <remarks>
/// <para>等値は次の 3 つの成分の序数一致で決まる。</para>
/// <list type="bullet">
/// <item>表：<see cref="SchemaNames.Key"/> で畳んだ表の名前</item>
/// <item>名前：<see cref="SchemaNames.Key"/> で畳んだ制約の名前。名前の無い制約（<c>null</c>）は空文字列として持つので、
/// <c>null</c>・空文字列・空白だけの名前は互いに等しい</item>
/// <item>列集合：<see cref="UniqueConstraintNaming.ColumnSetSignature"/> の署名（列の名前を畳んで並べ替えたもの＝列の並びを
/// 見ず、同じ名前の列の重なりは数える）</item>
/// </list>
/// </remarks>
/// <param name="Table">畳んだ表の名前</param>
/// <param name="Name">畳んだ制約の名前（名前の無い制約は空文字列）</param>
/// <param name="ColumnSet">列集合の署名</param>
internal sealed record UniqueConstraintId(string Table, string Name, string ColumnSet)
{
    /// <summary>表・制約の名前・構成列から識別を作る</summary>
    /// <param name="table">表の名前</param>
    /// <param name="name">DB の名前（名前の無い制約は <c>null</c>）</param>
    /// <param name="columnNames">構成列（並びは見ない）</param>
    public static UniqueConstraintId Of(
        string table,
        string? name,
        IEnumerable<string> columnNames
    ) =>
        new(
            SchemaNames.Key(table),
            name is null ? string.Empty : SchemaNames.Key(name),
            UniqueConstraintNaming.ColumnSetSignature(columnNames)
        );
}

/// <summary>同期計画が外部キーを照合する識別（子の表・親の表・列ペアの並び・実効名・参照アクション）</summary>
/// <remarks>
/// <para>等値は次の成分の一致で決まる。</para>
/// <list type="bullet">
/// <item>子の表・親の表：<see cref="SchemaNames.Key"/> で畳んだ名前の序数一致</item>
/// <item>列ペア：宣言順のまま（並びを見る）、1 組ずつ親の列・子の列を <see cref="SchemaNames.Key"/> で畳み、
/// <see cref="SignatureText.Part"/> でつないだ署名の序数一致</item>
/// <item>名前：呼び出し側が決めた実効名を <see cref="SchemaNames.Key"/> で畳んだものの序数一致</item>
/// <item>参照アクション：ON DELETE と ON UPDATE の値の一致</item>
/// </list>
/// <para>
/// 外部キーの名前を保存する方言では、同じ子の表の外部キーは実効名が見分ける。参照アクションは、名前を保存しない能力で
/// 同じ列ペア・同じ名前の 2 本を見分けるための成分。
/// </para>
/// </remarks>
/// <param name="Child">畳んだ子の表の名前</param>
/// <param name="Parent">畳んだ親の表の名前</param>
/// <param name="ColumnPairs">列ペアの署名（宣言順）</param>
/// <param name="Name">畳んだ実効名</param>
/// <param name="OnDelete">ON DELETE の参照アクション</param>
/// <param name="OnUpdate">ON UPDATE の参照アクション</param>
internal sealed record ForeignKeyId(
    string Child,
    string Parent,
    string ColumnPairs,
    string Name,
    ForeignKeyReferentialAction OnDelete,
    ForeignKeyReferentialAction OnUpdate
)
{
    /// <summary>外部キーの成分から識別を作る</summary>
    /// <param name="childTable">子の表</param>
    /// <param name="parentTable">親の表</param>
    /// <param name="columnPairs">構成列ペア（宣言順）</param>
    /// <param name="effectiveName">実効名（畳む前）</param>
    /// <param name="onDelete">ON DELETE の参照アクション</param>
    /// <param name="onUpdate">ON UPDATE の参照アクション</param>
    public static ForeignKeyId Of(
        string childTable,
        string parentTable,
        IEnumerable<ForeignKeyColumnNamePair> columnPairs,
        string effectiveName,
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    ) =>
        new(
            SchemaNames.Key(childTable),
            SchemaNames.Key(parentTable),
            string.Concat(
                columnPairs.Select(p =>
                    SignatureText.Part(SchemaNames.Key(p.ParentColumn))
                    + SignatureText.Part(SchemaNames.Key(p.ChildColumn))
                )
            ),
            SchemaNames.Key(effectiveName),
            onDelete,
            onUpdate
        );
}
