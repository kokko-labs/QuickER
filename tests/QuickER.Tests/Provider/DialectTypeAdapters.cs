using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>取込が DB のカタログから型の表記を組むときの入力（方言ごとに使う項目だけを読む）</summary>
/// <param name="Name">型の名前（PostgreSQL は <c>format_type</c> の出力・MySQL は <c>COLUMN_TYPE</c>）</param>
/// <param name="Length">長さ（SQL Server の <c>max_length</c>・Oracle の <c>CHAR_LENGTH</c>）</param>
/// <param name="Precision">精度</param>
/// <param name="Scale">スケール</param>
/// <param name="DateTimePrecision">小数秒の桁（SQL Server）</param>
/// <param name="DataLength">バイトの長さ（Oracle の <c>DATA_LENGTH</c>）</param>
/// <param name="CharUsed">長さの単位（Oracle の <c>CHAR_USED</c>）</param>
internal sealed record ImportedType(
    string Name,
    int? Length = null,
    int? Precision = null,
    int? Scale = null,
    int? DateTimePrecision = null,
    int? DataLength = null,
    string? CharUsed = null
);

/// <summary>方言の型の表の、方言ごとのアダプタ（方言による違いはここの抽象メンバーだけが持つ）</summary>
/// <remarks>
/// 表の行と実行部は方言を名指しして分岐しない。方言を足したら、アダプタを書くまで <see cref="DialectTypeAxis.Axis"/> の構築が赤になる。
/// </remarks>
internal abstract class DialectTypeAdapter : IMatrixAdapter
{
    /// <summary>受け持つ方言（<see cref="DialectNames"/> の値）</summary>
    public abstract string AxisValue { get; }

    /// <summary>方言のプロバイダ（型カタログ・マッパー・同期の能力はこのインスタンスのものを使う）</summary>
    public abstract IDatabaseProvider Provider { get; }

    /// <summary>型カタログ（<see cref="IDatabaseProvider.TypeCatalog"/>）</summary>
    public ITypeCatalog Catalog => Provider.TypeCatalog;

    /// <summary>型の表記を C# の型の情報へ写す（方言のマッパーの <c>Map</c>）</summary>
    /// <param name="dataType">型の表記</param>
    public abstract CSharpTypeInfo Map(string dataType);

    /// <summary>
    /// 型の表記を取込の表記へ寄せる（方言の <c>TypeNotation.Normalize</c>。SQLite は宣言をそのまま返すので恒等）
    /// </summary>
    /// <param name="dataType">型の表記</param>
    public abstract string Normalize(string dataType);

    /// <summary>取込が DB のカタログから型の表記を組む関数</summary>
    /// <param name="imported">DB のカタログの値</param>
    public abstract string ImportedSpelling(ImportedType imported);

    /// <summary>
    /// その表記で宣言した列について DB のカタログが返す値（取込の入力）を組む（方言ごとの表記の分解。宣言のセルの表記から
    /// 取込の表記の行を作るのに使う）
    /// </summary>
    /// <param name="nativeType">宣言の型の表記（括弧の引数は数字か <c>max</c>）</param>
    public abstract ImportedType CatalogValueOf(string nativeType);

    /// <summary>型の表記を名前・括弧の引数・括弧の後ろの語へ分ける（括弧が無ければ引数は空）</summary>
    /// <param name="nativeType">型の表記</param>
    internal static (string Name, IReadOnlyList<string> Arguments, string Suffix) SplitType(
        string nativeType
    )
    {
        var open = nativeType.IndexOf('(', StringComparison.Ordinal);

        if (open < 0)
        {
            return (nativeType.Trim(), [], string.Empty);
        }

        var close = nativeType.IndexOf(')', open);
        var arguments = nativeType[(open + 1)..close]
            .Split(',')
            .Select(argument => argument.Trim())
            .ToList();

        return (nativeType[..open].Trim(), arguments, nativeType[(close + 1)..].Trim());
    }

