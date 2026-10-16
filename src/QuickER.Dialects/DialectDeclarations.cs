using System.Collections.ObjectModel;
using static QuickER.Dialects.SyncAlterEffect;
using Kind = QuickER.Dialects.UnsupportedDefinitionKind;

namespace QuickER.Dialects;

/// <summary>方言 × 種類の宣言（扱いの表の表 2 の写し）</summary>
/// <remarks>
/// 既定値を持たない（5 方言 × 全種類を明示して書き、網羅テストが欠けを赤にする）。方言ごとに 1 つのメソッドが
/// 全種類を並べる。向き 2 の種類は「方言がその定義を表せるか」だけを持ち、検出も同期も該当しない。
/// </remarks>
public static class DialectDeclarations
{
    /// <summary>方言名（<see cref="DialectNames"/> の正規の綴り）→ 種類 → 宣言</summary>
    public static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<UnsupportedDefinitionKind, DialectKindDeclaration>
    > All { get; } =
        new ReadOnlyDictionary<
            string,
            IReadOnlyDictionary<UnsupportedDefinitionKind, DialectKindDeclaration>
        >(
            new Dictionary<
                string,
                IReadOnlyDictionary<UnsupportedDefinitionKind, DialectKindDeclaration>
            >
            {
                { DialectNames.SqlServer, ReadOnly(SqlServer()) },
                { DialectNames.PostgreSql, ReadOnly(PostgreSql()) },
                { DialectNames.MySql, ReadOnly(MySql()) },
                { DialectNames.Oracle, ReadOnly(Oracle()) },
                { DialectNames.Sqlite, ReadOnly(Sqlite()) },
            }
        );

    /// <summary>方言 × 種類の宣言を引く</summary>
    /// <param name="dialect">方言名（大文字小文字を問わない）</param>
    /// <param name="kind">種類</param>
    /// <exception cref="ArgumentException">知らない方言名</exception>
    /// <exception cref="KeyNotFoundException">宣言の無い種類</exception>
    public static DialectKindDeclaration For(string dialect, UnsupportedDefinitionKind kind)
    {
        var name =
            DialectNames.Find(dialect)
            ?? throw new ArgumentException($"Unknown dialect name: '{dialect}'.", nameof(dialect));

        return All[name][kind];
    }

    /// <summary>その方言でこの種類の定義があり得るか（宣言が「あり得ない」でない）</summary>
    /// <param name="dialect">方言名（大文字小文字を問わない）</param>
    /// <param name="kind">種類</param>
    /// <exception cref="ArgumentException">知らない方言名</exception>
    public static bool CanOccur(string dialect, UnsupportedDefinitionKind kind) =>
        For(dialect, kind).Occurrence != DialectOccurrence.Impossible;

    // 検出しない理由（表 2 の備考から。種類ごとに 1 つ）
    private const string CheckConstraintReason =
        "保存後の機能の振る舞いを変えず、取込で検出を足す対象に入っていない（SQLite の再構築は CHECK の喪失を警告する）";

    private const string CollationReason =
        "取込で検出を足すのは MySQL の MODIFY が落とす属性だけ（SQL Server・PostgreSQL は同期で照合順序を読み直して保ち、SQLite は再構築の前に警告する）";

    private const string CollationOracleReason =
        "既定の構成（MAX_STRING_SIZE=STANDARD）では列の照合順序を作れず、EXTENDED の DB の挙動は確かめていない";

    private const string InvisibleColumnOracleReason =
        "取込で検出を足すのは MySQL の MODIFY が落とす属性だけ（Oracle の MODIFY は INVISIBLE を保つ）";

    private const string NonUniqueIndexReason =
        "保存後の機能の振る舞いを変えず、取込で検出を足す対象に入っていない（SQLite だけ補助オブジェクトとして取り込む）";

    private const string NonConstraintUniqueIndexReason =
        "保存後の機能の振る舞いを変えず、取込で検出を足す対象に入っていない（MySQL は一意インデックスを一意制約として取り込み、SQLite は補助オブジェクトとして取り込む）";

    private const string TriggerReason =
        "保存後の機能の振る舞いを変えず、取込で検出を足す対象に入っていない（SQLite だけ補助オブジェクトとして取り込む）";

