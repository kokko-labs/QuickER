using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider.Sync.State;

/// <summary>同期計画が読む DB の今の図（live の表とリレーション）と、そこから作る索引・一覧</summary>
/// <remarks>
/// <para>
/// 計画の組み立て（<see cref="SyncPlanner.BuildPlan"/>）の入口で 1 回だけ作り、各段へ渡す。中身は作った後に変わらない
/// （外部キーの実効名だけは、最初に求めた上限ごとに覚える）。
/// </para>
/// <para>
/// 持つのは live の表・リレーションから決まるものだけ。索引・トリガー・<c>CREATE TABLE</c> の原文・列の既定値・取込の警告・
/// ビューの判定・作り直しを止める物は <see cref="SyncPlanContext"/> から読む。
/// </para>
/// </remarks>
internal sealed class LiveSchema
{
    private IReadOnlyDictionary<Relationship, string>? _foreignKeyNames;

    private IdentifierLengthLimit? _foreignKeyNamesLimit;

    private LiveSchema(IReadOnlyList<Entity> entities, IReadOnlyList<Relationship> relationships)
    {
        Entities = entities;
        Relationships = relationships;
        Tables = new NameLookup<Entity>(entities, SchemaDiffService.NormalizeTable);
        ForeignKeys = ForeignKeyColumnPairResolver.ResolveAll(relationships, entities);

        var pairs = new HashSet<ForeignKeyPairId>();

        foreach (var fk in ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            foreach (var pair in fk.ColumnPairs)
            {
                pairs.Add(
                    ForeignKeyPairId.Of(
                        childTable,
                        pair.ChildColumn,
                        parentTable,
                        pair.ParentColumn
                    )
                );
            }
        }

        ForeignKeyPairs = pairs;
    }

    /// <summary>計画の入力から live の図を作る</summary>
    /// <param name="context">計画の入力（<c>null</c> なら live の図も無い）</param>
    /// <returns>live の図。<paramref name="context"/> が <c>null</c> なら <c>null</c></returns>
    public static LiveSchema? From(SyncPlanContext? context) =>
        context is null ? null : new LiveSchema(context.LiveEntities, context.LiveRelationships);

    /// <summary>DB の今の表（取込の並びのまま）</summary>
    public IReadOnlyList<Entity> Entities { get; }

    /// <summary>DB の今のリレーション（取込の並びのまま）</summary>
    public IReadOnlyList<Relationship> Relationships { get; }

    /// <summary>表を名前で引く索引（規則は差分と同じ <see cref="NameLookup{T}"/>）</summary>
    /// <remarks>
    /// 序数一致の 1 件を優先し、無ければ大文字小文字を無視した一致が 1 件のときだけ引ける。2 件以上なら引けない
    /// （並び順で片方を選ばない）。
    /// </remarks>
    public NameLookup<Entity> Tables { get; }

    /// <summary>親子の表と構成列ペアまで解決できた外部キー（リレーションの並びのまま）</summary>
    /// <remarks>
    /// 多対多と、親子・構成列を解決できないリレーションは含まない（規則は差分と共有の
    /// <see cref="ForeignKeyColumnPairResolver.ResolveAll"/>）。複合外部キーは列ペアを宣言順に持つ。
    /// </remarks>
    public IReadOnlyList<ResolvedForeignKeyRelationship> ForeignKeys { get; }

    /// <summary><see cref="ForeignKeys"/> が持つ組（子の列と親の列）の集合</summary>
    public IReadOnlySet<ForeignKeyPairId> ForeignKeyPairs { get; }

    /// <summary>外部キーの実効名（図全体から求めた名前＝DDL が付けるのと同じ規則）</summary>
    /// <remarks>
    /// 名前の無い同じ組の外部キーが 2 本あると、2 本目は子の列名を足した名前になる
    /// （<see cref="ConstraintNames.ResolveForeignKeyNames"/>）。リレーションごとに既定名を求めると 2 本とも同じ名前になり、
    /// 警告・外し直しの名前が DDL と食い違う。前回と同じ上限なら覚えた結果を返す。
    /// </remarks>
    /// <param name="limit">既定名を縮める同期の方言の上限（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/>）</param>
    public IReadOnlyDictionary<Relationship, string> ForeignKeyNames(IdentifierLengthLimit limit)
    {
        if (_foreignKeyNames is null || !Equals(_foreignKeyNamesLimit, limit))
        {
            _foreignKeyNames = ConstraintNames.ResolveForeignKeyNames(
                Entities,
                Relationships,
                limit
            );
            _foreignKeyNamesLimit = limit;
        }

        return _foreignKeyNames;
    }
}
