using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期スクリプトの 1 セクション分を、差分の項目で表した読み取り用の形（テストの期待の面・静的な規則・計画の射影が読む）
/// </summary>
/// <remarks>
/// まとめ方は方言ビルダーが文を読む製品の <see cref="StepSections"/> と同じ。同じ <see cref="Kind"/> のセクションが複数回・別の位置に
/// 現れることがある（主キーの変更の 2 フェーズ・前寄せ）。
/// </remarks>
internal sealed class SyncPlanSection
{
    /// <summary>このセクションの差分の種類</summary>
    public SchemaDiffKind Kind { get; init; }

    /// <summary>主キーの変更のフェーズ（主キーの変更でなければ <see cref="PrimaryKeyPhase.None"/>）</summary>
    public PrimaryKeyPhase PrimaryKeyPhase { get; init; } = PrimaryKeyPhase.None;

    /// <summary>このセクションの差分の項目（計画の文の順）</summary>
    public IReadOnlyList<SchemaDiffItem> Items { get; init; } = [];

    /// <summary>このセクションの文（製品のセクション <see cref="StepSection.Steps"/> そのもの。写しの形の自己検査が項目と突き合わせる）</summary>
    public IReadOnlyList<SyncStep> Steps { get; init; } = [];

    /// <summary>
    /// 必ず断られる文として計画の先頭へ移したセクションか（製品のセクションの印 <see cref="StepSection.IsMovedFirst"/> の写し）
    /// </summary>
    public bool IsMovedFirst { get; init; }
}

/// <summary>
/// 計画（<see cref="SyncPlan"/>）の文の並びを、セクションと差分の項目の形（<see cref="SyncPlanSection"/>）へ写す
/// </summary>
/// <remarks>
/// <para>
/// セクションへのまとめ方は製品の <see cref="StepSections.Of"/>。主キーを外す文は主キーの変更の解除のフェーズへ、付ける文は付与の
/// フェーズへ入る。表を作り直す文と列を動かす文はセクションに入らない。
/// </para>
/// <para>
/// 選んだ項目から来て種類の変わらない文は、元の項目そのものを返す（列の組を並べ替えた外部キーの追加は、列の組だけ違う複製）。
/// 作り直しを展開した文と計画器が足した文は、文の値から項目を組み立てる（説明は由来から取る）。対になった主キーの解除と付与は
/// 同じ 1 つの項目になる。
/// </para>
/// <para>
/// 計画ごとに 1 回だけ導いて覚えるので、同じ計画を何度読んでも同じセクション・同じ項目の参照を返す。写しが文を落とさず取り違えない
/// ことは、能力の組の網の関係 N4（<see cref="SyncPlanCapabilityNetRunner.ReadableFormProblems"/>）が見る。
/// </para>
/// </remarks>
internal static class SyncPlanSections
{
    /// <summary>計画ごとに導いたセクションの並び</summary>
    private static readonly ConditionalWeakTable<SyncPlan, IReadOnlyList<SyncPlanSection>> Cache =
        new();

    extension(SyncPlan plan)
    {
        /// <summary>
        /// 計画の文の並びを、セクションと差分の項目で表した形（空のセクションは含まない。表を作り直す文と列を動かす文は入らない）
        /// </summary>
        public IReadOnlyList<SyncPlanSection> Sections => Cache.GetValue(plan, ToSections);
    }

    /// <summary>
    /// 列の組だけを差し替えた複製を返す（ほかの設定できるプロパティ〔internal の計画器の印を含む〕と選択の状態はそのまま写す）
    /// </summary>
    /// <param name="item">元の項目</param>
    /// <param name="pairs">新しい列の組</param>
    /// <returns>列の組だけが違う新しい項目（新しく組み立てるので、変更通知の購読は写さない）</returns>
    /// <remarks>
    /// プロパティを足したときに複製だけが値を落とさないことは、<see cref="SyncPlanFuzzOracleTests"/> の反射の照合が見る。
    /// </remarks>
    internal static SchemaDiffItem WithForeignKeyColumnPairs(
        SchemaDiffItem item,
        IReadOnlyList<ForeignKeyColumnNamePair> pairs
    ) =>
        new()
        {
            Kind = item.Kind,
            Description = item.Description,
            TableName = item.TableName,
            ColumnName = item.ColumnName,
            Entity = item.Entity,
            Column = item.Column,
            OldColumn = item.OldColumn,
            Relationship = item.Relationship,
            ParentEntity = item.ParentEntity,
            ChildEntity = item.ChildEntity,
            ForeignKeyName = item.ForeignKeyName,
            ForeignKeyColumnPairs = pairs,
            ReplacedForeignKey = item.ReplacedForeignKey,
            ReplacedUniqueConstraint = item.ReplacedUniqueConstraint,
            UniqueConstraintName = item.UniqueConstraintName,
            UniqueConstraintNames = item.UniqueConstraintNames,
            UniqueConstraintColumns = item.UniqueConstraintColumns,
            NewDescription = item.NewDescription,
            OldDescription = item.OldDescription,
            IsSelectable = item.IsSelectable,
            IsSelected = item.IsSelected,
            PlanMark = item.PlanMark,
        };

