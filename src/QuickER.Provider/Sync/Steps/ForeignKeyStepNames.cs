using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 外部キーの文（<see cref="AddForeignKeyStep"/>・<see cref="DropForeignKeyStep"/>）が付ける・外す外部キーの実効名を、文の値から求める
/// </summary>
/// <remarks>
/// <para>
/// 付ける文は、同期スクリプトが付ける名前（文の名前。無ければ既定名＝<see cref="ConstraintNames.EffectiveForeignKey"/>）。
/// </para>
/// <para>
/// 外す文は、DB の名前を持たず、外す DB の外部キーのリレーションが分かれば DB の今の図の外部キーの実効名
/// （<see cref="LiveSchema.ForeignKeyNames"/>＝名前の無い同じ組の 2 本目を 1 本目と取り違えない）。それも引けなければ、
/// 文の名前（無ければリレーションの名前）から付ける文と同じ求め方をする。
/// </para>
/// <para>表名はどちらも保存する形（<see cref="SchemaNames.ToStoredForm"/>）にしてから名前を作る。</para>
/// </remarks>
internal static class ForeignKeyStepNames
{
    /// <summary>付ける文が付ける外部キーの実効名</summary>
    /// <param name="add">外部キーを付ける文</param>
    /// <param name="limit">既定名を縮める方言の上限</param>
    public static string Added(AddForeignKeyStep add, IdentifierLengthLimit limit) =>
        Effective(add.Name, add.ChildTable, add.ParentTable, limit);

    /// <summary>外す文が外す外部キーの実効名</summary>
    /// <param name="drop">外部キーを外す文</param>
    /// <param name="relationship">外す DB の外部キーのリレーション（分からなければ <c>null</c>）</param>
    /// <param name="live">DB の今の図（無ければ <c>null</c>）</param>
    /// <param name="limit">既定名を縮める方言の上限</param>
    public static string Dropped(
        DropForeignKeyStep drop,
        Relationship? relationship,
        LiveSchema? live,
        IdentifierLengthLimit limit
    ) =>
        drop.Name is null
        && relationship is not null
        && live is not null
        && live.ForeignKeyNames(limit).TryGetValue(relationship, out var resolved)
            ? resolved
            : Effective(
                drop.Name ?? relationship?.ConstraintName,
                drop.ChildTable,
                drop.ParentTable,
                limit
            );

    private static string Effective(
        string? name,
        string childTable,
        string parentTable,
        IdentifierLengthLimit limit
    ) =>
        ConstraintNames.EffectiveForeignKey(
            name,
            SchemaNames.ToStoredForm(childTable),
            SchemaNames.ToStoredForm(parentTable),
            limit
        );
}
