using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider.Sync.State;

/// <summary>
/// 同期の計画が項目・モデルから識別の型（<see cref="ColumnId"/>・<see cref="ForeignKeyPairId"/>・
/// <see cref="UniqueConstraintId"/>・<see cref="ForeignKeyId"/>）を作る入口と、項目から求める外部キーの名前
/// </summary>
/// <remarks>
/// 文から求める外部キーの名前は <see cref="Steps.ForeignKeyStepNames"/> が持つ。
/// </remarks>
internal static class PlanIdentity
{
    /// <summary>外部キーの 1 組の照合キー（子の列と親の列。名前の比べ方は列の照合キーと同じ）</summary>
    internal static ForeignKeyPairId ForeignKeyPairKey(
        string childTable,
        string childColumn,
        string parentTable,
        string parentColumn
    ) => ForeignKeyPairId.Of(childTable, childColumn, parentTable, parentColumn);

    /// <summary>
    /// DB の一意制約の照合キー（テーブル＋DB の名前＋列集合シグネチャ。名前は <see cref="SchemaNames.Key"/> で畳み、列順を無視する）
    /// </summary>
    /// <param name="table">テーブル</param>
    /// <param name="name">DB の名前（名前の無い制約＝SQLite の自動名は <c>null</c>）</param>
    /// <param name="columnNames">構成列</param>
    /// <remarks>
    /// <para>
    /// 照合するのは、DB の一意制約（live）と、それを外す項目（差分の DropUniqueConstraint・作り直しを展開した削除＝どちらも
    /// live の制約の名前をそのまま運ぶ）だけ。図の一意制約（追加の項目）はこのキーで照合しない。名前の無い制約どうしは
    /// 列集合だけで組む（合成名は使わない＝並び順に依る）。
    /// </para>
    /// <para>
    /// 名前を含めるのは、同じ列集合の一意制約が 2 つある表で、明示的に外した 1 つがもう 1 つまで「外した」ことにして、
    /// 暗黙の外し直し・列の削除に伴う削除・同期後のキーの集合を変えないため（外部キーの <see cref="ForeignKeyKey"/> と対称。
    /// 名前を見ないと、残った 1 つが列の変更を阻んだ＝SQL Server の Msg 5074）。
    /// </para>
    /// </remarks>
    internal static UniqueConstraintId UniqueConstraintKey(
        string table,
        string? name,
        IEnumerable<string> columnNames
    ) => UniqueConstraintId.Of(table, name, columnNames);

    /// <summary>テーブル・列の照合キー（名前は <see cref="SchemaNames.Key"/> で畳む）</summary>
    internal static ColumnId ColumnKey(string table, string column) => ColumnId.Of(table, column);

    /// <summary>
    /// 外部キーの照合キー（子テーブル・親テーブル・構成列ペアの宣言順リスト・実効名・参照アクション。
    /// 名前は <see cref="SchemaNames.Key"/> で畳む）
    /// </summary>
    /// <param name="childTable">子テーブル</param>
    /// <param name="parentTable">親テーブル</param>
    /// <param name="columnPairs">構成列ペア（宣言順）</param>
    /// <param name="relationship">名前と参照アクションを読むリレーション（無ければ既定名・NO ACTION）</param>
    /// <remarks>
    /// 複合外部キーも 1 本のキーへ畳むため、列ペアを宣言順に連結する。
    /// 名前と参照アクションも含める＝同じ列ペアに外部キーが 2 本ある構成で、明示的に外した 1 本がもう 1 本まで
    /// 「外した」ことにして暗黙の外し直しを止めないため（止まると、残った 1 本が主キーの解除・列の変更を阻む）。
    /// 暗黙の外し直しのある実在の方言（逐次 DDL）は外部キーの名前を保存するので、同じ子の表の 2 本は実効名が見分ける。
    /// 参照アクションは、名前を保存しない能力と暗黙の外し直しの組のための成分で、いまの実在の方言の組には当たらない
    /// （名前を保存しない SQLite の再構築の合成は、削除 1 件につき 1 本を外す数え方で 2 本を扱う）。
    /// </remarks>
    internal static ForeignKeyId ForeignKeyKey(
        string childTable,
        string parentTable,
        IEnumerable<ForeignKeyColumnNamePair> columnPairs,
        Relationship? relationship,
        IdentifierLengthLimit limit
    ) =>
        ForeignKeyId.Of(
            childTable,
            parentTable,
            columnPairs,
            ResolveForeignKeyName(relationship, childTable, parentTable, limit),
            relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction,
            relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction
        );

    /// <summary>
    /// 選んだ外部キーの追加・作り直しの項目を警告で名指しする名前（スクリプトが付ける名前＝
    /// <see cref="SyncScriptBuilderHelper.ForeignKeyNameToAdd"/>）
    /// </summary>
    /// <remarks>
    /// 差分は名前の無い外部キーの名前を図全体から求めて項目に載せる（同じ組の 2 本目は子の列名が付く）。リレーションの名前から
    /// 規約名を作り直すと 2 本目は 1 本目と同じ名前になり、DB にある無関係な外部キーを名指しした。
    /// </remarks>
    internal static string SelectedForeignKeyName(
        SchemaDiffItem item,
        IdentifierLengthLimit limit
    ) => SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, limit);

    /// <summary>FK 制約名を解決する（未設定なら <c>FK_{子}_{親}</c> の規約名）</summary>
    /// <param name="relationship">外部キーのリレーション</param>
    /// <param name="childTable">子表</param>
    /// <param name="parentTable">親表</param>
    /// <param name="limit">既定名を縮める同期の方言の上限（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/>）</param>
    /// <param name="liveSchema">
    /// live の図。live のリレーションなら、図全体から求めた実効名（<see cref="LiveSchema.ForeignKeyNames"/>）を使う
    /// </param>
    internal static string ResolveForeignKeyName(
        Relationship? relationship,
        string childTable,
        string parentTable,
        IdentifierLengthLimit limit,
        LiveSchema? liveSchema = null
    ) =>
        relationship is not null
        && liveSchema is not null
        && liveSchema.ForeignKeyNames(limit).TryGetValue(relationship, out var resolved)
            ? resolved
            : ConstraintNames.EffectiveForeignKey(
                relationship?.ConstraintName,
                childTable,
                parentTable,
                limit
            );

    /// <summary>
    /// 外部キーの列の組の対応の集合のキー（並びを見ない。名前は <see cref="SchemaNames.Key"/> で畳み、部分は <see cref="SignatureText.Part"/>
    /// で書いてつなぐ）
    /// </summary>
    /// <remarks>
    /// 区切り文字だけでつなぐと、区切り文字（<c>&gt;</c>・<c>,</c>）を含む列名の組が別の組と同じキーになる。
    /// 外部キーの追加の並び（<see cref="Scheduling.ForeignKeyAddOrder"/>）と、同期後も残る DB の制約の定義の重なり
    /// （<see cref="Selection.RemainingLiveConstraints"/>）が共有する。
    /// </remarks>
    internal static string ForeignKeyPairSet(IEnumerable<ForeignKeyColumnNamePair> pairs) =>
        string.Concat(
            pairs
                .Select(p =>
                    SignatureText.Part(SchemaNames.Key(p.ParentColumn))
                    + SignatureText.Part(SchemaNames.Key(p.ChildColumn))
                )
                .Order(System.StringComparer.Ordinal)
        );
}