    private const string UnvalidatedConstraintReason = "取り込んだ制約は以後の書き込みに強制が効く";

    private const string ConstraintClauseReason =
        "保存後の機能の振る舞いを変えず、取込で検出を足す対象に入っていない（SQLite の再構築は ON CONFLICT・DEFERRABLE の喪失を警告する）";

    private const string MaterializedViewInternalsReason =
        "DB の内部の表なので、告知せずに外す（段 3）";

    private const string ShadowTableReason =
        "仮想テーブルの付属表なので、告知せずに外す（段 3。本体の告知が代わりを務める）";

    private const string ExternalTableMySqlReason =
        "FEDERATED の表はローカルに列を持ち DML も転送されるので、普通の表として取り込む（ENGINE と同じ扱い）";

    private const string TableOptionReason =
        "保存後の機能の振る舞いを変えず、取込で検出を足す対象に入っていない（SQLite の再構築は WITHOUT ROWID・STRICT の喪失を警告する）";

    private const string ExtensionOwnedTableReason =
        "拡張が所有する表なので、告知せずに外す（段 3）";

    private const string SystemTableReason = "DB・ツールのシステム表なので、告知せずに外す（段 3）";

    private const string UserDefinedTypeReason = "型名のまま持ち帰り、同じ DB への同期は型名で通る";

    private const string UnparsableTypeReason =
        "型表記は保持され定義は落ちていない。承知で使う型が取込のたびに鳴って形骸化するので、生成時の Info で告げる";

    // 向き 1：あり得て、取込で検出する
    private static DialectKindDeclaration Detected(SyncAlterEffect sync) =>
        new(DialectOccurrence.Possible, ImportDetection.Detected, sync, null);

    // 向き 1：あり得るが、取込で検出しない（理由つき）
    private static DialectKindDeclaration NotDetected(SyncAlterEffect sync, string reason) =>
        new(DialectOccurrence.Possible, ImportDetection.NotDetected, sync, reason);

    // 向き 1：一部の形だけあり得て、取込で検出しない（理由つき）
    private static DialectKindDeclaration PartlyNotDetected(SyncAlterEffect sync, string reason) =>
        new(DialectOccurrence.Partial, ImportDetection.NotDetected, sync, reason);

    // 向き 1：あり得ない
    private static DialectKindDeclaration Impossible() =>
        new(DialectOccurrence.Impossible, ImportDetection.NotApplicable, NotApplicable, null);

    // 向き 2：方言が表せる
    private static DialectKindDeclaration Expressible() =>
        new(DialectOccurrence.Possible, ImportDetection.NotApplicable, NotApplicable, null);

    // 向き 2：方言が一部だけ表せる
    private static DialectKindDeclaration PartlyExpressible() =>
        new(DialectOccurrence.Partial, ImportDetection.NotApplicable, NotApplicable, null);

    // 向き 2：方言が表せない
    private static DialectKindDeclaration NotExpressible() =>
        new(DialectOccurrence.Impossible, ImportDetection.NotApplicable, NotApplicable, null);

    private static ReadOnlyDictionary<UnsupportedDefinitionKind, DialectKindDeclaration> ReadOnly(
        Dictionary<UnsupportedDefinitionKind, DialectKindDeclaration> declarations
    ) => new(declarations);

