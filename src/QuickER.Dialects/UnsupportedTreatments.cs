using System.Collections.Frozen;
using System.Collections.ObjectModel;
using static QuickER.Dialects.UnsupportedTreatment;
using Kind = QuickER.Dialects.UnsupportedDefinitionKind;

namespace QuickER.Dialects;

/// <summary>種類 × 機能の扱い（扱いの表の表 3 の写し）</summary>
/// <remarks>
/// 既定値を持たない（全種類 × 全機能のマスを明示して書き、網羅テストが欠けを赤にする）。種類ごとに 1 つの塊で
/// 8 機能を並べ、行末のコメントに表のマスの文言を縮めて書く（表と突き合わせるため）。方言に依らない機能
/// （C# 生成・出力・ツール・リバース）は 5 方言に同じ値を書く。取込の扱いは、方言の宣言が「検出する」の方言にだけ
/// 書く（段 3 の「告知せずに外す」だけは、あり得て検出しない方言に書く）。
/// </remarks>
public static class UnsupportedTreatments
{
    /// <summary>種類 × 機能 → 方言ごとの扱い</summary>
    public static IReadOnlyDictionary<
        (UnsupportedDefinitionKind Kind, UnsupportedFeature Feature),
        TreatmentCell
    > All { get; } =
        new ReadOnlyDictionary<
            (UnsupportedDefinitionKind Kind, UnsupportedFeature Feature),
            TreatmentCell
        >(Build());

    /// <summary>種類 × 機能 × 方言の扱いを引く</summary>
    /// <param name="kind">種類</param>
    /// <param name="feature">機能</param>
    /// <param name="dialect">方言名（大文字小文字を問わない）</param>
    /// <exception cref="ArgumentException">知らない方言名</exception>
    /// <exception cref="KeyNotFoundException">扱いの無いマス</exception>
    public static UnsupportedTreatment For(
        UnsupportedDefinitionKind kind,
        UnsupportedFeature feature,
        string dialect
    ) => All[(kind, feature)].For(dialect);