    /// <summary>括弧の引数を数で読む（<c>max</c> は -1）</summary>
    internal static int Argument(string text) =>
        text.Equals("max", StringComparison.OrdinalIgnoreCase)
            ? -1
            : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 書き出しに上限（長さ・精度・スケール・小数秒）がある正規型（上限の「ちょうど」と「1 つ超え」の行を必須にする）
    /// </summary>
    /// <remarks>上限の値そのものは表の行が持つ。ここに無い種類は、その方言では上限の境界が原理的に無い。</remarks>
    public abstract IReadOnlySet<CanonicalTypeKind> WriteLimitedKinds { get; }

    /// <summary>
    /// 型カタログの解析（<see cref="ITypeCatalog.TryParse"/>）が返し得る正規型の種類（表記を入力にする観点で、ここに無い種類は
    /// その方言では原理的に現れない）
    /// </summary>
    /// <remarks>宣言は <c>DialectTypeTableTests.ParsedKinds_MatchCatalog</c> が型の選択肢・書き出しの代表値・表の入力の解析と照合する。</remarks>
    public abstract IReadOnlySet<CanonicalTypeKind> ParsedKinds { get; }

    /// <summary>結線の期待：表示名</summary>
    public abstract string ExpectedDisplayName { get; }

    /// <summary>結線の期待：既定のポート</summary>
    public abstract int? ExpectedDefaultPort { get; }

    /// <summary>結線の期待：型の選択肢（<see cref="ITypeCatalog.DataTypes"/> と同じインスタンス）</summary>
    public abstract IReadOnlyList<string> ExpectedDataTypes { get; }

    /// <summary>結線の期待：部品の型（取込・マッパー・型カタログ・同期スクリプト・同期の実行器・DDL）</summary>
    public abstract IReadOnlyList<Type> ExpectedComponentTypes { get; }

    /// <summary>プロバイダの部品（<see cref="ExpectedComponentTypes"/> と同じ並び）</summary>
    public IReadOnlyList<object> Components =>
        [
            Provider.SchemaImporter,
            Provider.TypeMapper,
            Provider.TypeCatalog,
            Provider.SyncScriptBuilder,
            Provider.SyncExecutor,
            Provider.DdlGenerator,
        ];

    /// <inheritdoc />
    public override string ToString() => AxisValue;
}

/// <summary>方言の型の表の行列の軸（正本は <see cref="DialectNames.All"/>）</summary>
internal static class DialectTypeAxis
{
    /// <summary>方言を持たない観点（型トークン・型の表記の安全性）の方言の水準</summary>
    public const string None = "-";

    /// <summary>軸</summary>
    public static MatrixAxis<DialectTypeAdapter> Axis { get; } =
        new(
            "DialectTypes",
            DialectNames.All,
            [
                new SqlServerTypeAdapter(),
                new PostgreSqlTypeAdapter(),
                new MySqlTypeAdapter(),
                new OracleTypeAdapter(),
                new SqliteTypeAdapter(),
            ]
        );

    /// <summary>方言のアダプタを引く</summary>
    /// <param name="dialect">方言</param>
    public static DialectTypeAdapter Get(string dialect) => Axis.Get(dialect);

    /// <summary>長さを要する種類＋ほかの種類（アダプタの宣言を短く書く。軸より先に初期化されるよう式で書く）</summary>
    /// <param name="except">長さの種類のうち上限を持たないもの</param>
    /// <param name="others">長さ以外で上限を持つ種類</param>
    internal static IReadOnlySet<CanonicalTypeKind> Limited(
        IEnumerable<CanonicalTypeKind> except,
        params CanonicalTypeKind[] others
    ) =>
        Enum.GetValues<CanonicalTypeKind>()
            .Where(CanonicalTypeToken.HasLengthArgument)
            .Except(except)
            .Concat(others)
            .ToHashSet();

    private sealed class SqlServerTypeAdapter : DialectTypeAdapter
    {
        private readonly SqlServerCSharpTypeMapper _mapper = new();

        public override string AxisValue => DialectNames.SqlServer;