    private static Dictionary<UnsupportedDefinitionKind, DialectKindDeclaration> SqlServer() =>
        new()
        {
            // 向き 1
            { Kind.ComputedColumn, Detected(NotApplicable) }, // A1 ○ 既 該
            { Kind.DefaultValue, Detected(Keeps) }, // A2 ○ 33 保
            { Kind.CheckConstraint, NotDetected(Keeps, CheckConstraintReason) }, // A3 ○ 無 保
            { Kind.Collation, NotDetected(Drops, CollationReason) }, // A4 ○ 無 落
            { Kind.IdentityRejectingValues, Detected(Keeps) }, // A5a ○ 33 保
            { Kind.IdentityAcceptingValues, Impossible() }, // A5b ×
            { Kind.InvisibleColumn, Impossible() }, // A6a ×
            { Kind.ColumnOnUpdate, Impossible() }, // A6b ×
            { Kind.SpatialReferenceId, Impossible() }, // A6c ×
            { Kind.PrimaryKeyNullabilityAdjusted, Impossible() }, // A7 ×
            { Kind.NonUniqueIndex, NotDetected(Keeps, NonUniqueIndexReason) }, // B1 ○ 無 保
            { Kind.NonConstraintUniqueIndex, NotDetected(Keeps, NonConstraintUniqueIndexReason) }, // B2 ○ 無 保
            { Kind.Trigger, NotDetected(Keeps, TriggerReason) }, // B3 ○ 無 保
            { Kind.DisabledConstraint, Detected(NotApplicable) }, // B4a ○ 既 該
            { Kind.UnvalidatedConstraint, NotDetected(Keeps, UnvalidatedConstraintReason) }, // B4b ○ 無 保
            { Kind.ConstraintClause, Impossible() }, // B5 ×
            { Kind.View, Detected(Keeps) }, // C1 ○ 33 保
            { Kind.MaterializedView, Impossible() }, // C2a ×
            { Kind.MaterializedViewInternals, Impossible() }, // C2b ×
            { Kind.VirtualTable, Impossible() }, // C3a ×
            { Kind.ShadowTable, Impossible() }, // C3b ×
            { Kind.ExternalTable, Detected(NotApplicable) }, // C3c ○ 33 該
            { Kind.PartitionedTable, Detected(NotApplicable) }, // C4a ○ 33 該
            { Kind.ChildPartition, Impossible() }, // C4b ×
            { Kind.TemporalHistoryTable, Detected(NotApplicable) }, // C5 ○ 既 該
            { Kind.TableOption, NotDetected(Keeps, TableOptionReason) }, // C6 ○ 無 保
            { Kind.TemporaryTable, Impossible() }, // C7a ×
            { Kind.ExtensionOwnedTable, Impossible() }, // C7b ×
            { Kind.SystemTable, NotDetected(NotApplicable, SystemTableReason) }, // C7c ○ 無 該
            { Kind.ForeignKeyToExcludedTable, Detected(NotApplicable) }, // C9 ○ 36 該（名前に . を含むため外したテーブルを参照する外部キー）
            { Kind.FlattenedType, Detected(NotApplicable) }, // D1a ○ 33 該
            { Kind.UserDefinedType, NotDetected(NotApplicable, UserDefinedTypeReason) }, // D1b ○ 無 該
            { Kind.UnparsableType, NotDetected(NotApplicable, UnparsableTypeReason) }, // D2a ○ 無 該
            { Kind.UnemittableTypeNotation, Detected(NotApplicable) }, // D2b ○ 既 該
            { Kind.PaddedName, Detected(NotApplicable) }, // N1 ○ 33 該
            { Kind.TableWithoutReadableColumns, Impossible() }, // T1 ×
            // 向き 2
            { Kind.ComputedColumnTypeChange, NotExpressible() }, // A1b ×
            { Kind.UniqueConstraintOnPrimaryKey, Expressible() }, // B6 ○
            { Kind.CompositeOrMissingPrimaryKey, Expressible() }, // B7a ○
            { Kind.CompositeForeignKey, Expressible() }, // B7b ○
            { Kind.ManyToManyRelationship, Expressible() }, // B7c ○
            { Kind.NotEditableColumn, NotExpressible() }, // D3 ×
            { Kind.UnrepresentableType, PartlyExpressible() }, // D5 △
            { Kind.OnUpdateReferentialAction, Expressible() }, // E1 ○
            { Kind.SetDefaultReferentialAction, PartlyExpressible() }, // E2 △
            { Kind.Description, Expressible() }, // E5 ○
            { Kind.SchemaQualifiedTableName, Expressible() }, // E6 ○
            { Kind.ColumnOrder, NotExpressible() }, // X1 ×
            { Kind.ForeignKeyConstraintName, Expressible() }, // X2 ○
            { Kind.CompositeForeignKeyColumnOrder, Expressible() }, // X3 ○（並び必須）
            { Kind.ForeignKeyTypeMismatch, NotExpressible() }, // X4 ×（長さも一致が要る）
            { Kind.ForeignKeyNotOnCandidateKey, NotExpressible() }, // X5 ×
        };