    /// <summary>計画の文の並びを、セクションと差分の項目の並びへ写す</summary>
    /// <param name="plan">計画</param>
    /// <returns>同じ文を同じ順で持つセクションの並び（表を作り直す文と列を動かす文は入らない）</returns>
    private static IReadOnlyList<SyncPlanSection> ToSections(SyncPlan plan)
    {
        // 対になった主キーの付与（解除の文から、同じ対の付与の列の並びを引く）
        var keyAdds = new Dictionary<ReaddPair, AddPrimaryKeyStep>();

        // 対になった外部キーの削除（付け直す文から、同じ対の外す文の列の組の並びを引く）
        var foreignKeyDrops = new Dictionary<ReaddPair, DropForeignKeyStep>();

        foreach (var step in plan.Steps)
        {
            if (step is AddPrimaryKeyStep { Pair: { } keyPair } add)
            {
                keyAdds[keyPair] = add;
            }

            if (step is DropForeignKeyStep { Pair: { } foreignKeyPair } drop)
            {
                foreignKeyDrops[foreignKeyPair] = drop;
            }
        }

        // 対ごとに 1 つだけ作る主キーの変更の項目（解除と付与の両方のセクションへ同じ項目を入れる）
        var keyItems = new Dictionary<ReaddPair, SchemaDiffItem>();

        return
        [
            .. StepSections
                .Of(plan)
                .Select(section => new SyncPlanSection
                {
                    Kind = section.Kind,
                    PrimaryKeyPhase = section.PrimaryKeyPhase,
                    Items =
                    [
                        .. section.Steps.Select(step =>
                            ItemOf(step, keyAdds, keyItems, foreignKeyDrops)
                        ),
                    ],
                    Steps = section.Steps,
                    IsMovedFirst = section.IsMovedFirst,
                }),
        ];
    }

    /// <summary>文をセクションの項目へ写す</summary>
    private static SchemaDiffItem ItemOf(
        SyncStep step,
        Dictionary<ReaddPair, AddPrimaryKeyStep> keyAdds,
        Dictionary<ReaddPair, SchemaDiffItem> keyItems,
        Dictionary<ReaddPair, DropForeignKeyStep> foreignKeyDrops
    )
    {
        if (step.Origin is SelectedOrigin selected)
        {
            var item = selected.Item;

            // 列の組を並べ替えた外部キーの追加は、列の組だけ違う複製（ほかのメンバーは元の項目のまま）
            if (step is AddForeignKeyStep add && item.Kind == SchemaDiffKind.AddForeignKey)
            {
                return add.ColumnPairs.SequenceEqual(item.ForeignKeyColumnPairs)
                    ? item
                    : WithForeignKeyColumnPairs(item, add.ColumnPairs);
            }

            // 種類の変わらない文は元の項目そのもの（主キーの解除と付与は、どちらも元の主キーの変更の項目）
            if (StepSections.SectionOf(step).Kind == item.Kind)
            {
                return item;
            }
        }

        return FromFields(
            step,
            keyAdds,
            keyItems,
            step is AddForeignKeyStep added
                ? DeclaredFirstChildColumn(added, foreignKeyDrops)
                : null
        );
    }

    /// <summary>
    /// 外部キーの追加の項目が名乗る列名（列の組を並べ替える前の、宣言の順の先頭の組の子の列）
    /// </summary>
    /// <remarks>
    /// 作り直しを展開した追加は元の作り直しの項目の列名、外して付け直す追加は対の外す文（DB の宣言の順）の先頭の組の子の列。
    /// どちらでもなければ <c>null</c>（文の列の組の先頭を使う）。
    /// </remarks>
    private static string? DeclaredFirstChildColumn(
        AddForeignKeyStep add,
        Dictionary<ReaddPair, DropForeignKeyStep> foreignKeyDrops
    )
    {
        if (add.Origin is SelectedOrigin { Item.ColumnName: { } column })
        {
            return column;
        }

        return
            add.Pair is { } pair
            && foreignKeyDrops.TryGetValue(pair, out var drop)
            && drop.ColumnPairs.Count > 0
            ? drop.ColumnPairs[0].ChildColumn
            : null;
    }

    /// <summary>文の値だけから項目を組み立てる（説明だけは文の出どころから取る）</summary>
    /// <param name="step">文</param>
    /// <param name="keyAdds">対になった主キーの付与</param>
    /// <param name="keyItems">対ごとに 1 つだけ作る主キーの変更の項目</param>
    /// <param name="foreignKeyColumnName">
    /// 外部キーの追加の項目が名乗る列名（<c>null</c> なら文の列の組の先頭の子の列）
    /// </param>
    private static SchemaDiffItem FromFields(
        SyncStep step,
        Dictionary<ReaddPair, AddPrimaryKeyStep> keyAdds,
        Dictionary<ReaddPair, SchemaDiffItem> keyItems,
        string? foreignKeyColumnName
    )
    {
        var description = DescriptionOf(step.Origin);

        switch (step)
        {
            case CreateTableStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = s.TableName,
                    Entity = s.Definition,
                    UniqueConstraintNames = s.UniqueConstraintNames,
                    Description = description,
                };

            case DropTableStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropTable,
                    TableName = s.TableName,
                    Description = description,
                };

