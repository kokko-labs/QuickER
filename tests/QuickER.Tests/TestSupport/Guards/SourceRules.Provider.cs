using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Provider;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：DB の取込・同期と DDL の生成（QuickER.Provider*）</summary>
internal static partial class SourceRules
{
    /// <summary>負にならない値を照合のキーへ埋める</summary>
    /// <remarks>整数の書式がカルチャで変わるのは負号だけ（.NET は数字を置き換えず、既定の書式は桁区切りを入れない）</remarks>
    private const string NonNegativeKey =
        "負にならない値（長さ・件数）を照合のキーへ埋める（整数の書式がカルチャで変わるのは負号だけで、キーはプロセスの外へ出ない）";

    /// <summary>SQL コメントへ埋めてよい値：enum（図の自由入力ではない）</summary>
    private const string SqlCommentEnum = "差分の種別・フェーズ＝enum（図の自由入力ではない）";

    /// <summary>取込・同期のコマンドを作る 10 ファイル（5 方言の取込と同期実行器）</summary>
    private static readonly string[] SchemaCommandFiles =
    [
        "src/QuickER.Provider.SqlServer/SqlServerSchemaImporter.cs",
        "src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs",
        "src/QuickER.Provider.PostgreSql/PostgreSqlSchemaImporter.cs",
        "src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs",
        "src/QuickER.Provider.MySql/MySqlSchemaImporter.cs",
        "src/QuickER.Provider.MySql/MySqlSchemaSyncExecutor.cs",
        "src/QuickER.Provider.Oracle/OracleSchemaImporter.cs",
        "src/QuickER.Provider.Oracle/OracleSchemaSyncExecutor.cs",
        "src/QuickER.Provider.Sqlite/SqliteSchemaImporter.cs",
        "src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs",
    ];

    /// <summary>5 方言の同期実行器（MySQL / Oracle は DDL が暗黙コミットされトランザクションを持たないが、禁止の検査には含める）</summary>
    private static readonly string[] SchemaSyncExecutorFiles =
    [
        "src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs",
        "src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs",
        "src/QuickER.Provider.MySql/MySqlSchemaSyncExecutor.cs",
        "src/QuickER.Provider.Oracle/OracleSchemaSyncExecutor.cs",
        "src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs",
    ];

    /// <summary>トランザクションを実際に持つ同期実行器</summary>
    private static readonly string[] TransactionalSyncExecutorFiles =
    [
        "src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs",
        "src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs",
        "src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs",
    ];

    /// <summary>DDL 生成・同期スクリプト生成を持つ 6 プロジェクト</summary>
    private static readonly string[] SqlCommentProjects =
    [
        "QuickER.Provider",
        "QuickER.Provider.SqlServer",
        "QuickER.Provider.PostgreSql",
        "QuickER.Provider.MySql",
        "QuickER.Provider.Oracle",
        "QuickER.Provider.Sqlite",
    ];

    /// <summary>ヘルパーを迂回するコマンド生成</summary>
    private static readonly Regex SchemaCommandBypass = new(
        @"\.CreateCommand\(\)|new\s+(?:Sql|Npgsql|MySql|Oracle|Sqlite)Command\s*\(",
        RegexOptions.Compiled
    );

    /// <summary>ヘルパーを迂回する素のロールバック（<c>RollbackQuietlyAsync</c> は名前が異なるため一致しない）</summary>
    private static readonly Regex SchemaSyncRawRollback = new(
        @"\.RollbackAsync\(",
        RegexOptions.Compiled
    );

    /// <summary>補間文字列の穴（<c>{式}</c>）。式に波括弧を含むケースは無い</summary>
    private static readonly Regex SqlCommentInterpolationHole = new(
        @"\{([^{}]+)\}",
        RegexOptions.Compiled
    );