    private static Dictionary<UnsupportedDefinitionKind, DialectKindDeclaration> PostgreSql() =>
        new()
        {
            // 向き 1
            { Kind.ComputedColumn, Detected(Keeps) }, // A1 ○ 既 保
            { Kind.DefaultValue, Detected(Keeps) }, // A2 ○ 33 保
            { Kind.CheckConstraint, NotDetected(Keeps, CheckConstraintReason) }, // A3 ○ 無 保
            { Kind.Collation, NotDetected(Drops, CollationReason) }, // A4 ○ 無 落
            { Kind.IdentityRejectingValues, Detected(Keeps) }, // A5a ○ 33 保
            { Kind.IdentityAcceptingValues, Detected(Keeps) }, // A5b ○ 33 保
            { Kind.InvisibleColumn, Impossible() }, // A6a ×
            { Kind.ColumnOnUpdate, Impossible() }, // A6b ×
            { Kind.SpatialReferenceId, Impossible() }, // A6c ×
            { Kind.PrimaryKeyNullabilityAdjusted, Impossible() }, // A7 ×
            { Kind.NonUniqueIndex, NotDetected(Keeps, NonUniqueIndexReason) }, // B1 ○ 無 保
            { Kind.NonConstraintUniqueIndex, NotDetected(Keeps, NonConstraintUniqueIndexReason) }, // B2 ○ 無 保
            { Kind.Trigger, NotDetected(Keeps, TriggerReason) }, // B3 ○ 無 保
            { Kind.DisabledConstraint, Impossible() }, // B4a ×
            { Kind.UnvalidatedConstraint, NotDetected(Keeps, UnvalidatedConstraintReason) }, // B4b ○ 無 保
            { Kind.ConstraintClause, NotDetected(Keeps, ConstraintClauseReason) }, // B5 ○ 無 保
            { Kind.View, Detected(Keeps) }, // C1 ○ 33 保
            { Kind.MaterializedView, Detected(NotApplicable) }, // C2a ○ 33 該
            { Kind.MaterializedViewInternals, Impossible() }, // C2b ×
            { Kind.VirtualTable, Impossible() }, // C3a ×
            { Kind.ShadowTable, Impossible() }, // C3b ×
            { Kind.ExternalTable, Detected(NotApplicable) }, // C3c ○ 33 該
            { Kind.PartitionedTable, Detected(NotApplicable) }, // C4a ○ 既 該
            { Kind.ChildPartition, Detected(NotApplicable) }, // C4b ○ 33 該
            { Kind.TemporalHistoryTable, Impossible() }, // C5 ×
            { Kind.TableOption, NotDetected(Keeps, TableOptionReason) }, // C6 ○ 無 保
            { Kind.TemporaryTable, Impossible() }, // C7a ×
            { Kind.ExtensionOwnedTable, NotDetected(NotApplicable, ExtensionOwnedTableReason) }, // C7b ○ 無 該
            { Kind.SystemTable, Impossible() }, // C7c ×
            { Kind.ForeignKeyToExcludedTable, Detected(NotApplicable) }, // C9 ○ 33 該
            { Kind.FlattenedType, Detected(NotApplicable) }, // D1a ○ 既 該
            { Kind.UserDefinedType, NotDetected(NotApplicable, UserDefinedTypeReason) }, // D1b ○ 無 該
            { Kind.UnparsableType, NotDetected(NotApplicable, UnparsableTypeReason) }, // D2a ○ 無 該
            { Kind.UnemittableTypeNotation, Detected(NotApplicable) }, // D2b ○ 既 該
            { Kind.PaddedName, Detected(NotApplicable) }, // N1 ○ 33 該
            { Kind.TableWithoutReadableColumns, Detected(NotApplicable) }, // T1 ○ 既 該
            // 向き 2
            { Kind.ComputedColumnTypeChange, Expressible() }, // A1b ○
            { Kind.UniqueConstraintOnPrimaryKey, Expressible() }, // B6 ○
            { Kind.CompositeOrMissingPrimaryKey, Expressible() }, // B7a ○
            { Kind.CompositeForeignKey, Expressible() }, // B7b ○
            { Kind.ManyToManyRelationship, Expressible() }, // B7c ○
            { Kind.NotEditableColumn, Expressible() }, // D3 ○
            { Kind.UnrepresentableType, PartlyExpressible() }, // D5 △
            { Kind.OnUpdateReferentialAction, Expressible() }, // E1 ○
            { Kind.SetDefaultReferentialAction, Expressible() }, // E2 ○
            { Kind.Description, Expressible() }, // E5 ○
            { Kind.SchemaQualifiedTableName, Expressible() }, // E6 ○（逆引きは to_regclass）
            { Kind.ColumnOrder, NotExpressible() }, // X1 ×
            { Kind.ForeignKeyConstraintName, Expressible() }, // X2 ○
            { Kind.CompositeForeignKeyColumnOrder, Expressible() }, // X3 ○（並び不問）
            { Kind.ForeignKeyTypeMismatch, Expressible() }, // X4 ○（長さの違いを許す）
            { Kind.ForeignKeyNotOnCandidateKey, NotExpressible() }, // X5 ×
        };

