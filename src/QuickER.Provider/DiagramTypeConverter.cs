using QuickER.Model;

namespace QuickER.Provider;

/// <summary>1 カラム分の型変換計画</summary>
/// <param name="ColumnId">対象カラムの ID</param>
/// <param name="TableName">対象カラムが属するテーブル名</param>
/// <param name="ColumnName">対象カラム名</param>
/// <param name="OldType">変換前（from 方言）のネイティブ型文字列</param>
/// <param name="NewType">変換後（to 方言）のネイティブ型文字列。<c>null</c> は変換不能（元の型を保持する）ことを表す</param>
/// <param name="MakeNullable">
/// 変換にあわせて NOT NULL を解除するか。行バージョン列（SQL Server の <c>rowversion</c>）が、
/// 行バージョンを持たない方言のただのバイナリ列へ落ちるときだけ <c>true</c> になる。
/// 元が NULL 許容の列は解除するものが無いため <c>false</c>（＝この値が真なら必ず NOT NULL → NULL 許容の変更になる）。
/// </param>
public sealed record ColumnTypeConversion(
    Guid ColumnId,
    string TableName,
    string ColumnName,
    string OldType,
    string? NewType,
    bool MakeNullable = false
);

/// <summary>方言切替時の型変換計画。<see cref="Unconverted"/> が警告一覧になる</summary>
public sealed class DiagramTypeConversionPlan
{
    /// <summary>変換に成功したカラムの一覧</summary>
    public required IReadOnlyList<ColumnTypeConversion> Converted { get; init; }

    /// <summary>変換できなかったカラムの一覧（警告表示用。型は変更されない）</summary>
    public required IReadOnlyList<ColumnTypeConversion> Unconverted { get; init; }
}

/// <summary>
/// 図のターゲット DBMS 切替時に、カラムのネイティブ型を正規型経由で変換する計画を作る純関数群。
/// </summary>
/// <remarks>
/// 図そのものは変更しない。作成した計画の適用は GUI 側が Undo 対応で行うため、計画作成と適用を分離している。
/// </remarks>
public static class DiagramTypeConverter
{
    /// <summary>
    /// <paramref name="from"/> 方言の型を <paramref name="to"/> 方言の型へ変換する計画を作る。
    /// <paramref name="from"/> と <paramref name="to"/> が同一インスタンスなら空の計画を返す。
    /// </summary>
    /// <param name="diagram">変換対象の図</param>
    /// <param name="from">変換前の型カタログ（現在の図の方言）</param>
    /// <param name="to">変換後の型カタログ（切替先の方言）</param>
    public static DiagramTypeConversionPlan CreatePlan(
        ErDiagram diagram,
        ITypeCatalog from,
        ITypeCatalog to
    )
    {
        if (ReferenceEquals(from, to))
        {
            return new DiagramTypeConversionPlan { Converted = [], Unconverted = [] };
        }

        var converted = new List<ColumnTypeConversion>();
        var unconverted = new List<ColumnTypeConversion>();

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                var oldType = column.DataType;

                if (
                    from.TryParse(oldType, out var canonical)
                    && to.TryFormat(canonical, out var newType)
                )
                {
                    converted.Add(
                        new ColumnTypeConversion(
                            column.Id,
                            entity.TableName,
                            column.Name,
                            oldType,
                            newType,
                            MakeNullable: LosesRowVersion(canonical, newType, to)
                                && !column.IsNullable
                        )
                    );
                }
                else
                {
                    unconverted.Add(
                        new ColumnTypeConversion(
                            column.Id,
                            entity.TableName,
                            column.Name,
                            oldType,
                            null
                        )
                    );
                }
            }
        }

        return new DiagramTypeConversionPlan { Converted = converted, Unconverted = unconverted };
    }

    /// <summary>
    /// 行バージョン列が、変換先の方言では「行バージョンでないただの列」へ落ちるかを判定する。
    /// </summary>
    /// <remarks>
    /// 判定は変換先の型を <paramref name="to"/> 自身で読み直し、<see cref="CanonicalTypeKind.RowVersion"/> のままかを見る
    /// （方言名で分岐しない＝行バージョンを持つ方言が増えても正しく効く）。落ちる場合、その列は DB が採番しなくなり、
    /// 書き手（同期処理）が値を入れるまで空になるため NOT NULL のままでは行を作れない。
    /// </remarks>
    private static bool LosesRowVersion(CanonicalType canonical, string newType, ITypeCatalog to) =>
        canonical.Kind == CanonicalTypeKind.RowVersion
        && !(
            to.TryParse(newType, out var roundTripped)
            && roundTripped.Kind == CanonicalTypeKind.RowVersion
        );

    /// <summary>
    /// 計画を図へ適用する（<see cref="DiagramTypeConversionPlan.Converted"/> のみ反映）。
    /// テスト・CLI 用の素朴な適用であり、Undo/Redo 対応は呼び出し側（GUI）の責務とする。
    /// </summary>
    /// <remarks>
    /// 型の書き換えに加え、<see cref="ColumnTypeConversion.MakeNullable"/> が真の列は NOT NULL を解除する。
    /// </remarks>
    /// <param name="diagram">適用対象の図</param>
    /// <param name="plan">適用する変換計画</param>
    public static void Apply(ErDiagram diagram, DiagramTypeConversionPlan plan)
    {
        if (plan.Converted.Count == 0)
        {
            return;
        }

        var columnsById = diagram.Entities.SelectMany(e => e.Columns).ToDictionary(c => c.Id);

        foreach (var conversion in plan.Converted)
        {
            if (
                conversion.NewType is not null
                && columnsById.TryGetValue(conversion.ColumnId, out var column)
            )
            {
                column.DataType = conversion.NewType;

                if (conversion.MakeNullable)
                {
                    column.IsNullable = true;
                }
            }
        }
    }
}