        public override IDatabaseProvider Provider { get; } = new SqlServerProvider();

        public override CSharpTypeInfo Map(string dataType) => _mapper.Map(dataType);

        public override string Normalize(string dataType) =>
            SqlServerTypeNotation.Normalize(dataType);

        public override string ImportedSpelling(ImportedType imported) =>
            SqlServerSchemaImporter.FormatDataType(
                imported.Name,
                imported.Length,
                imported.Precision,
                imported.Scale,
                imported.DateTimePrecision
            );

        // INFORMATION_SCHEMA.COLUMNS の DATA_TYPE と長さ・精度・小数秒（SqlServerSchemaImporter が読む列）。
        // rowversion の列は DATA_TYPE が timestamp で返る（docs/database.md の same-type-spellings の表）
        public override ImportedType CatalogValueOf(string nativeType)
        {
            var (name, arguments, _) = SplitType(nativeType);
            name = name.ToLowerInvariant();

            return name switch
            {
                "rowversion" => new ImportedType("timestamp"),
                "char"
                or "varchar"
                or "nchar"
                or "nvarchar"
                or "binary"
                or "varbinary" when arguments.Count == 1 => new ImportedType(
                    name,
                    Length: Argument(arguments[0])
                ),
                "decimal" or "numeric" when arguments.Count > 0 => new ImportedType(
                    name,
                    Precision: Argument(arguments[0]),
                    Scale: arguments.Count > 1 ? Argument(arguments[1]) : 0
                ),
                "datetime2" or "time" or "datetimeoffset" when arguments.Count == 1 =>
                    new ImportedType(name, DateTimePrecision: Argument(arguments[0])),
                _ => new ImportedType(name),
            };
        }

        public override IReadOnlySet<CanonicalTypeKind> WriteLimitedKinds { get; } =
            Limited(
                [],
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.Time,
                CanonicalTypeKind.DateTimeOffset
            );

        // 解析が返さない種類：json 型が無い（json は nvarchar(max) で書き、文字列として読む）
        public override IReadOnlySet<CanonicalTypeKind> ParsedKinds { get; } =
            new HashSet<CanonicalTypeKind>
            {
                CanonicalTypeKind.Boolean,
                CanonicalTypeKind.TinyInt,
                CanonicalTypeKind.SmallInt,
                CanonicalTypeKind.Int32,
                CanonicalTypeKind.Int64,
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.Float32,
                CanonicalTypeKind.Float64,
                CanonicalTypeKind.Money,
                CanonicalTypeKind.String,
                CanonicalTypeKind.AnsiString,
                CanonicalTypeKind.FixedString,
                CanonicalTypeKind.AnsiFixedString,
                CanonicalTypeKind.Binary,
                CanonicalTypeKind.FixedBinary,
                CanonicalTypeKind.Date,
                CanonicalTypeKind.Time,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.DateTimeOffset,
                CanonicalTypeKind.Guid,
                CanonicalTypeKind.Xml,
                CanonicalTypeKind.RowVersion,
            };

        public override string ExpectedDisplayName => "SQL Server";

        public override int? ExpectedDefaultPort => null;

        public override IReadOnlyList<string> ExpectedDataTypes => SqlServerDataTypes.All;

        public override IReadOnlyList<Type> ExpectedComponentTypes =>
            [
                typeof(SqlServerSchemaImporter),
                typeof(SqlServerCSharpTypeMapper),
                typeof(SqlServerTypeCatalog),
                typeof(SqlServerSyncScriptBuilder),
                typeof(SqlServerSchemaSyncExecutor),
                typeof(SqlServerDdlGenerator),
            ];
    }

    private sealed class PostgreSqlTypeAdapter : DialectTypeAdapter
    {
        private readonly PostgreSqlCSharpTypeMapper _mapper = new();

        public override string AxisValue => DialectNames.PostgreSql;

        public override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

        public override CSharpTypeInfo Map(string dataType) => _mapper.Map(dataType);

        public override string Normalize(string dataType) =>
            PostgreSqlTypeNotation.Normalize(dataType);