    private static Dictionary<UnsupportedDefinitionKind, DialectKindDeclaration> MySql() =>
        new()
        {
            // 向き 1
            { Kind.ComputedColumn, Detected(Keeps) }, // A1 ○ 既 保
            { Kind.DefaultValue, Detected(Drops) }, // A2 ○ 33 落
            { Kind.CheckConstraint, NotDetected(Keeps, CheckConstraintReason) }, // A3 ○ 無 保
            { Kind.Collation, Detected(Drops) }, // A4 ○ 33 落
            { Kind.IdentityRejectingValues, Impossible() }, // A5a ×
            { Kind.IdentityAcceptingValues, Detected(Drops) }, // A5b ○ 33 落
            { Kind.InvisibleColumn, Detected(Drops) }, // A6a ○ 33 落
            { Kind.ColumnOnUpdate, Detected(Drops) }, // A6b ○ 33 落
            { Kind.SpatialReferenceId, Detected(Drops) }, // A6c ○ 33 落
            { Kind.PrimaryKeyNullabilityAdjusted, Impossible() }, // A7 ×
            { Kind.NonUniqueIndex, NotDetected(Drops, NonUniqueIndexReason) }, // B1 ○ 無 落
            { Kind.NonConstraintUniqueIndex, NotDetected(Keeps, NonConstraintUniqueIndexReason) }, // B2 ○ 無 保
            { Kind.Trigger, NotDetected(Keeps, TriggerReason) }, // B3 ○ 無 保
            { Kind.DisabledConstraint, Impossible() }, // B4a ×
            { Kind.UnvalidatedConstraint, Impossible() }, // B4b ×
            { Kind.ConstraintClause, Impossible() }, // B5 ×
            { Kind.View, Detected(Keeps) }, // C1 ○ 33 保
            { Kind.MaterializedView, Impossible() }, // C2a ×
            { Kind.MaterializedViewInternals, Impossible() }, // C2b ×
            { Kind.VirtualTable, Impossible() }, // C3a ×
            { Kind.ShadowTable, Impossible() }, // C3b ×
            { Kind.ExternalTable, NotDetected(NotApplicable, ExternalTableMySqlReason) }, // C3c ○ 無 該
            { Kind.PartitionedTable, Detected(NotApplicable) }, // C4a ○ 既 該
            { Kind.ChildPartition, Impossible() }, // C4b ×
            { Kind.TemporalHistoryTable, Impossible() }, // C5 ×
            { Kind.TableOption, NotDetected(Keeps, TableOptionReason) }, // C6 ○ 無 保
            { Kind.TemporaryTable, Impossible() }, // C7a ×
            { Kind.ExtensionOwnedTable, Impossible() }, // C7b ×
            { Kind.SystemTable, Impossible() }, // C7c ×
            { Kind.ForeignKeyToExcludedTable, Detected(NotApplicable) }, // C9 ○ 36 該（名前に . を含むため外したテーブルを参照する外部キー）
            { Kind.FlattenedType, Impossible() }, // D1a ×
            { Kind.UserDefinedType, Impossible() }, // D1b ×
            { Kind.UnparsableType, NotDetected(NotApplicable, UnparsableTypeReason) }, // D2a ○ 無 該
            { Kind.UnemittableTypeNotation, Detected(NotApplicable) }, // D2b ○ 既 該
            { Kind.PaddedName, Detected(NotApplicable) }, // N1 ○ 33 該
            { Kind.TableWithoutReadableColumns, Detected(NotApplicable) }, // T1 ○ 既 該（TRIGGER・DROP・INDEX・SHOW VIEW だけの表単位の権限では表が見えて列が 0 本。実機で確認）
            // 向き 2
            { Kind.ComputedColumnTypeChange, NotExpressible() }, // A1b ×（?）
            { Kind.UniqueConstraintOnPrimaryKey, Expressible() }, // B6 ○
            { Kind.CompositeOrMissingPrimaryKey, Expressible() }, // B7a ○
            { Kind.CompositeForeignKey, Expressible() }, // B7b ○
            { Kind.ManyToManyRelationship, Expressible() }, // B7c ○
            { Kind.NotEditableColumn, NotExpressible() }, // D3 ×
            { Kind.UnrepresentableType, PartlyExpressible() }, // D5 △
            { Kind.OnUpdateReferentialAction, Expressible() }, // E1 ○
            { Kind.SetDefaultReferentialAction, NotExpressible() }, // E2 ×
            { Kind.Description, Expressible() }, // E5 ○
            { Kind.SchemaQualifiedTableName, Expressible() }, // E6 ○
            { Kind.ColumnOrder, Expressible() }, // X1 ○（ネイティブ）
            { Kind.ForeignKeyConstraintName, Expressible() }, // X2 ○
            { Kind.CompositeForeignKeyColumnOrder, Expressible() }, // X3 ○（並び必須）
            { Kind.ForeignKeyTypeMismatch, Expressible() }, // X4 ○（長さの違いを許す）
            { Kind.ForeignKeyNotOnCandidateKey, Expressible() }, // X5 ○（索引があれば可）
        };