    /// <summary>機能ごとに使える扱いの値</summary>
    /// <param name="feature">機能</param>
    /// <exception cref="ArgumentOutOfRangeException">定義されていない機能</exception>
    public static IReadOnlySet<UnsupportedTreatment> AllowedFor(UnsupportedFeature feature) =>
        feature switch
        {
            UnsupportedFeature.Import => ImportAllowed,
            UnsupportedFeature.Diff => DiffAllowed,
            UnsupportedFeature.Sync => SyncAllowed,
            UnsupportedFeature.Ddl => DdlAllowed,
            UnsupportedFeature.CSharpGeneration => CSharpGenerationAllowed,
            UnsupportedFeature.Export => ExportAllowed,
            UnsupportedFeature.ToolHosts => ToolHostsAllowed,
            UnsupportedFeature.Reverse => ReverseAllowed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(feature),
                feature,
                "Unknown unsupported feature."
            ),
        };

    // 機能ごとに使える値（AllowedFor が返す）
    private static readonly FrozenSet<UnsupportedTreatment> ImportAllowed = FrozenSet.ToFrozenSet([
        None,
        Notice,
        MarkWithNotice,
        ExcludeWithNotice,
        ExcludeWithAggregatedNotice,
        ExcludeSilently,
    ]);

    private static readonly FrozenSet<UnsupportedTreatment> DiffAllowed = FrozenSet.ToFrozenSet([
        None,
        Fold,
        GuideItem,
    ]);

    private static readonly FrozenSet<UnsupportedTreatment> SyncAllowed = FrozenSet.ToFrozenSet([
        None,
        Annotation,
        OmitWithAnnotation,
        Warning,
        ExcludeWithWarning,
        Preserve,
        HandledNatively,
        HandledByRebuild,
        Reject,
        Reorder,
    ]);

    private static readonly FrozenSet<UnsupportedTreatment> DdlAllowed = FrozenSet.ToFrozenSet([
        None,
        Annotation,
        OmitWithAnnotation,
        Reject,
        Reorder,
    ]);

    private static readonly FrozenSet<UnsupportedTreatment> CSharpGenerationAllowed =
        FrozenSet.ToFrozenSet([None, FollowMark, Diagnostic]);

    private static readonly FrozenSet<UnsupportedTreatment> ExportAllowed = FrozenSet.ToFrozenSet([
        None,
        FollowMark,
        OmissionNotice,
    ]);

    private static readonly FrozenSet<UnsupportedTreatment> ToolHostsAllowed =
        FrozenSet.ToFrozenSet([None, FollowMark, Annotation, ConfirmBeforeApply]);

    private static readonly FrozenSet<UnsupportedTreatment> ReverseAllowed = FrozenSet.ToFrozenSet([
        None,
        RoundTrip,
        Warning,
    ]);

    // 5 方言に同じ扱い
    private static TreatmentCell Same(UnsupportedTreatment treatment) =>
        TreatmentCell.Same(treatment);

    // 1 種類の 8 機能のマスを足す（引数は省略できない＝機能の書き忘れはコンパイルで止まる）
    private static void Add(
        Dictionary<
            (UnsupportedDefinitionKind Kind, UnsupportedFeature Feature),
            TreatmentCell
        > cells,
        UnsupportedDefinitionKind kind,
        TreatmentCell import,
        TreatmentCell diff,
        TreatmentCell sync,
        TreatmentCell ddl,
        TreatmentCell cSharpGeneration,
        TreatmentCell export,
        TreatmentCell toolHosts,
        TreatmentCell reverse
    )
    {
        cells.Add((kind, UnsupportedFeature.Import), import);
        cells.Add((kind, UnsupportedFeature.Diff), diff);
        cells.Add((kind, UnsupportedFeature.Sync), sync);
        cells.Add((kind, UnsupportedFeature.Ddl), ddl);
        cells.Add((kind, UnsupportedFeature.CSharpGeneration), cSharpGeneration);
        cells.Add((kind, UnsupportedFeature.Export), export);
        cells.Add((kind, UnsupportedFeature.ToolHosts), toolHosts);
        cells.Add((kind, UnsupportedFeature.Reverse), reverse);
    }

    private static Dictionary<
        (UnsupportedDefinitionKind Kind, UnsupportedFeature Feature),
        TreatmentCell
    > Build()
    {
        var cells =
            new Dictionary<
                (UnsupportedDefinitionKind Kind, UnsupportedFeature Feature),
                TreatmentCell
            >();

        // ---- 向き 1 ----

        // A1 計算列・生成列
        Add(
            cells,
            Kind.ComputedColumn,
            import: Same(MarkWithNotice), // 印（IsComputed）＋告知（段 1）。5 方言
            diff: Same(None), // なし（型・NULL 許容の差は A1b）
            sync: Same(Annotation), // 注記（AddTable・AddColumn）。SQLite 再構築の警告も注記として扱う
            ddl: Same(Annotation), // 注記
            cSharpGeneration: Same(FollowMark), // 印に従う（INSERT・UPDATE から外す）
            export: Same(FollowMark), // 印に従う（Mermaid・DBML は欠落告知）
            toolHosts: Same(FollowMark), // 印に従う（保持のみ）
            reverse: Same(RoundTrip) // [ComputedColumn] で往復
        );

        // A2 既定値 DEFAULT
        Add(
            cells,
            Kind.DefaultValue,
            import: Same(Notice), // 告知（段 1）。5 方言で検出を追加
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: Preserve, // 列の変更で DEFAULT 制約を読み直し、外して付け直す（J1）。J2 の事前の警告（型の種類の変更）は別の経路で、このマスには表さない
                PostgreSql: Warning, // DEFAULT のある列の型の種類の変更を事前に警告（J2）
                MySql: Preserve, // 型変更のとき読み直して保つ。J2 の事前の警告は SQL Server と同じく別の経路で、このマスには表さない
                Oracle: Warning, // 同上（J2）
                Sqlite: Warning // 再構築は警告
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // A3 CHECK 制約
        Add(
            cells,
            Kind.CheckConstraint,
            import: Same(None), // なし（検出しない。理由つき宣言）
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Warning // 再構築は警告。他方言は触らない
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // A4 照合順序 COLLATE
        Add(
            cells,
            Kind.Collation,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Notice, // テーブルの既定と違う列だけ告知（段 1）
                Oracle: None, // 検出しない（J3）
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: Preserve, // 読み直して書き添える
                PostgreSql: Preserve, // 読み直して書き添える
                MySql: Preserve, // 読み直して保つ
                Oracle: None, // なし（既定の構成では列の照合順序を作れない・J3）
                Sqlite: Warning // 警告（今のまま）
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // A5a 自動採番（DB が指定した値を拒む列）
        Add(
            cells,
            Kind.IdentityRejectingValues,
            import: new TreatmentCell(
                SqlServer: MarkWithNotice, // 印＋告知（段 1）
                PostgreSql: MarkWithNotice,
                MySql: None, // あり得ない
                Oracle: MarkWithNotice,
                Sqlite: None // あり得ない
            ),
            diff: Same(None), // なし（有無の差は出さない）
            sync: new TreatmentCell(
                SqlServer: Annotation, // 普通の列＋注記（AddTable・AddColumn）
                PostgreSql: Annotation,
                MySql: None,
                Oracle: Annotation,
                Sqlite: None
            ),
            ddl: new TreatmentCell(
                SqlServer: Annotation, // 普通の列＋注記
                PostgreSql: Annotation,
                MySql: None,
                Oracle: Annotation,
                Sqlite: None
            ),
            cSharpGeneration: Same(FollowMark), // 印に従う（主キーの列は生成時に警告）
            export: Same(OmissionNotice), // Mermaid・DBML に欠落告知
            toolHosts: Same(FollowMark), // 印に従う（保持のみ）
            reverse: Same(RoundTrip) // C# 生成が刻む属性で往復
        );

        // A5b 自動採番（指定した値も受け付ける列）
        Add(
            cells,
            Kind.IdentityAcceptingValues,
            import: new TreatmentCell(
                SqlServer: None, // あり得ない
                PostgreSql: Notice, // 告知（段 1）
                MySql: Notice,
                Oracle: Notice,
                Sqlite: Notice
            ),
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None, // 保つ（今のまま）
                MySql: Preserve, // 型変更のとき AUTO_INCREMENT を読み直して保つ
                Oracle: None, // 保つ（今のまま）
                Sqlite: Warning // 再構築は警告
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // A6a INVISIBLE 列
        Add(
            cells,
            Kind.InvisibleColumn,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Notice, // MySQL だけ検出を追加（段 1）
                Oracle: None, // 検出しない（理由つき宣言）
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Preserve, // 型変更のとき読み直して保つ
                Oracle: None,
                Sqlite: None
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // A6b 列の ON UPDATE 属性
        Add(
            cells,
            Kind.ColumnOnUpdate,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Notice, // MySQL だけ検出を追加（段 1）
                Oracle: None,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Preserve, // 型変更のとき読み直して保つ
                Oracle: None,
                Sqlite: None
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // A6c SRID 属性
        Add(
            cells,
            Kind.SpatialReferenceId,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Notice, // MySQL だけ検出を追加（段 1）
                Oracle: None,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Preserve, // 型変更のとき読み直して保つ
                Oracle: None,
                Sqlite: None
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // A7 主キー列の NULL 許容の補正
        Add(
            cells,
            Kind.PrimaryKeyNullabilityAdjusted,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Notice // 告知（段 1）。告げない条件 3 つ
            ),
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: None // 同期は何もしない（NULL の行があると再構築で失敗しうることは取込の告知文で伝える）
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B1 非一意索引
        Add(
            cells,
            Kind.NonUniqueIndex,
            import: Same(None), // SQLite は再構築で作り直すために補助オブジェクトとして読む（告知はしない）。他は検出しない
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None, // 外部キー用の自動索引を外すのは外部キーの削除の実装（この種類の扱いではない）
                Oracle: None,
                Sqlite: Preserve // 再構築は CREATE 文を DB から読み直して作り直す
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B2 制約でない一意インデックス・関数索引など
        Add(
            cells,
            Kind.NonConstraintUniqueIndex,
            import: Same(None), // 告知しない。MySQL は一意制約として取り込み、SQLite は再構築で作り直すために補助オブジェクトとして読む
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Preserve // 再構築は CREATE 文を DB から読み直して作り直す（式・条件付きの索引も）
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B3 トリガー
        Add(
            cells,
            Kind.Trigger,
            import: Same(None), // SQLite は再構築で作り直すために補助オブジェクトとして読む（告知はしない）。他は検出しない
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None, // 同期は触らない
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Preserve // 再構築は CREATE 文を DB から読み直して作り直す
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B4a 無効化された制約
        Add(
            cells,
            Kind.DisabledConstraint,
            import: new TreatmentCell(
                SqlServer: ExcludeWithNotice, // 外す＋告知（外部キーだけ）
                PostgreSql: None,
                MySql: None,
                Oracle: ExcludeWithNotice, // 外す＋告知（PK・UNIQUE・外部キー）
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B4b 未検証の制約
        Add(
            cells,
            Kind.UnvalidatedConstraint,
            import: Same(None), // なし（以後の書き込みには強制が効く）
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B5 DEFERRABLE・ON CONFLICT・MATCH・NULLS NOT DISTINCT
        Add(
            cells,
            Kind.ConstraintClause,
            import: Same(None), // なし（検出しない）
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Warning // 再構築は ON CONFLICT・DEFERRABLE INITIALLY DEFERRED を警告
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C1 ビュー
        Add(
            cells,
            Kind.View,
            import: Same(ExcludeWithAggregatedNotice), // 外す＋まとめて告知（段 2）。5 方言
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Preserve // ビューは触らずに残す（再構築はビューを作り直さない）。再構築でビューが壊れるときは前後比較で失敗にしてロールバックする（警告ではなく失敗）
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C2a マテリアライズドビュー本体
        Add(
            cells,
            Kind.MaterializedView,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: ExcludeWithAggregatedNotice, // 外す＋まとめて告知（段 2）
                MySql: None,
                Oracle: ExcludeWithAggregatedNotice,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C2b MV ログ表・MV の内部表
        Add(
            cells,
            Kind.MaterializedViewInternals,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: ExcludeSilently, // 外す＋告知しない（段 3）
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C3a 仮想テーブル
        Add(
            cells,
            Kind.VirtualTable,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: ExcludeWithAggregatedNotice // 外す＋まとめて告知（段 2）
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C3b シャドウ表
        Add(
            cells,
            Kind.ShadowTable,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: ExcludeSilently // 外す＋告知しない（段 3）
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C3c 外部テーブル
        Add(
            cells,
            Kind.ExternalTable,
            import: new TreatmentCell(
                SqlServer: ExcludeWithAggregatedNotice, // is_external（J6）
                PostgreSql: ExcludeWithAggregatedNotice, // relkind f
                MySql: None, // FEDERATED は検出しない（J6）
                Oracle: ExcludeWithAggregatedNotice, // external=YES（U9）
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C4a パーティション親
        Add(
            cells,
            Kind.PartitionedTable,
            import: new TreatmentCell(
                SqlServer: Notice, // 告知（段 1）。SS を追加
                PostgreSql: Notice,
                MySql: Notice,
                Oracle: Notice,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし（パーティションなしの表を出すだけで、DDL に特別な処理は無い。告知は取込が担う）
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C4b 子パーティション
        Add(
            cells,
            Kind.ChildPartition,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: ExcludeWithAggregatedNotice, // 外す＋まとめて告知（段 2）
                MySql: None,
                Oracle: None,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C5 テンポラル履歴表
        Add(
            cells,
            Kind.TemporalHistoryTable,
            import: new TreatmentCell(
                SqlServer: ExcludeWithAggregatedNotice, // 外す＋まとめて告知（段 2）
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C6 テーブルオプション
        Add(
            cells,
            Kind.TableOption,
            import: Same(None), // なし（検出しない）
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Warning // 再構築は WITHOUT ROWID・STRICT を警告
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C7a 一時表
        Add(
            cells,
            Kind.TemporaryTable,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: ExcludeWithAggregatedNotice, // 外す＋まとめて告知（段 2）
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C7b 拡張が所有する表
        Add(
            cells,
            Kind.ExtensionOwnedTable,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: ExcludeSilently, // 外す＋告知しない（段 3）
                MySql: None,
                Oracle: None,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C7c システム表・ツールの表
        Add(
            cells,
            Kind.SystemTable,
            import: new TreatmentCell(
                SqlServer: ExcludeSilently, // 外す＋告知しない（段 3）
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: ExcludeSilently
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // C9 除外した表を参照する外部キー
        Add(
            cells,
            Kind.ForeignKeyToExcludedTable,
            import: Same(Notice), // 告知（段 1）
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // D1a 基底型へ平坦化される型
        Add(
            cells,
            Kind.FlattenedType,
            import: new TreatmentCell(
                SqlServer: Notice, // 別名型を追加（J4）
                PostgreSql: Notice, // ドメイン型（今のまま）
                MySql: None,
                Oracle: None,
                Sqlite: None
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // D1b ユーザー定義型の本体
        Add(
            cells,
            Kind.UserDefinedType,
            import: Same(None), // なし（型名のまま持ち帰る）
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // D2a 解析できない型
        Add(
            cells,
            Kind.UnparsableType,
            import: Same(None), // 告知しない（取込警告にしない）
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(Diagnostic), // 告知（Info・IsFallbackType）
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(Warning) // 警告（トークンが解けないとき）
        );

        // D2b DDL へ出せない型表記
        Add(
            cells,
            Kind.UnemittableTypeNotation,
            import: Same(Notice), // 告知（段 1）。5 方言
            diff: Same(None), // なし
            sync: Same(Reject), // 図全体を拒否（入口検証）
            ddl: Same(Reject), // 図全体を拒否（入口検証）
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // D2c 型を宣言していない列
        Add(
            cells,
            Kind.UndeclaredColumnType,
            import: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Notice // BLOB として取り込み告知（段 1）
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // N1 前後に空白のある表名・列名・制約名
        Add(
            cells,
            Kind.PaddedName,
            import: Same(Notice), // 告知（段 1）。5 方言
            diff: Same(Fold), // 比較で畳む（空白を除いた名前で対応づける）
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // T1 列が 1 本も読めない表
        Add(
            cells,
            Kind.TableWithoutReadableColumns,
            import: new TreatmentCell(
                SqlServer: None, // あり得ない（J5）
                PostgreSql: Notice, // 告知（段 1）
                MySql: Notice, // 列を扱う権限の無い表単位の権限だけのとき（実機で確認）
                Oracle: None, // あり得ない（見える列を必ず 1 本持つ。実機で確認）
                Sqlite: None // あり得ない（J5）
            ),
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // ---- 向き 2 ----

        // A1b 計算列の型・NULL 許容の変更
        Add(
            cells,
            Kind.ComputedColumnTypeChange,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: GuideItem, // 案内項目（PG・MySQL 以外の型の差）
                PostgreSql: None, // PG は選択可
                MySql: None, // MySQL も選択可（列の変更を実行時のカタログから組み直し、式と種別を保つ）
                Oracle: GuideItem,
                Sqlite: GuideItem
            ),
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B6 主キーと同じ列集合の一意制約
        Add(
            cells,
            Kind.UniqueConstraintOnPrimaryKey,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: Fold, // 比較で畳む（主キーが一意性を保証する）
                Sqlite: Fold
            ),
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: OmitWithAnnotation, // 注記（AddTable の UNIQUE 句を省く旨）
                Sqlite: OmitWithAnnotation
            ),
            ddl: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: OmitWithAnnotation, // 注記し UNIQUE 句を省く
                Sqlite: OmitWithAnnotation
            ),
            cSharpGeneration: Same(None), // なし（[UniqueConstraint] をそのまま出す）
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B7a 複合主キー・主キーなしの表
        Add(
            cells,
            Kind.CompositeOrMissingPrimaryKey,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(Diagnostic), // 告知（Warning）。Repository の対象外
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B7b 複合外部キー
        Add(
            cells,
            Kind.CompositeForeignKey,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(Diagnostic), // 告知（Warning）。ナビゲーションと EF Fluent をスキップ
            export: Same(OmissionNotice), // Mermaid は欠落告知（FK 列ペア）
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // B7c 多対多
        Add(
            cells,
            Kind.ManyToManyRelationship,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(Annotation), // 注記
            cSharpGeneration: Same(Diagnostic), // 告知（Warning）。ナビゲーションを作らない
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // D3 EditModel で入力できない列
        Add(
            cells,
            Kind.NotEditableColumn,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(Diagnostic), // 告知（Info）。EditModel・Mapper・値オブジェクトから外す
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // D5 方言が表せない型
        Add(
            cells,
            Kind.UnrepresentableType,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(Diagnostic), // 告知（Info・負のスケール）
            export: Same(None), // なし
            toolHosts: Same(ConfirmBeforeApply), // 方言切替で変換できない列を一覧し確認
            reverse: Same(Warning) // 警告（長さなしトークン）
        );

        // E1 参照アクション ON UPDATE
        Add(
            cells,
            Kind.OnUpdateReferentialAction,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: Fold, // 比較で畳む（NO ACTION へ）
                Sqlite: None
            ),
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: OmitWithAnnotation, // 注記（ON UPDATE が NO ACTION でないとき）
                Sqlite: None
            ),
            ddl: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: OmitWithAnnotation, // 注記
                Sqlite: None
            ),
            cSharpGeneration: Same(None), // なし（EF Core の Fluent は ON UPDATE を出さない）
            export: Same(OmissionNotice), // Mermaid は欠落告知、DBML は往復
            toolHosts: Same(None), // なし
            reverse: Same(RoundTrip) // ON UPDATE を読む
        );

        // E2 参照アクション SET DEFAULT
        Add(
            cells,
            Kind.SetDefaultReferentialAction,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: Fold, // 比較で畳む（NO ACTION へ）
                Oracle: Fold,
                Sqlite: None
            ),
            sync: new TreatmentCell(
                SqlServer: Warning, // NOT NULL の子の列への SET DEFAULT を計画の警告で名指し（J7）
                PostgreSql: None,
                MySql: OmitWithAnnotation, // 注記（句を省く旨）
                Oracle: OmitWithAnnotation, // 注記（句を省く旨）。Oracle にも付ける
                Sqlite: None
            ),
            ddl: new TreatmentCell(
                SqlServer: Annotation, // NOT NULL の子の列への SET DEFAULT を注記で名指し（J7）
                PostgreSql: None,
                MySql: OmitWithAnnotation, // 注記（句を省く旨）
                Oracle: OmitWithAnnotation, // 注記（句を省く旨）。Oracle にも付ける
                Sqlite: None
            ),
            cSharpGeneration: Same(None), // なし（EF Core は ClientNoAction）
            export: Same(OmissionNotice), // Mermaid は欠落告知、DBML は往復
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // E3 参照アクション SET NULL の外部キーの NOT NULL の子の列
        Add(
            cells,
            Kind.SetNullOnNotNullColumn,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: ExcludeWithWarning, // 作れない（1761）。選んだ追加・作り直しを外し、DB の外部キーは外し直さない
                PostgreSql: Warning, // 作れるが、参照される親の行の削除・更新が失敗する
                MySql: ExcludeWithWarning, // 作れない（1830）。選んだ追加・作り直しを外し、DB の外部キーは外し直さない
                Oracle: Warning, // 作れるが、参照される親の行の削除が失敗する
                Sqlite: Warning // 作れるが、参照される親の行の削除・更新が失敗する
            ),
            ddl: Same(Annotation), // 外部キーの文の前に、NOT NULL の子の列を名指しして注記する
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // E5 説明（コメント）
        Add(
            cells,
            Kind.Description,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Fold // 比較で畳む（説明の差を出さない）
            ),
            sync: Same(None), // なし
            ddl: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Annotation // 注記（--コメントで説明を残す）
            ),
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // E6 スキーマ付きテーブル名
        Add(
            cells,
            Kind.SchemaQualifiedTableName,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None, // 逆引きは to_regclass で ALTER TABLE と同じ表に絞る
                MySql: None,
                Oracle: None,
                Sqlite: Warning // ドットを含む表名を計画の警告で名指し
            ),
            ddl: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Annotation // ドットを含む表名を DDL に注記
            ),
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // X1 列順
        Add(
            cells,
            Kind.ColumnOrder,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: GuideItem, // 案内項目
                PostgreSql: GuideItem,
                MySql: None, // 選択可
                Oracle: GuideItem,
                Sqlite: None // 選択可
            ),
            sync: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: HandledNatively, // ネイティブ
                Oracle: None,
                Sqlite: HandledByRebuild // 再構築へ畳む
            ),
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // X2 外部キーの制約名
        Add(
            cells,
            Kind.ForeignKeyConstraintName,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: None,
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: Fold // 比較で畳む（子表・親表・子列で対応づける）
            ),
            sync: Same(None), // なし
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // X3 複合外部キーの列の並び
        Add(
            cells,
            Kind.CompositeForeignKeyColumnOrder,
            import: Same(None), // なし
            diff: Same(Fold), // 比較で畳む（列ペアを並びを見ずに比べる。並べ替えは DDL・同期の出力で行う）
            sync: new TreatmentCell(
                SqlServer: Reorder, // 外部キーの追加を参照先の同期後のキーの並びへ並べ替えて出す
                PostgreSql: None,
                MySql: Reorder,
                Oracle: None,
                Sqlite: None
            ),
            ddl: new TreatmentCell(
                SqlServer: Reorder, // 参照先キーの並びへ並べ替えて出す
                PostgreSql: None,
                MySql: Reorder,
                Oracle: None,
                Sqlite: None
            ),
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // X4 外部キー両端の型の不整合
        Add(
            cells,
            Kind.ForeignKeyTypeMismatch,
            import: Same(None), // なし
            diff: new TreatmentCell(
                SqlServer: None,
                PostgreSql: Fold, // 比較で畳む（長さだけの違いを食い違いとみなさない・J9）
                MySql: Fold,
                Oracle: Fold,
                Sqlite: Fold
            ),
            sync: Same(Warning), // 警告（ForeignKeyColumnTypeMismatch）
            ddl: new TreatmentCell(
                SqlServer: Annotation, // 長さの違う外部キーを注記で名指し（J10）
                PostgreSql: None,
                MySql: None,
                Oracle: None,
                Sqlite: None
            ),
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(None), // なし
            reverse: Same(None) // なし
        );

        // X5 候補キーでない列を参照する外部キー
        Add(
            cells,
            Kind.ForeignKeyNotOnCandidateKey,
            import: Same(None), // なし
            diff: Same(None), // なし
            sync: Same(Warning), // 警告（ForeignKeyNotOnCandidateKey ほか）。実行は止めない
            ddl: Same(None), // なし
            cSharpGeneration: Same(None), // なし
            export: Same(None), // なし
            toolHosts: Same(Annotation), // 注記（add_relationship の注意文）
            reverse: Same(None) // なし
        );

        return cells;
    }
}