        public override string ImportedSpelling(ImportedType imported) =>
            PostgreSqlSchemaImporter.NormalizeFormatType(imported.Name);

        // format_type() の正準表記（長い形。PostgreSqlTypeNotation の XmlDoc の規則の左辺）
        public override ImportedType CatalogValueOf(string nativeType)
        {
            var (name, arguments, suffix) = SplitType(nativeType);
            var parenthesized = arguments.Count == 0 ? "" : $"({string.Join(",", arguments)})";

            return new ImportedType(
                suffix.Length > 0
                    ? nativeType
                    : name.ToLowerInvariant() switch
                    {
                        "varchar" => $"character varying{parenthesized}",
                        "char" => $"character{parenthesized}",
                        "timestamp" => $"timestamp{parenthesized} without time zone",
                        "timestamptz" => $"timestamp{parenthesized} with time zone",
                        "time" => $"time{parenthesized} without time zone",
                        _ => nativeType,
                    }
            );
        }

        // bytea は長さを持たないので Binary / FixedBinary に上限は無い
        public override IReadOnlySet<CanonicalTypeKind> WriteLimitedKinds { get; } =
            Limited(
                [CanonicalTypeKind.Binary, CanonicalTypeKind.FixedBinary],
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.Time,
                CanonicalTypeKind.DateTimeOffset
            );

        // 解析が返さない種類：tinyint・Ansi の文字列・固定長のバイナリ・行バージョンの語彙が無い
        public override IReadOnlySet<CanonicalTypeKind> ParsedKinds { get; } =
            new HashSet<CanonicalTypeKind>
            {
                CanonicalTypeKind.Boolean,
                CanonicalTypeKind.SmallInt,
                CanonicalTypeKind.Int32,
                CanonicalTypeKind.Int64,
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.Float32,
                CanonicalTypeKind.Float64,
                CanonicalTypeKind.Money,
                CanonicalTypeKind.String,
                CanonicalTypeKind.FixedString,
                CanonicalTypeKind.Binary,
                CanonicalTypeKind.Date,
                CanonicalTypeKind.Time,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.DateTimeOffset,
                CanonicalTypeKind.Guid,
                CanonicalTypeKind.Xml,
                CanonicalTypeKind.Json,
            };

        public override string ExpectedDisplayName => "PostgreSQL";

        public override int? ExpectedDefaultPort => 5432;

        public override IReadOnlyList<string> ExpectedDataTypes => PostgreSqlDataTypes.All;

        public override IReadOnlyList<Type> ExpectedComponentTypes =>
            [
                typeof(PostgreSqlSchemaImporter),
                typeof(PostgreSqlCSharpTypeMapper),
                typeof(PostgreSqlTypeCatalog),
                typeof(PostgreSqlSyncScriptBuilder),
                typeof(PostgreSqlSchemaSyncExecutor),
                typeof(PostgreSqlDdlGenerator),
            ];
    }

    private sealed class MySqlTypeAdapter : DialectTypeAdapter
    {
        private readonly MySqlCSharpTypeMapper _mapper = new();

        public override string AxisValue => DialectNames.MySql;

        public override IDatabaseProvider Provider { get; } = new MySqlProvider();

        public override CSharpTypeInfo Map(string dataType) => _mapper.Map(dataType);

        public override string Normalize(string dataType) => MySqlTypeNotation.Normalize(dataType);

        // MySQL の取込は COLUMN_TYPE をそのまま正規化へ通す
        public override string ImportedSpelling(ImportedType imported) =>
            MySqlTypeNotation.Normalize(imported.Name);

        // COLUMN_TYPE は宣言の表記そのもの（引数を含む）
        public override ImportedType CatalogValueOf(string nativeType) => new(nativeType);

        public override IReadOnlySet<CanonicalTypeKind> WriteLimitedKinds { get; } =
            Limited(
                [],
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.Time,
                CanonicalTypeKind.DateTimeOffset
            );