    private static Dictionary<UnsupportedDefinitionKind, DialectKindDeclaration> Oracle() =>
        new()
        {
            // 向き 1
            { Kind.ComputedColumn, Detected(NotApplicable) }, // A1 ○ 既 該
            { Kind.DefaultValue, Detected(Keeps) }, // A2 ○ 33 保
            { Kind.CheckConstraint, NotDetected(Keeps, CheckConstraintReason) }, // A3 ○ 無 保
            { Kind.Collation, PartlyNotDetected(NotApplicable, CollationOracleReason) }, // A4 △ 無 該
            { Kind.IdentityRejectingValues, Detected(Keeps) }, // A5a ○ 33 保
            { Kind.IdentityAcceptingValues, Detected(Keeps) }, // A5b ○ 33 保
            { Kind.InvisibleColumn, NotDetected(Keeps, InvisibleColumnOracleReason) }, // A6a ○ 無 保
            { Kind.ColumnOnUpdate, Impossible() }, // A6b ×
            { Kind.SpatialReferenceId, Impossible() }, // A6c ×
            { Kind.PrimaryKeyNullabilityAdjusted, Impossible() }, // A7 ×
            { Kind.NonUniqueIndex, NotDetected(Keeps, NonUniqueIndexReason) }, // B1 ○ 無 保
            { Kind.NonConstraintUniqueIndex, NotDetected(Keeps, NonConstraintUniqueIndexReason) }, // B2 ○ 無 保
            { Kind.Trigger, NotDetected(Keeps, TriggerReason) }, // B3 ○ 無 保
            { Kind.DisabledConstraint, Detected(NotApplicable) }, // B4a ○ 既 該
            { Kind.UnvalidatedConstraint, NotDetected(Keeps, UnvalidatedConstraintReason) }, // B4b ○ 無 保
            { Kind.ConstraintClause, NotDetected(Keeps, ConstraintClauseReason) }, // B5 ○ 無 保
            { Kind.View, Detected(Keeps) }, // C1 ○ 33 保
            { Kind.MaterializedView, Detected(NotApplicable) }, // C2a ○ 33 該
            {
                Kind.MaterializedViewInternals,
                NotDetected(NotApplicable, MaterializedViewInternalsReason)
            }, // C2b ○ 無 該
            { Kind.VirtualTable, Impossible() }, // C3a ×
            { Kind.ShadowTable, Impossible() }, // C3b ×
            { Kind.ExternalTable, Detected(NotApplicable) }, // C3c ○ 33 該
            { Kind.PartitionedTable, Detected(NotApplicable) }, // C4a ○ 既 該
            { Kind.ChildPartition, Impossible() }, // C4b ×
            { Kind.TemporalHistoryTable, Impossible() }, // C5 ×
            { Kind.TableOption, NotDetected(Keeps, TableOptionReason) }, // C6 ○ 無 保
            { Kind.TemporaryTable, Detected(NotApplicable) }, // C7a ○ 33 該
            { Kind.ExtensionOwnedTable, Impossible() }, // C7b ×
            { Kind.SystemTable, Impossible() }, // C7c ×
            { Kind.ForeignKeyToExcludedTable, Detected(NotApplicable) }, // C9 ○ 36 該（名前に . を含むため外したテーブルを参照する外部キー）
            { Kind.FlattenedType, Impossible() }, // D1a ×
            { Kind.UserDefinedType, NotDetected(NotApplicable, UserDefinedTypeReason) }, // D1b ○ 無 該
            { Kind.UnparsableType, NotDetected(NotApplicable, UnparsableTypeReason) }, // D2a ○ 無 該
            { Kind.UnemittableTypeNotation, Detected(NotApplicable) }, // D2b ○ 既 該
            { Kind.PaddedName, Detected(NotApplicable) }, // N1 ○ 33 該
            { Kind.TableWithoutReadableColumns, Impossible() }, // T1 ×（自スキーマの表は見える列を必ず 1 本持つ＝全列 INVISIBLE は ORA-54039・最後の列の SET UNUSED は ORA-12983。実機で確認）
            // 向き 2
            { Kind.ComputedColumnTypeChange, NotExpressible() }, // A1b ×
            { Kind.UniqueConstraintOnPrimaryKey, NotExpressible() }, // B6 ×
            { Kind.CompositeOrMissingPrimaryKey, Expressible() }, // B7a ○
            { Kind.CompositeForeignKey, Expressible() }, // B7b ○
            { Kind.ManyToManyRelationship, Expressible() }, // B7c ○
            { Kind.NotEditableColumn, NotExpressible() }, // D3 ×
            { Kind.UnrepresentableType, PartlyExpressible() }, // D5 △
            { Kind.OnUpdateReferentialAction, NotExpressible() }, // E1 ×
            { Kind.SetDefaultReferentialAction, NotExpressible() }, // E2 ×
            { Kind.Description, Expressible() }, // E5 ○
            { Kind.SchemaQualifiedTableName, Expressible() }, // E6 ○
            { Kind.ColumnOrder, NotExpressible() }, // X1 ×
            { Kind.ForeignKeyConstraintName, Expressible() }, // X2 ○
            { Kind.CompositeForeignKeyColumnOrder, Expressible() }, // X3 ○（並び不問）
            { Kind.ForeignKeyTypeMismatch, Expressible() }, // X4 ○（長さの違いを許す）
            { Kind.ForeignKeyNotOnCandidateKey, NotExpressible() }, // X5 ×
        };

