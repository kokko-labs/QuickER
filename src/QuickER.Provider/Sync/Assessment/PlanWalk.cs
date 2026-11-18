using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 計画の文の並びを DB の今の図から 1 回歩いた結果（位置ごとの、当てる前の状態と、その文が外す・付ける制約の身元）
/// </summary>
/// <remarks>
/// <para>
/// 状態は DB の今の図の制約（<see cref="ConstraintState.From"/>）から始め、文を順に当てる（<see cref="ConstraintState.Apply"/>）。
/// 歩きは文の並びも状態も書き換えない。
/// </para>
/// <para>身元の求め方（<see cref="ConstraintIdentity"/>）：</para>
/// <list type="bullet">
/// <item>
/// 外部キー：子の表（保存する形）と実効名。付ける文は <see cref="ForeignKeyStepNames.Added"/>、外す文はその位置の状態から外す外部キーを
/// 引き（<see cref="ConstraintState.FindDropped"/>）、そのリレーションで <see cref="ForeignKeyStepNames.Dropped"/>。
/// </item>
/// <item>一意制約：表（保存する形）と <see cref="UniqueConstraintNaming.Resolve"/> の実効名（文の名前・表名・構成列から）。</item>
/// <item>主キー：表（保存する形）。名前は <see cref="ConstraintIdentity.PrimaryKeyName"/>。</item>
/// </list>
/// </remarks>
/// <param name="Positions">位置ごとの結果（文の並びの順）</param>
/// <param name="End">全部の文を当てた後の状態</param>
internal sealed record PlanWalk(IReadOnlyList<WalkPosition> Positions, ConstraintState End)
{
    /// <summary>文の並びを歩く</summary>
    /// <param name="steps">計画の文の並び</param>
    /// <param name="live">DB の今の図</param>
    /// <param name="capabilities">方言の宣言（実効名の上限に使う）</param>
    public static PlanWalk Over(
        IReadOnlyList<SyncStep> steps,
        LiveSchema live,
        SyncDialectCapabilities capabilities
    )
    {
        var limit = capabilities.IdentifierLengthLimit;
        var state = ConstraintState.From(live, capabilities);
        var positions = new List<WalkPosition>(steps.Count);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            positions.Add(
                new WalkPosition(
                    index,
                    step,
                    state,
                    DroppedIdentity(step, state, live, limit),
                    AddedIdentity(step, limit)
                )
            );
            state = state.Apply(step);
        }

        return new PlanWalk(positions, state);
    }

    /// <summary>文が外す制約の身元（外さない文は <c>null</c>）</summary>
    private static ConstraintIdentity? DroppedIdentity(
        SyncStep step,
        ConstraintState before,
        LiveSchema live,
        IdentifierLengthLimit limit
    ) =>
        step switch
        {
            DropForeignKeyStep drop => new ConstraintIdentity(
                ConstraintKind.ForeignKey,
                SchemaNames.ToStoredForm(drop.ChildTable),
                ForeignKeyStepNames.Dropped(
                    drop,
                    before.FindDropped(drop)?.Live?.Relationship,
                    live,
                    limit
                )
            ),
            DropUniqueConstraintStep drop => UniqueIdentity(
                drop.TableName,
                drop.Name,
                drop.Columns,
                limit
            ),
            DropPrimaryKeyStep drop => PrimaryKeyIdentity(drop.TableName),
            _ => null,
        };

    /// <summary>文が付ける制約の身元（付けない文は <c>null</c>）</summary>
    private static ConstraintIdentity? AddedIdentity(SyncStep step, IdentifierLengthLimit limit) =>
        step switch
        {
            AddForeignKeyStep add => new ConstraintIdentity(
                ConstraintKind.ForeignKey,
                SchemaNames.ToStoredForm(add.ChildTable),
                ForeignKeyStepNames.Added(add, limit)
            ),
            AddUniqueConstraintStep add => UniqueIdentity(
                add.TableName,
                add.Name,
                add.Columns,
                limit
            ),
            AddPrimaryKeyStep add => PrimaryKeyIdentity(add.TableName),
            _ => null,
        };

    private static ConstraintIdentity UniqueIdentity(
        string tableName,
        string? name,
        IReadOnlyList<string> columns,
        IdentifierLengthLimit limit
    ) =>
        new(
            ConstraintKind.UniqueConstraint,
            SchemaNames.ToStoredForm(tableName),
            UniqueConstraintNaming.Resolve(name, tableName, columns, limit)
        );

    private static ConstraintIdentity PrimaryKeyIdentity(string tableName) =>
        new(
            ConstraintKind.PrimaryKey,
            SchemaNames.ToStoredForm(tableName),
            ConstraintIdentity.PrimaryKeyName
        );
}