        // 解析が返さない種類：通貨・Ansi の文字列・UUID・XML・行バージョンの語彙が無い（utf8mb4 前提）
        public override IReadOnlySet<CanonicalTypeKind> ParsedKinds { get; } =
            new HashSet<CanonicalTypeKind>
            {
                CanonicalTypeKind.Boolean,
                CanonicalTypeKind.TinyInt,
                CanonicalTypeKind.SmallInt,
                CanonicalTypeKind.Int32,
                CanonicalTypeKind.Int64,
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.Float32,
                CanonicalTypeKind.Float64,
                CanonicalTypeKind.String,
                CanonicalTypeKind.FixedString,
                CanonicalTypeKind.Binary,
                CanonicalTypeKind.FixedBinary,
                CanonicalTypeKind.Date,
                CanonicalTypeKind.Time,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.DateTimeOffset,
                CanonicalTypeKind.Json,
            };

        public override string ExpectedDisplayName => "MySQL";

        public override int? ExpectedDefaultPort => 3306;

        public override IReadOnlyList<string> ExpectedDataTypes => MySqlDataTypes.All;

        public override IReadOnlyList<Type> ExpectedComponentTypes =>
            [
                typeof(MySqlSchemaImporter),
                typeof(MySqlCSharpTypeMapper),
                typeof(MySqlTypeCatalog),
                typeof(MySqlSyncScriptBuilder),
                typeof(MySqlSchemaSyncExecutor),
                typeof(MySqlDdlGenerator),
            ];
    }

    private sealed class OracleTypeAdapter : DialectTypeAdapter
    {
        private readonly OracleCSharpTypeMapper _mapper = new();

        public override string AxisValue => DialectNames.Oracle;

        public override IDatabaseProvider Provider { get; } = new OracleProvider();

        public override CSharpTypeInfo Map(string dataType) => _mapper.Map(dataType);

        public override string Normalize(string dataType) => OracleTypeNotation.Normalize(dataType);

        public override string ImportedSpelling(ImportedType imported) =>
            OracleSchemaImporter.FormatDataType(
                imported.Name,
                imported.Precision,
                imported.Scale,
                imported.Length,
                imported.DataLength,
                imported.CharUsed
            );

        // user_tab_cols の data_type と精度・スケール・char_length・data_length・char_used
        // （OracleSchemaImporter.FormatDataType の XmlDoc：TIMESTAMP 系は data_type が精度・修飾を含む・RAW の char_length は 0）
        public override ImportedType CatalogValueOf(string nativeType)
        {
            var (name, arguments, _) = SplitType(nativeType);
            name = name.ToUpperInvariant();

            if (name.StartsWith("TIMESTAMP", StringComparison.Ordinal) || arguments.Count == 0)
            {
                return new ImportedType(nativeType);
            }

            return name switch
            {
                "NUMBER" => new ImportedType(
                    name,
                    Length: 0,
                    Precision: Argument(arguments[0]),
                    Scale: arguments.Count > 1 ? Argument(arguments[1]) : 0
                ),
                "VARCHAR2" or "CHAR" => new ImportedType(
                    name,
                    Length: Argument(arguments[0]),
                    CharUsed: "B"
                ),
                "NVARCHAR2" or "NCHAR" => new ImportedType(
                    name,
                    Length: Argument(arguments[0]),
                    CharUsed: "C"
                ),
                "RAW" => new ImportedType(name, Length: 0, DataLength: Argument(arguments[0])),
                _ => new ImportedType(nativeType),
            };
        }

        // Time は Oracle が書き出せない（上限の境界ではなく、どの値でも false）
        public override IReadOnlySet<CanonicalTypeKind> WriteLimitedKinds { get; } =
            Limited(
                [],
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.DateTimeOffset
            );