    private static Dictionary<UnsupportedDefinitionKind, DialectKindDeclaration> Sqlite() =>
        new()
        {
            // 向き 1
            { Kind.ComputedColumn, Detected(Drops) }, // A1 ○ 既 落
            { Kind.DefaultValue, Detected(Drops) }, // A2 ○ 33 落
            { Kind.CheckConstraint, NotDetected(Drops, CheckConstraintReason) }, // A3 ○ 無 落
            { Kind.Collation, NotDetected(Drops, CollationReason) }, // A4 ○ 無 落
            { Kind.IdentityRejectingValues, Impossible() }, // A5a ×
            { Kind.IdentityAcceptingValues, Detected(Drops) }, // A5b ○ 33 落
            { Kind.InvisibleColumn, Impossible() }, // A6a ×
            { Kind.ColumnOnUpdate, Impossible() }, // A6b ×
            { Kind.SpatialReferenceId, Impossible() }, // A6c ×
            { Kind.PrimaryKeyNullabilityAdjusted, Detected(NotApplicable) }, // A7 ○ 既 該
            { Kind.NonUniqueIndex, Detected(Keeps) }, // B1 ○ 既 保
            { Kind.NonConstraintUniqueIndex, Detected(Keeps) }, // B2 ○ 既 保
            { Kind.Trigger, Detected(Keeps) }, // B3 ○ 既 保
            { Kind.DisabledConstraint, Impossible() }, // B4a ×
            { Kind.UnvalidatedConstraint, Impossible() }, // B4b ×
            { Kind.ConstraintClause, NotDetected(Drops, ConstraintClauseReason) }, // B5 ○ 無 落
            { Kind.View, Detected(Keeps) }, // C1 ○ 33 保
            { Kind.MaterializedView, Impossible() }, // C2a ×
            { Kind.MaterializedViewInternals, Impossible() }, // C2b ×
            { Kind.VirtualTable, Detected(NotApplicable) }, // C3a ○ 既 該
            { Kind.ShadowTable, NotDetected(NotApplicable, ShadowTableReason) }, // C3b ○ 無 該
            { Kind.ExternalTable, Impossible() }, // C3c ×
            { Kind.PartitionedTable, Impossible() }, // C4a ×
            { Kind.ChildPartition, Impossible() }, // C4b ×
            { Kind.TemporalHistoryTable, Impossible() }, // C5 ×
            { Kind.TableOption, NotDetected(Drops, TableOptionReason) }, // C6 ○ 無 落
            { Kind.TemporaryTable, Impossible() }, // C7a ×
            { Kind.ExtensionOwnedTable, Impossible() }, // C7b ×
            { Kind.SystemTable, NotDetected(NotApplicable, SystemTableReason) }, // C7c ○ 無 該
            { Kind.ForeignKeyToExcludedTable, Detected(NotApplicable) }, // C9 ○ 既 該
            { Kind.FlattenedType, Impossible() }, // D1a ×
            { Kind.UserDefinedType, Impossible() }, // D1b ×
            { Kind.UnparsableType, NotDetected(NotApplicable, UnparsableTypeReason) }, // D2a ○ 無 該
            { Kind.UnemittableTypeNotation, Detected(NotApplicable) }, // D2b ○ 既 該
            { Kind.PaddedName, Detected(NotApplicable) }, // N1 ○ 33 該
            { Kind.TableWithoutReadableColumns, Impossible() }, // T1 ×
            // 向き 2
            { Kind.ComputedColumnTypeChange, NotExpressible() }, // A1b ×
            { Kind.UniqueConstraintOnPrimaryKey, NotExpressible() }, // B6 ×
            { Kind.CompositeOrMissingPrimaryKey, Expressible() }, // B7a ○
            { Kind.CompositeForeignKey, Expressible() }, // B7b ○
            { Kind.ManyToManyRelationship, Expressible() }, // B7c ○
            { Kind.NotEditableColumn, NotExpressible() }, // D3 ×
            { Kind.UnrepresentableType, PartlyExpressible() }, // D5 △
            { Kind.OnUpdateReferentialAction, Expressible() }, // E1 ○
            { Kind.SetDefaultReferentialAction, Expressible() }, // E2 ○
            { Kind.Description, NotExpressible() }, // E5 ×
            { Kind.SchemaQualifiedTableName, NotExpressible() }, // E6 ×
            { Kind.ColumnOrder, Expressible() }, // X1 ○（再構築）
            { Kind.ForeignKeyConstraintName, NotExpressible() }, // X2 ×
            { Kind.CompositeForeignKeyColumnOrder, Expressible() }, // X3 ○（並び不問）
            { Kind.ForeignKeyTypeMismatch, Expressible() }, // X4 ○（長さの違いを許す）
            { Kind.ForeignKeyNotOnCandidateKey, NotExpressible() }, // X5 ×
        };
}