    /// <summary>
    /// <c>--</c> で始まる文字列リテラル（先頭のインデント空白は許す）。補間文字列に限らないのは、
    /// 「素の <c>"-- …"</c> ＋ <c>$"{…}"</c> の連結」で組み立てるコメントがあるため
    /// </summary>
    private static readonly Regex SqlCommentStringLiteral = new(@"""\s*--", RegexOptions.Compiled);

    /// <summary>同期の照合の 4 ファイルで禁じる識別子</summary>
    private static readonly HashSet<string> SyncNameForbiddenIdentifiers = new(
        StringComparer.Ordinal
    )
    {
        "Trim",
        "TrimStart",
        "TrimEnd",
        "ToLower",
        "ToLowerInvariant",
        "ToUpper",
        "ToUpperInvariant",
        "OrdinalIgnoreCase",
        "InvariantCultureIgnoreCase",
        "CurrentCultureIgnoreCase",
    };

    private static IEnumerable<SourceRule> Provider() =>
        [
            // プロバイダと方言の宣言が数値・日時を文字列へ埋めるときは string.Create(CultureInfo.InvariantCulture, …) を通す
            // （DDL・同期スクリプト・型の表記にスウェーデン語の負号・タイ語の仏暦が出る）。CA1305 は素の補間と連結を診断しないので、
            // 補間の穴と文字列の連結の被演算子の型をシンボルで数える。拾う形は CultureDependentText（何かは節のテキスト）
            SourceRule.Forbid(
                "numeric-interpolation-culture",
                "数値・日時を文字列へ埋めるときは string.Create(CultureInfo.InvariantCulture, …) を通し、例外は理由つきで一覧に載せる",
                SrcProjectsWhere(
                    "DDL・同期スクリプト・型の表記を組み立てる QuickER.Provider* と QuickER.Dialects だけを見る",
                    name =>
                        name.StartsWith("QuickER.Provider", StringComparison.Ordinal)
                        || name == "QuickER.Dialects"
                ),
                GuardFinder.Symbols(CultureDependentText.Describe),
                // 型の表記を解決するための仮の列の名前（T0, T1, …）
                new GuardKnown(
                    "src/QuickER.Provider/QueryParameterTypeResolver.cs",
                    "{columnIdByToken.Count}",
                    1,
                    NonNegativeKey
                )
            ),
            // 取込・同期のコマンドは DbCommands.Create で作る（個々の生成箇所で CommandTimeout を書くと、クエリを足したときに設定が
            // 静かに漏れる）。10 ファイルの素のコマンド生成を 1 行ずつ正規表現で数える（1 行に 1 件）
            SourceRule.Forbid(
                "schema-command-bypasses",
                "コマンドは DbCommands.Create で作る（素の生成はコマンドタイムアウトの設定漏れを生む）",
                SourceScope.Files("src", SchemaCommandFiles),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindLineMatches(file, SchemaCommandBypass)
                )
            ) with
            {
                MinimumFiles = SchemaCommandFiles.Length,
            },
            // 10 ファイルが DbCommands.Create を実際に使っていること（対象の付け替えで空検査にならない）
            SourceRule.Require(
                "schema-command-helper-uses",
                "全方言の取込・同期がコマンド生成ヘルパー DbCommands.Create を通す",
                SourceScope.Files("src", SchemaCommandFiles),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindOccurrences(file, "DbCommands.Create")
                ),
                new(SchemaCommandFiles[0], "DbCommands.Create", 11, "SQL Server の取込"),
                new(SchemaCommandFiles[1], "DbCommands.Create", 1, "SQL Server の同期実行器"),
                new(SchemaCommandFiles[2], "DbCommands.Create", 8, "PostgreSQL の取込"),
                new(SchemaCommandFiles[3], "DbCommands.Create", 1, "PostgreSQL の同期実行器"),
                new(SchemaCommandFiles[4], "DbCommands.Create", 7, "MySQL の取込"),
                new(SchemaCommandFiles[5], "DbCommands.Create", 2, "MySQL の同期実行器"),
                new(SchemaCommandFiles[6], "DbCommands.Create", 11, "Oracle の取込"),
                new(SchemaCommandFiles[7], "DbCommands.Create", 1, "Oracle の同期実行器"),
                new(SchemaCommandFiles[8], "DbCommands.Create", 7, "SQLite の取込"),
                new(SchemaCommandFiles[9], "DbCommands.Create", 5, "SQLite の同期実行器")
            ) with
            {
                MinimumFiles = SchemaCommandFiles.Length,
            },
            // 同期実行器のロールバックは DbTransactions.RollbackQuietlyAsync を通す（素の RollbackAsync(ct) はキャンセルで後始末が中断し、
            // 完了済みへの Rollback が元の例外を置き換える）。5 つの同期実行器の素のロールバックを 1 行ずつ数える
            SourceRule.Forbid(
                "schema-sync-raw-rollbacks",
                "ロールバックは DbTransactions.RollbackQuietlyAsync で行う",
                SourceScope.Files("src", SchemaSyncExecutorFiles),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindLineMatches(file, SchemaSyncRawRollback)
                )
            ) with
            {
                MinimumFiles = SchemaSyncExecutorFiles.Length,
            },
            // トランザクションを持つ 3 つの同期実行器が後始末ヘルパーを実際に使っていること（対象の付け替えで空検査にならない）
            SourceRule.Require(
                "schema-sync-rollback-helper-uses",
                "トランザクションを持つ同期実行器が後始末ヘルパー DbTransactions.RollbackQuietlyAsync を通す",
                SourceScope.Files("src", TransactionalSyncExecutorFiles),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindOccurrences(file, "DbTransactions.RollbackQuietlyAsync")
                ),
                new(
                    TransactionalSyncExecutorFiles[0],
                    "DbTransactions.RollbackQuietlyAsync",
                    1,
                    "SQL Server の同期のトランザクションの後始末"
                ),
                new(
                    TransactionalSyncExecutorFiles[1],
                    "DbTransactions.RollbackQuietlyAsync",
                    1,
                    "PostgreSQL の同期のトランザクションの後始末"
                ),
                new(
                    TransactionalSyncExecutorFiles[2],
                    "DbTransactions.RollbackQuietlyAsync",
                    3,
                    "SQLite の同期のトランザクションの後始末"
                )
            ) with
            {
                MinimumFiles = TransactionalSyncExecutorFiles.Length,
            },
            // 生成 SQL の -- 行コメントへ埋める値は SqlComment.Sanitize を通す（改行入りの値が 2 行目以降を実行される SQL に変える）。
            // -- で始まる文字列リテラルを含む文（; 区切り・リテラル・コメントの中の ; は区切りにしない）の、文字列リテラルの中身の
            // 補間穴のうち SqlComment.Sanitize( を含まないものを、式の文字列で数える。例外はユーザーデータでない式だけ
            SourceRule.Forbid(
                "sql-comment-unsanitized-holes",
                "-- 行コメントへ載せる値は SqlComment.Sanitize を通し、ユーザーデータでない式は理由つきで一覧に載せる",
                SrcProjectsNamed(
                    "DDL 生成・同期スクリプト生成を持つ QuickER.Provider* の 6 プロジェクトだけを見る",
                    SqlCommentProjects
                ),
                GuardFinder.File(GuardReader.Text, FindUnsanitizedCommentHoles),
                new(
                    "src/QuickER.Provider/SyncScriptBuilderHelper.cs",
                    "item.Kind",
                    1,
                    SqlCommentEnum
                ),
                new(
                    "src/QuickER.Provider.Sqlite/SqliteSyncScriptBuilder.cs",
                    "kind",
                    1,
                    SqlCommentEnum
                ),
                new("src/QuickER.Provider/SyncScriptBuilderBase.cs", "phase", 1, SqlCommentEnum),
                new(
                    "src/QuickER.Provider/SyncScriptBuilderBase.cs",
                    "SchemaDiffKind.AlterPrimaryKey",
                    1,
                    SqlCommentEnum
                ),
                new(
                    "src/QuickER.Provider/SyncScriptBuilderBase.cs",
                    "SectionLabel(section)",
                    1,
                    SqlCommentEnum
                ),
                new(
                    "src/QuickER.Provider/SyncScriptBuilderBase.cs",
                    "section.Items.Count",
                    1,
                    "件数＝int"
                )
            ) with
            {
                // 空振りの検知：-- で始まる文字列リテラルを含む文（検査する文）が 11 以上見つかる（元の「10 件を超える」）
                Floor = new(
                    GuardFinder.File(
                        GuardReader.Text,
                        file =>
                            SplitSqlStatements(file.Text)
                                .Where(item => SqlCommentStringLiteral.IsMatch(item.Text))
                                .Select(item => new GuardFinding("文", file.LineAt(item.Start)))
                    ),
                    11,
                    "-- の行コメントを組み立てる文"
                ),
            },
            // 同期の差分と計画の照合（4 ファイル）は大文字小文字・空白を自前で畳まず SchemaNames を通す。Trim 系・ToLower / ToUpper 系・
            // 大小を無視する比較の指定が識別子として現れたら数える（コメント・文字列の中は数えない）。例外は無い
            SourceRule.Forbid(
                "sync-name-comparison-tokens",
                "同期の照合は SchemaNames（AreSame / Comparer / ToStoredForm / Key）だけで行う",
                SourceScope.Files(
                    "src",
                    "src/QuickER.Provider/SchemaDiffService.cs",
                    "src/QuickER.Provider/SyncPlanner.cs",
                    "src/QuickER.Provider/NameMatching.cs",
                    "src/QuickER.Provider/UniqueConstraintNaming.cs"
                ),
                GuardFinder.Tokens(token =>
                    token.IsKind(SyntaxKind.IdentifierToken)
                    && SyncNameForbiddenIdentifiers.Contains(token.Text)
                        ? token.Text
                        : null
                )
            ) with
            {
                MinimumFiles = 4,
            },
            // 同期の計画で「この同期で型が決まる列」は CollectTypeDecidingColumns の 1 か所が決める（NULL 許容だけの列の変更は型を
            // 決めない）。外部キーの型の照合の 3 か所がそれぞれ列の変更の新しい型を辞書へ写していたため、NULL 許容だけの変更まで
            // 「型が決まる」と数える誤りが 3 か所に写った（P5-2 の試運転の L4）。差分の項目の列の新しい型を直接読む箇所をメソッドで
            // 数え、理由つきの一覧と照合する
            SourceRule.Forbid(
                "sync-type-deciding-column-copies",
                "同期の計画で差分の項目の列の新しい型を読むのは、型が決まる列の正本（CollectTypeDecidingColumns）と理由つきの一覧だけ",
                SourceScope.Files("src", "src/QuickER.Provider/SyncPlanner.cs"),
                GuardFinder.Symbols(DiffItemNewTypeReads.Describe),
                new(
                    "src/QuickER.Provider/SyncPlanner.cs",
                    "CollectTypeDecidingColumns",
                    1,
                    "この同期で型が決まる列の正本そのもの"
                ),
                new(
                    "src/QuickER.Provider/SyncPlanner.cs",
                    "ExcludeItemsRequiringUnselected",
                    1,
                    "前提の欠けで NULL 許容化だけを外すとき、項目自身の旧と新の型（OldColumn と Column）を比べて型の変更が残るかを決める（live を引かない入口の段で、外部キーの照合ではない）"
                ),
                new(
                    "src/QuickER.Provider/SyncPlanner.cs",
                    "WarnDefaultsOnColumnTypeKindChange",
                    1,
                    "既定値のある列の型の種類（括弧より前の名前）の変更を名指しする（型が同じかでなく型の種類を比べる別の判定）"
                ),
                new(
                    "src/QuickER.Provider/SyncPlanner.cs",
                    "ApplySelectedColumnChanges",
                    1,
                    "再構築の合成で、選んだ列の変更の型を live の列へ写す（照合ではなく合成）"
                )
            ),
            // 選んだ外部キーの追加・作り直しを警告で名指しする名前は、スクリプトが付ける名前（SelectedForeignKeyName＝
            // ForeignKeyNameToAdd）の 1 か所で作る。差分の項目のリレーションから規約名を作り直すと、名前の無い同じ組の 2 本目が
            // 1 本目と同じ名前になり、警告が DB にある無関係な外部キーを名指しした（4 つの警告に写っていた）。差分の項目の名前から
            // EffectiveForeignKey で組む写しも拾う（名前が無いときの落とし先が食い違いうる。依存の除外の警告に残っていた）
            SourceRule.Forbid(
                "sync-selected-foreign-key-names",
                "同期の計画で差分の項目のリレーション・名前から外部キーの名前を作り直さず、選んだ外部キーの名前は SelectedForeignKeyName を通す",
                SourceScope.Files("src", "src/QuickER.Provider/SyncPlanner.cs"),
                GuardFinder.Symbols(DiffItemForeignKeyNameRebuilds.Describe),
                new GuardKnown(
                    "src/QuickER.Provider/SyncPlanner.cs",
                    "SynthesizeForeignKeys",
                    1,
                    "再構築の合成で、外す外部キーの項目（削除の項目のリレーションは live のもの）を live の外部キーと名前で突き合わせる（警告の名指しではない）"
                )
            ),
            // DB の取込・DDL・差分・同期の計画は、列が NULL を持てるかを ColumnNullability.AllowsNull で読み、NULL 許容の印
            // （Column.IsNullable）を直接読まない。図は主キーの列に印を持てる（図のファイルの手書き）ので、印をそのまま読むと
            // DDL が NOT NULL で作る列を NULL 許容とみなし、同じ実効の図から違う計画・注記を作る。
            // 取込は主キーの列の印を false にそろえるので、live の列でも AllowsNull と印は同じ値になり、図の列と見分けずに禁じられる。
            // 書き込み（代入・初期化子の左辺）は数えない。取込のファイルに読み取りは無い（印を DB のカタログから書くだけ）ので除外しない
            SourceRule.Forbid(
                "provider-column-nullability-raw-reads",
                "QuickER.Provider* は列の NULL 許容の印を直接読まず、ColumnNullability.AllowsNull を通す",
                Src.Within("QuickER.Provider"),
                GuardFinder.Symbols(ColumnNullableRawReads.Describe),
                new GuardKnown(
                    "src/QuickER.Provider/SchemaSignature.cs",
                    "Compute",
                    1,
                    "図の構造の指紋に保存の形の印そのものを記録する（印が変われば置き換えの確認を出す。実効値の判定ではない）"
                ),
                new GuardKnown(
                    "src/QuickER.Provider/IdentityColumnComment.cs",
                    "Build",
                    1,
                    "主キーでない NOT NULL の採番列の判定（主キーでない列では印がそのまま実効値。規則 column-nullability-copies の既知と同じ）"
                ),
                new GuardKnown(
                    "src/QuickER.Provider/DiagramTypeConverter.cs",
                    "CreatePlan",
                    1,
                    "方言切替で行バージョンを失う主キーでない NOT NULL の列の判定（主キーでない列では印がそのまま実効値。規則 column-nullability-copies の既知と同じ）"
                )
            ) with
            {
                // 範囲が 6 つのプロジェクトを読んでいること（いまは 118 ファイル。本体の QuickER.Provider だけなら 54）
                MinimumFiles = 100,
            },
            // 取込警告の文言は SchemaImportWarningText.Describe（QuickER.Provider）が持ち、表示側は種別で分岐しない。種別で分岐する形
            // （switch のアームの定数パターン・is のパターン・case ラベル・== / != の比較）のうち SchemaImportWarningKind. を含むものを
            // 数える（種別を作る式＝方言のインポータの「"view" => ViewExcluded」の右辺は分岐ではない）
            SourceRule.Forbid(
                "schema-import-warning-kind-branches",
                "取込警告の文言は SchemaImportWarningText.Describe が持ち、表示側は種別で分岐しない",
                Src.Except(
                    "QuickER.Provider/",
                    "取込警告の文言の正本（SchemaImportWarningText）を持つプロジェクト"
                ),
                GuardFinder.Nodes(node =>
                    (
                        node is CaseSwitchLabelSyntax or ConstantPatternSyntax
                        || node.IsKind(SyntaxKind.EqualsExpression)
                        || node.IsKind(SyntaxKind.NotEqualsExpression)
                    )
                    && node.ToString()
                        .Contains("SchemaImportWarningKind.", StringComparison.Ordinal)
                        ? node.ToString().Split('\n')[0].Trim()
                        : null
                )
            ),
            // QuickER 自身が SQLite のファイルを開く接続は SqliteFileConnections.Create だけが作る（プールしない）。既定のプールつきの
            // 接続は閉じた後もファイルを開いたままにし、新規作成・取込・同期の直後に利用者がファイルを移動・削除できない。
            // 拾う形は SqliteConnection 型の生成（new SqliteConnection(…)・型を書かない new(…)）をシンボルで数える
            SourceRule.Forbid(
                "sqlite-file-connection-creations",
                "SQLite のファイルを開く接続は SqliteFileConnections.Create で作る（既定のプールつきの接続は閉じた後もファイルを掴む）",
                Src.Except(
                    "QuickER.Runtime.Sqlite/",
                    "生成コードのランタイムの接続は利用者のアプリの接続で、プールは利用者の接続文字列が決める"
                ),
                GuardFinder.Symbols(DescribeSqliteConnectionCreation),
                new GuardKnown(
                    SqliteFileConnectionsFile,
                    "new SqliteConnection",
                    1,
                    "唯一の作り手（Pooling=False を立てる）"
                )
            ) with
            {
                Witness = new(
                    SourceScope.Under("src/QuickER.Runtime.Sqlite"),
                    [
                        new(
                            "src/QuickER.Runtime.Sqlite/Runtime.Sqlite.g.cs",
                            "new SqliteConnection",
                            1,
                            "生成コードの接続ファクトリ（範囲の外の実在の生成を拾えること）"
                        ),
                    ]
                ),
            },
            // その唯一の作り手が、接続文字列のビルダーで Pooling を false にしていること（作り手を残したままプールへ戻すのを止める）
            SourceRule.Require(
                "sqlite-file-connection-pooling-off",
                "SqliteFileConnections.Create は接続文字列のビルダーで Pooling = false を立てる",
                SourceScope.Files("src", SqliteFileConnectionsFile),
                GuardFinder.Symbols(DescribeSqlitePoolingOff),
                new GuardKnown(
                    SqliteFileConnectionsFile,
                    "Pooling = false",
                    1,
                    "閉じた接続がファイルを閉じる（プールに残らない）"
                )
            ),
            // QuickER 自身が SQLite で結果を読まない文（DDL・PRAGMA）を実行するのは SqliteScripts.RunToEndAsync だけ（結果を最後まで読み切る）。Microsoft.Data.Sqlite の
            // ExecuteNonQuery は、行の無い表への ADD COLUMN … NOT NULL の後ろの文が断られると例外を出さずに後ろを飛ばし、同期の実行器が
            // 部分適用をコミットする。スクリプトが複数の文かは静的に見分けられないので、SQLite の接続を作り・使うプロジェクトでは
            // ExecuteNonQuery(Async) の呼び出しそのものを断る（1 文でも部品を通す）。拾う形は DbCommand・IDbCommand の
            // ExecuteNonQuery / ExecuteNonQueryAsync の呼び出しをシンボルで数える
            SourceRule.Forbid(
                "sqlite-script-executions",
                "SQLite で結果を読まない文（DDL・PRAGMA）を実行するのは SqliteScripts.RunToEndAsync（ExecuteNonQuery は断られた文の後ろを黙って飛ばすことがある）",
                SrcProjectsNamed(
                    "QuickER 自身の SQLite の接続は QuickER.Provider.Sqlite が作り（規則 sqlite-file-connection-creations）、Db.UI・Cli・Gui が使う。ほかの方言のプロジェクトの ExecuteNonQuery は範囲の外",
                    ["QuickER.Provider.Sqlite", "QuickER.Db.UI", "QuickER.Cli", "QuickER.Gui"]
                ),
                GuardFinder.Symbols(DescribeExecuteNonQuery)
            ) with
            {
                Witness = new(
                    SourceScope.Under("src/QuickER.Provider.MySql"),
                    [
                        new(
                            "src/QuickER.Provider.MySql/MySqlSchemaSyncExecutor.cs",
                            "ExecuteNonQuery",
                            2,
                            "MySQL の同期の実行器（範囲の外の実在の呼び出しを拾えること）"
                        ),
                    ]
                ),
            },
            // DDL・同期の名前の入口（SqlNameText）が名前を集める names.Add の 1 本ずつは、方言の DDL の表の名前の出どころの宣言
            // （DialectDdlRows.NameSources）と 1 対 1 に対応する。宣言が正本で、あるべき出現はそこから作る（写しを持たない）。
            // 足したのに宣言へ載せ忘れる（その出どころの行が無い）・消したのに宣言に残る（行が空振りする）の両方向を赤にする
            SourceRule.Require(
                "sql-name-text-name-sources",
                "SqlNameText の names.Add の 1 本ずつ（所属のメソッド・出どころの式・表の位置か・置き場所）が、方言の DDL の表の名前の出どころの宣言と一致する",
                SourceScope.Files("src", DialectDdlRows.NameSourceFile),
                GuardFinder.Nodes(DescribeNameSourceAdd),
                [
                    .. DialectDdlRows
                        .NameSources.GroupBy(source => source.What, StringComparer.Ordinal)
                        .Select(group => new GuardKnown(
                            DialectDdlRows.NameSourceFile,
                            group.Key,
                            group.Count(),
                            "方言の DDL の表が、この出どころだけへ表せない名前を差し込んだ行で入口が断ることを確かめる（経路 "
                                + string.Join(
                                    "・",
                                    group
                                        .SelectMany(source => source.Routes)
                                        .Select(route => route.Id)
                                )
                                + "）"
                        )),
                ]
            ),
        ];

    /// <summary>名前の入口の置き場所の関数と、例外文の置き場所の表記</summary>
    private static readonly IReadOnlyDictionary<string, string> NameSourceLocations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TableLocation"] = "TABLE",
            ["ColumnLocation"] = "COLUMN",
            ["UniqueLocation"] = "UNIQUE",
            ["ForeignKeyLocation"] = "FOREIGN KEY",
        };

    /// <summary>
    /// 節が <c>names.Add(...)</c> の呼び出しなら <c>{メソッド}: names.Add({式}, {表の位置か}, {置き場所})</c>
    /// （<see cref="DdlNameSource.What"/> と同じ表記。形が違えば節のテキストを添えて、宣言に無い出現にする）
    /// </summary>
    private static string? DescribeNameSourceAdd(SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "Add",
                    Expression: IdentifierNameSyntax { Identifier.Text: "names" },
                },
            } invocation
        )
        {
            return null;
        }

        var method =
            invocation
                .Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault()
                ?.Identifier.Text
            ?? "?";

        if (
            invocation.ArgumentList.Arguments
                is not [{ Expression: BaseObjectCreationExpressionSyntax creation }]
            || creation.ArgumentList?.Arguments is not [var name, var isTable, var locate]
        )
        {
            return $"{method}: 形の違う names.Add {invocation}";
        }

        var locateFunction = locate
            .Expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => identifier.Identifier.Text)
            .FirstOrDefault(NameSourceLocations.ContainsKey);
        var location = locateFunction is null
            ? locate.Expression.ToString()
            : NameSourceLocations[locateFunction];

        return $"{method}: names.Add({name.Expression}, {isTable.Expression}, {location})";
    }

    /// <summary>SQLite のファイルを開く接続の唯一の作り手</summary>
    private const string SqliteFileConnectionsFile =
        "src/QuickER.Provider.Sqlite/SqliteFileConnections.cs";

    /// <summary>節が <c>Microsoft.Data.Sqlite.SqliteConnection</c> の生成なら <c>new SqliteConnection</c></summary>
    private static string? DescribeSqliteConnectionCreation(SemanticModel model, SyntaxNode node) =>
        node is BaseObjectCreationExpressionSyntax creation
        && model.GetTypeInfo(creation).Type?.ToDisplayString()
            == "Microsoft.Data.Sqlite.SqliteConnection"
            ? "new SqliteConnection"
            : null;

    /// <summary>
    /// 節が ADO.NET のコマンド（<c>System.Data.Common.DbCommand</c> の派生か <c>System.Data.IDbCommand</c>）の
    /// <c>ExecuteNonQuery</c> / <c>ExecuteNonQueryAsync</c> の呼び出しなら <c>ExecuteNonQuery</c>
    /// </summary>
    private static string? DescribeExecuteNonQuery(SemanticModel model, SyntaxNode node)
    {
        if (
            node is not InvocationExpressionSyntax invocation
            || model.GetSymbolInfo(invocation).Symbol
                is not IMethodSymbol { Name: "ExecuteNonQuery" or "ExecuteNonQueryAsync" } method
        )
        {
            return null;
        }

        for (var type = method.ContainingType; type is not null; type = type.BaseType)
        {
            if (
                type.ToDisplayString() is "System.Data.Common.DbCommand" or "System.Data.IDbCommand"
            )
            {
                return "ExecuteNonQuery";
            }
        }

        return method.ContainingType.AllInterfaces.Any(i =>
            i.ToDisplayString() == "System.Data.IDbCommand"
        )
            ? "ExecuteNonQuery"
            : null;
    }

    /// <summary>
    /// 節が <c>SqliteConnectionStringBuilder.Pooling</c> へ定数の <see langword="false"/> を代入する式なら <c>Pooling = false</c>
    /// </summary>
    private static string? DescribeSqlitePoolingOff(SemanticModel model, SyntaxNode node) =>
        node is AssignmentExpressionSyntax assignment
        && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && model.GetSymbolInfo(assignment.Left).Symbol
            is IPropertySymbol { Name: "Pooling" } property
        && property.ContainingType.ToDisplayString()
            == "Microsoft.Data.Sqlite.SqliteConnectionStringBuilder"
        && model.GetConstantValue(assignment.Right) is { HasValue: true, Value: false }
            ? "Pooling = false"
            : null;

    /// <summary>src のうち、名前を挙げたプロジェクトだけ（挙げたプロジェクトのフォルダが無ければ例外＝範囲の書き間違い）</summary>
    /// <param name="reason">ほかのプロジェクトを除く理由（範囲の形）</param>
    /// <param name="names">プロジェクトのフォルダの名前</param>
    private static SourceScope SrcProjectsNamed(string reason, IReadOnlyList<string> names)
    {
        var missing = names
            .Where(name =>
                !System.IO.Directory.Exists(
                    System.IO.Path.Combine(SourceFiles.RepositoryRoot, "src", name)
                )
            )
            .ToList();

        return missing.Count > 0
            ? throw new InvalidOperationException(
                $"範囲に挙げたプロジェクトが無い: {string.Join(", ", missing)}"
            )
            : SrcProjectsWhere(reason, name => names.Contains(name, StringComparer.Ordinal));
    }

    /// <summary>1 行ずつ正規表現を当て、一致した行ごとに 1 件（何かは最初に一致した表記の空白を詰めたもの）</summary>
    private static IEnumerable<GuardFinding> FindLineMatches(GuardFile file, Regex pattern)
    {
        var lines = file.Text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (pattern.Match(lines[i]) is { Success: true } match)
            {
                yield return new GuardFinding(Regex.Replace(match.Value, @"\s+", " "), i + 1);
            }
        }
    }

    /// <summary>本文の部分文字列の出現（重ならない数え方。何かはその文字列）</summary>
    private static IEnumerable<GuardFinding> FindOccurrences(GuardFile file, string value)
    {
        for (
            var index = file.Text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = file.Text.IndexOf(value, index + value.Length, StringComparison.Ordinal)
        )
        {
            yield return new GuardFinding(value, file.LineAt(index));
        }
    }

    /// <summary>-- コメントを組み立てる文の、SqlComment.Sanitize を通さない補間穴（何かは穴の式）</summary>
    private static IEnumerable<GuardFinding> FindUnsanitizedCommentHoles(GuardFile file)
    {
        foreach (var (statement, start) in SplitSqlStatements(file.Text))
        {
            if (!SqlCommentStringLiteral.IsMatch(statement))
            {
                continue;
            }

            var line = file.LineAt(FirstCodeIndex(file.Text, start));

            foreach (
                Match hole in SqlCommentInterpolationHole.Matches(StringLiteralBodies(statement))
            )
            {
                var expression = hole.Groups[1].Value.Trim();

                if (!expression.Contains("SqlComment.Sanitize(", StringComparison.Ordinal))
                {
                    yield return new GuardFinding(expression, line);
                }
            }
        }
    }

    /// <summary>位置から空白と行コメントを飛ばした、文の最初のコードの位置（報告の行に使う）</summary>
    private static int FirstCodeIndex(string text, int start)
    {
        var index = start;

        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
            }
            else if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                var end = text.IndexOf('\n', index);
                index = end < 0 ? text.Length : end;
            }
            else
            {
                break;
            }
        }

        return index;
    }

    /// <summary>
    /// 文に含まれる文字列リテラルの中身だけを連結して返す（補間穴の抽出対象を文字列の内側に限るため）
    /// </summary>
    /// <remarks>
    /// 文はコードブロックの <c>{</c> <c>}</c> をまたぐので、文そのものから補間穴を探すと C# の波括弧を
    /// 穴と誤認する。リテラルの中身だけを取り出してから数える。
    /// </remarks>
    private static string StringLiteralBodies(string statement)
    {
        var bodies = new StringBuilder();
        var inString = false;
        var inVerbatim = false;

        for (var i = 0; i < statement.Length; i++)
        {
            var ch = statement[i];

            if (!inString)
            {
                if (ch == '@' && i + 1 < statement.Length && statement[i + 1] == '"')
                {
                    inString = true;
                    inVerbatim = true;
                    i++;
                }
                else if (ch == '"')
                {
                    inString = true;
                    inVerbatim = false;
                }

                continue;
            }

            if (!inVerbatim && ch == '\\' && i + 1 < statement.Length)
            {
                i++;
                continue;
            }

            if (inVerbatim && ch == '"' && i + 1 < statement.Length && statement[i + 1] == '"')
            {
                i++;
                continue;
            }

            if (ch == '"')
            {
                inString = false;
                bodies.Append('\n');
                continue;
            }

            bodies.Append(ch);
        }

        return bodies.ToString();
    }

    /// <summary>
    /// ソースを文（<c>;</c> 区切り）へ分割する（文の本文と、文の始まりの位置）。文字列リテラル・文字リテラル・コメントの中の
    /// <c>;</c> は区切りにしない
    /// </summary>
    /// <remarks>
    /// 補間文字列は複数行へ折り返され、続きの行にも補間穴が現れる（<c>+ $"{Sanitize(x)} -&gt; "</c>）。
    /// 行単位で見ると続きの行を取りこぼすため、文単位で束ねてから穴を数える。
    /// </remarks>
    private static IEnumerable<(string Text, int Start)> SplitSqlStatements(string text)
    {
        var statement = new StringBuilder();
        var start = 0;
        var inString = false;
        var inVerbatimString = false;
        var inChar = false;
        var inLineComment = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inLineComment)
            {
                // コメント本文は検査対象に含めない（説明文の {…} を補間穴と誤認しないため）
                if (ch is '\n')
                {
                    inLineComment = false;
                    statement.Append(ch);
                }

                continue;
            }

            if (inVerbatimString)
            {
                // 逐語的文字列では \ はただの文字で、"" が " のエスケープになる
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inVerbatimString = false;
                }

                statement.Append(ch);
                continue;
            }

            if (inString)
            {
                if (ch == '\\' && i + 1 < text.Length)
                {
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                statement.Append(ch);
                continue;
            }

            if (inChar)
            {
                if (ch == '\\' && i + 1 < text.Length)
                {
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                }

                if (ch == '\'')
                {
                    inChar = false;
                }

                statement.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '@' when i + 1 < text.Length && text[i + 1] == '"':
                    inVerbatimString = true;
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                case '"':
                    inString = true;
                    break;
                case '\'':
                    inChar = true;
                    break;
                case '/' when i + 1 < text.Length && text[i + 1] == '/':
                    inLineComment = true;
                    break;
                case ';':
                    yield return (statement.ToString(), start);
                    statement.Clear();
                    start = i + 1;
                    continue;
            }

            statement.Append(ch);
        }

        if (statement.Length > 0)
        {
            yield return (statement.ToString(), start);
        }
    }
}