            case AddColumnStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddColumn,
                    TableName = s.TableName,
                    ColumnName = s.Column.Name,
                    Column = s.Column,
                    Description = description,
                };

            case AlterColumnStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = s.TableName,
                    ColumnName = s.Column.Name,
                    Column = s.Column,
                    OldColumn = s.OldColumn,
                    Description = description,
                };

            case DropColumnStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropColumn,
                    TableName = s.TableName,
                    ColumnName = s.ColumnName,
                    Description = description,
                };

            case DropPrimaryKeyStep s:
                return PrimaryKeyItem(s, s.TableName, [], description, keyAdds, keyItems);

            case AddPrimaryKeyStep s:
                return PrimaryKeyItem(s, s.TableName, s.Columns, description, keyAdds, keyItems);

            case DropUniqueConstraintStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropUniqueConstraint,
                    TableName = s.TableName,
                    UniqueConstraintName = s.Name,
                    UniqueConstraintColumns = s.Columns,
                    Description = description,
                };

            case AddUniqueConstraintStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddUniqueConstraint,
                    TableName = s.TableName,
                    UniqueConstraintName = s.Name,
                    UniqueConstraintColumns = s.Columns,
                    Description = description,
                };

            case DropForeignKeyStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropForeignKey,
                    TableName = SchemaNames.ToStoredForm(s.ChildTable),
                    ParentEntity = new Entity { TableName = s.ParentTable },
                    ChildEntity = new Entity { TableName = s.ChildTable },
                    Relationship = new Relationship
                    {
                        OnDelete = s.OnDelete,
                        OnUpdate = s.OnUpdate,
                    },
                    ForeignKeyName = s.Name,
                    ForeignKeyColumnPairs = s.ColumnPairs,
                    Description = description,
                };

            case AddForeignKeyStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = SchemaNames.ToStoredForm(s.ChildTable),
                    ColumnName =
                        foreignKeyColumnName
                        ?? (s.ColumnPairs.Count > 0 ? s.ColumnPairs[0].ChildColumn : null),
                    ParentEntity = new Entity { TableName = s.ParentTable },
                    ChildEntity = new Entity { TableName = s.ChildTable },
                    Relationship = new Relationship
                    {
                        OnDelete = s.OnDelete,
                        OnUpdate = s.OnUpdate,
                    },
                    ForeignKeyName = s.Name,
                    ForeignKeyColumnPairs = s.ColumnPairs,
                    Description = description,
                };

            case SetTableDescriptionStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.SetTableDescription,
                    TableName = s.TableName,
                    NewDescription = s.NewDescription,
                    Description = description,
                };

            case SetColumnDescriptionStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.SetColumnDescription,
                    TableName = s.TableName,
                    ColumnName = s.ColumnName,
                    NewDescription = s.NewDescription,
                    Description = description,
                };

            default:
                throw new InvalidOperationException(
                    $"The step {step.GetType().Name} does not become a section item."
                );
        }
    }

    /// <summary>主キーを外す文・付ける文を、主キーの変更の項目へ写す</summary>
    /// <remarks>
    /// 項目は主キーの列だけを持つ表の定義を持つ。対になった解除と付与は同じ 1 つの項目で、付与の列の並びを持つ。
    /// 対の無い解除は主キーの列の無い定義を持つ。
    /// </remarks>
    private static SchemaDiffItem PrimaryKeyItem(
        SyncStep step,
        string tableName,
        IReadOnlyList<string> columns,
        string description,
        Dictionary<ReaddPair, AddPrimaryKeyStep> keyAdds,
        Dictionary<ReaddPair, SchemaDiffItem> keyItems
    )
    {
        if (step.Pair is not { } pair)
        {
            return Build(columns);
        }

        if (!keyItems.TryGetValue(pair, out var shared))
        {
            shared = Build(keyAdds.TryGetValue(pair, out var add) ? add.Columns : columns);
            keyItems[pair] = shared;
        }

        return shared;

        SchemaDiffItem Build(IReadOnlyList<string> keyColumns) =>
            new()
            {
                Kind = SchemaDiffKind.AlterPrimaryKey,
                TableName = tableName,
                Entity = new Entity
                {
                    TableName = tableName,
                    Columns =
                    [
                        .. keyColumns.Select(name => new Column
                        {
                            Name = name,
                            IsPrimaryKey = true,
                            IsNullable = false,
                        }),
                    ],
                },
                Description = description,
            };
    }

    /// <summary>項目の説明（選んだ項目から来た文は元の項目の説明、計画器が足した文は足した側の説明）</summary>
    private static string DescriptionOf(StepOrigin origin) =>
        origin switch
        {
            SelectedOrigin selected => selected.Item.Description,
            ImplicitOrigin implied => implied.Description,
            _ => string.Empty,
        };
}