        // 解析が返さない種類：通貨・固定長のバイナリ・日付だけ・時刻・UUID・JSON・行バージョンの語彙が無い（DATE は時刻を含む日時）
        public override IReadOnlySet<CanonicalTypeKind> ParsedKinds { get; } =
            new HashSet<CanonicalTypeKind>
            {
                CanonicalTypeKind.Boolean,
                CanonicalTypeKind.TinyInt,
                CanonicalTypeKind.SmallInt,
                CanonicalTypeKind.Int32,
                CanonicalTypeKind.Int64,
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.Float32,
                CanonicalTypeKind.Float64,
                CanonicalTypeKind.String,
                CanonicalTypeKind.AnsiString,
                CanonicalTypeKind.FixedString,
                CanonicalTypeKind.AnsiFixedString,
                CanonicalTypeKind.Binary,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.DateTimeOffset,
                CanonicalTypeKind.Xml,
            };

        public override string ExpectedDisplayName => "Oracle";

        public override int? ExpectedDefaultPort => 1521;

        public override IReadOnlyList<string> ExpectedDataTypes => OracleDataTypes.All;

        public override IReadOnlyList<Type> ExpectedComponentTypes =>
            [
                typeof(OracleSchemaImporter),
                typeof(OracleCSharpTypeMapper),
                typeof(OracleTypeCatalog),
                typeof(OracleSyncScriptBuilder),
                typeof(OracleSchemaSyncExecutor),
                typeof(OracleDdlGenerator),
            ];
    }

    private sealed class SqliteTypeAdapter : DialectTypeAdapter
    {
        private readonly SqliteCSharpTypeMapper _mapper = new();

        public override string AxisValue => DialectNames.Sqlite;

        public override IDatabaseProvider Provider { get; } = new SqliteProvider();

        public override CSharpTypeInfo Map(string dataType) => _mapper.Map(dataType);

        // SQLite は宣言の型をそのまま返す（取込も宣言をそのまま持ち帰る）
        public override string Normalize(string dataType) => dataType;

        public override string ImportedSpelling(ImportedType imported) => imported.Name;

        // PRAGMA table_info の type は宣言の表記そのもの
        public override ImportedType CatalogValueOf(string nativeType) => new(nativeType);

        // 長さ・精度・小数秒の上限を持たない（宣言はアフィニティでしかない）
        public override IReadOnlySet<CanonicalTypeKind> WriteLimitedKinds { get; } =
            new HashSet<CanonicalTypeKind>();

        // 解析が返さない種類：単精度（REAL は倍精度のアフィニティ）と行バージョンの語彙が無い
        public override IReadOnlySet<CanonicalTypeKind> ParsedKinds { get; } =
            new HashSet<CanonicalTypeKind>
            {
                CanonicalTypeKind.Boolean,
                CanonicalTypeKind.TinyInt,
                CanonicalTypeKind.SmallInt,
                CanonicalTypeKind.Int32,
                CanonicalTypeKind.Int64,
                CanonicalTypeKind.Decimal,
                CanonicalTypeKind.Float64,
                CanonicalTypeKind.Money,
                CanonicalTypeKind.String,
                CanonicalTypeKind.AnsiString,
                CanonicalTypeKind.FixedString,
                CanonicalTypeKind.AnsiFixedString,
                CanonicalTypeKind.Binary,
                CanonicalTypeKind.FixedBinary,
                CanonicalTypeKind.Date,
                CanonicalTypeKind.Time,
                CanonicalTypeKind.DateTime,
                CanonicalTypeKind.DateTimeOffset,
                CanonicalTypeKind.Guid,
                CanonicalTypeKind.Xml,
                CanonicalTypeKind.Json,
            };

        public override string ExpectedDisplayName => "SQLite";

        public override int? ExpectedDefaultPort => null;

        public override IReadOnlyList<string> ExpectedDataTypes => SqliteDataTypes.All;

        public override IReadOnlyList<Type> ExpectedComponentTypes =>
            [
                typeof(SqliteSchemaImporter),
                typeof(SqliteCSharpTypeMapper),
                typeof(SqliteTypeCatalog),
                typeof(SqliteSyncScriptBuilder),
                typeof(SqliteSchemaSyncExecutor),
                typeof(SqliteDdlGenerator),
            ];
    }
}
