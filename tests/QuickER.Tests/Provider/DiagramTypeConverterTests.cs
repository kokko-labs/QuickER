using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="DiagramTypeConverter"/> の変換計画作成・適用を検証するテストクラス。
/// </summary>
public class DiagramTypeConverterTests
{
    /// <summary>from と to が参照同一なら空の計画を返すことを検証する</summary>
    [Fact(DisplayName = "from と to が同一カタログなら空の計画を返す")]
    public void CreatePlan_SameCatalogInstance_ReturnsEmptyPlan()
    {
        var catalog = new SqlServerTypeCatalog();
        var diagram = BuildDiagram("nvarchar(100)");

        var plan = DiagramTypeConverter.CreatePlan(diagram, catalog, catalog);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().BeEmpty();
    }

    /// <summary>変換可能な型が Converted に入り、新しい型が算出されることを検証する</summary>
    [Fact(DisplayName = "変換可能な型は Converted に入る")]
    public void CreatePlan_ConvertibleType_AddsToConverted()
    {
        var from = new SqlServerTypeCatalog();
        var to = new UpperCaseTypeCatalog();
        var diagram = BuildDiagram("nvarchar(100)");

        var plan = DiagramTypeConverter.CreatePlan(diagram, from, to);

        plan.Converted.Should().ContainSingle();
        plan.Unconverted.Should().BeEmpty();
        var conversion = plan.Converted[0];
        conversion.OldType.Should().Be("nvarchar(100)");
        conversion.NewType.Should().Be("NVARCHAR(100)");
    }

    /// <summary>変換不能な型（from が解析不能）は Unconverted に入り、NewType が null になることを検証する</summary>
    [Fact(DisplayName = "from 側で解析不能な型は Unconverted に入り NewType は null")]
    public void CreatePlan_UnparsableSourceType_AddsToUnconverted()
    {
        var from = new SqlServerTypeCatalog();
        var to = new UpperCaseTypeCatalog();
        var diagram = BuildDiagram("hierarchyid");

        var plan = DiagramTypeConverter.CreatePlan(diagram, from, to);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        var conversion = plan.Unconverted[0];
        conversion.OldType.Should().Be("hierarchyid");
        conversion.NewType.Should().BeNull();
    }

    /// <summary>変換不能な型（to 側でフォーマット不能）は Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "to 側でフォーマット不能な型は Unconverted に入る")]
    public void CreatePlan_UnformattableTargetType_AddsToUnconverted()
    {
        var from = new SqlServerTypeCatalog();
        var to = new NoJsonTypeCatalog();
        var diagram = BuildDiagram("xml");

        var plan = DiagramTypeConverter.CreatePlan(diagram, from, to);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }

    /// <summary>Apply は Converted のみを図へ反映し、Unconverted の型は変更しないことを検証する</summary>
    [Fact(DisplayName = "Apply は Converted のみ反映し、Unconverted の型は変更しない")]
    public void Apply_OnlyAppliesConvertedItems()
    {
        var from = new SqlServerTypeCatalog();
        var to = new UpperCaseTypeCatalog();
        var diagram = BuildDiagram("nvarchar(100)");
        var unconvertibleColumn = new Column { Name = "geo", DataType = "hierarchyid" };
        diagram.Entities[0].Columns.Add(unconvertibleColumn);

        var plan = DiagramTypeConverter.CreatePlan(diagram, from, to);
        DiagramTypeConverter.Apply(diagram, plan);

        diagram.Entities[0].Columns[0].DataType.Should().Be("NVARCHAR(100)");
        diagram.Entities[0].Columns[1].DataType.Should().Be("hierarchyid");
    }

    /// <summary>
    /// SQL Server の rowversion が SQLite では BLOB（ただのバイナリ列）へ落ち、NOT NULL も解除されることを検証する。
    /// </summary>
    /// <remarks>
    /// ローカル側では DB が採番しないため、同期が値を書き込むまでその列は空になる。NOT NULL のままだと
    /// ローカルで新しい行を作れない（＝ハイブリッド構成が成立しない）ので、変換にあわせて NULL 許容へ倒す。
    /// </remarks>
    [Fact(DisplayName = "SQL Server の rowversion は SQLite では BLOB かつ NULL 許容へ変換される")]
    public void CreatePlan_RowVersionToSqlite_ConvertsToNullableBlob()
    {
        var diagram = BuildDiagram("rowversion");
        diagram.Entities[0].Columns[0].IsNullable = false;

        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerTypeCatalog(),
            new SqliteTypeCatalog()
        );

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be("BLOB");
        plan.Converted[0].MakeNullable.Should().BeTrue();

        DiagramTypeConverter.Apply(diagram, plan);
        diagram.Entities[0].Columns[0].DataType.Should().Be("BLOB");
        diagram.Entities[0].Columns[0].IsNullable.Should().BeTrue();
    }

    /// <summary>行バージョンのまま持てる方言へ変換する場合は NOT NULL を解除しないことを検証する</summary>
    /// <remarks>
    /// 判定は「変換先の型を読み直しても行バージョンのままか」で行うため、方言名では分岐しない。
    /// <see cref="UpperCaseTypeCatalog"/> は <c>ROWVERSION</c> を出しつつ読み戻せるため、解除は起きない。
    /// </remarks>
    [Fact(DisplayName = "変換先でも行バージョンのままなら NOT NULL は解除しない")]
    public void CreatePlan_RowVersionStaysRowVersion_KeepsNotNull()
    {
        var diagram = BuildDiagram("rowversion");
        diagram.Entities[0].Columns[0].IsNullable = false;

        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerTypeCatalog(),
            new UpperCaseTypeCatalog()
        );

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be("ROWVERSION");
        plan.Converted[0].MakeNullable.Should().BeFalse();
    }

    /// <summary>元から NULL 許容の行バージョン列は「解除するもの」が無いことを検証する</summary>
    [Fact(DisplayName = "元から NULL 許容の行バージョン列は MakeNullable が立たない")]
    public void CreatePlan_NullableRowVersion_DoesNotFlagMakeNullable()
    {
        var diagram = BuildDiagram("rowversion");
        diagram.Entities[0].Columns[0].IsNullable = true;

        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerTypeCatalog(),
            new SqliteTypeCatalog()
        );

        plan.Converted[0].MakeNullable.Should().BeFalse();
    }

    /// <summary>主キーの行バージョン列は変換に成功しても NOT NULL を解除しないことを検証する</summary>
    /// <remarks>
    /// DDL 生成・差分同期・列 ViewModel の 3 層が主キー列を NOT NULL へクランプするため実害は無いが、
    /// モデル層に「NULL 許容の主キー」が一時的に載るのを避けるため、変換計画の段階で除外する。
    /// </remarks>
    [Fact(DisplayName = "主キーの行バージョン列は BLOB へ変換されるが MakeNullable は立たない")]
    public void CreatePlan_PrimaryKeyRowVersionToSqlite_DoesNotFlagMakeNullable()
    {
        var diagram = BuildDiagram("rowversion");
        diagram.Entities[0].Columns[0].IsNullable = false;
        diagram.Entities[0].Columns[0].IsPrimaryKey = true;

        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerTypeCatalog(),
            new SqliteTypeCatalog()
        );

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be("BLOB");
        plan.Converted[0].MakeNullable.Should().BeFalse();

        DiagramTypeConverter.Apply(diagram, plan);
        diagram.Entities[0].Columns[0].DataType.Should().Be("BLOB");
        diagram.Entities[0].Columns[0].IsNullable.Should().BeFalse();
    }

    /// <summary>SQLite の BLOB を SQL Server へ戻しても rowversion には戻らない（非可逆）ことを固定する</summary>
    [Fact(DisplayName = "SQLite の BLOB は SQL Server へ戻すと varbinary(max) になる（非可逆）")]
    public void CreatePlan_SqliteBlobBackToSqlServer_IsNotRowVersion()
    {
        var diagram = BuildDiagram("BLOB");

        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqliteTypeCatalog(),
            new SqlServerTypeCatalog()
        );

        plan.Converted[0].NewType.Should().Be("varbinary(max)");
    }

    /// <summary>
    /// Oracle の精度なし・スケール 0 の整数（<c>INTEGER</c> / <c>SMALLINT</c> / <c>NUMBER(*,0)</c>）は、
    /// 精度なしの decimal を書けない方言へは 38 桁の decimal として注意つきで変換されることを検証する
    /// </summary>
    /// <remarks>
    /// SQLite は精度なしの <c>DECIMAL</c> をそのまま書けるので注意を付けない（桁の上限が新しく付かない）。
    /// </remarks>
    [Theory(
        DisplayName = "Oracle の精度なし・スケール 0 の整数は、精度を書く方言へ 38 桁の decimal として注意つきで変換される"
    )]
    [InlineData("INTEGER", "sqlserver", "decimal(38,0)", true)]
    [InlineData("SMALLINT", "sqlserver", "decimal(38,0)", true)]
    [InlineData("NUMBER(*,0)", "sqlserver", "decimal(38,0)", true)]
    [InlineData("INTEGER", "postgresql", "numeric(38,0)", true)]
    [InlineData("NUMBER(*,0)", "postgresql", "numeric(38,0)", true)]
    [InlineData("INTEGER", "mysql", "decimal(38,0)", true)]
    [InlineData("NUMBER(*,0)", "mysql", "decimal(38,0)", true)]
    [InlineData("INTEGER", "sqlite", "DECIMAL", false)]
    [InlineData("NUMBER(*,0)", "sqlite", "DECIMAL", false)]
    public void CreatePlan_OracleUnlimitedInteger_ConvertsWithPrecisionCap(
        string oracleType,
        string target,
        string expectedType,
        bool expectedCapped
    )
    {
        var diagram = BuildDiagram(oracleType);

        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new OracleTypeCatalog(),
            Catalog(target)
        );

        plan.Unconverted.Should().BeEmpty();
        var conversion = plan.Converted.Should().ContainSingle().Subject;
        conversion.NewType.Should().Be(expectedType);
        conversion.PrecisionCapped.Should().Be(expectedCapped);
        conversion.MakeNullable.Should().BeFalse();

        DiagramTypeConverter.Apply(diagram, plan);
        diagram.Entities[0].Columns[0].DataType.Should().Be(expectedType);
    }

    /// <summary>
    /// 精度なし・スケールなしの <c>NUMBER</c> と、0 以外のスケールの <c>NUMBER(*,s)</c> は 38 桁へ倒さず、
    /// 従来どおり変換できない（または注意なしで変換する）ことを検証する
    /// </summary>
    [Theory(DisplayName = "Oracle の NUMBER と 0 以外のスケールの NUMBER(*,s) は 38 桁へ倒さない")]
    [InlineData("NUMBER", "sqlserver", null)]
    [InlineData("NUMBER", "mysql", null)]
    [InlineData("NUMBER", "postgresql", "numeric")]
    [InlineData("NUMBER(*,2)", "sqlserver", null)]
    [InlineData("NUMBER(*,2)", "postgresql", null)]
    [InlineData("NUMBER(*,2)", "mysql", null)]
    [InlineData("NUMBER(*,2)", "sqlite", "DECIMAL")]
    public void CreatePlan_OracleNumberWithoutIntegerScale_IsNotCapped(
        string oracleType,
        string target,
        string? expectedType
    )
    {
        var plan = DiagramTypeConverter.CreatePlan(
            BuildDiagram(oracleType),
            new OracleTypeCatalog(),
            Catalog(target)
        );

        if (expectedType is null)
        {
            plan.Converted.Should().BeEmpty();
            plan.Unconverted.Should().ContainSingle();
            return;
        }

        var conversion = plan.Converted.Should().ContainSingle().Subject;
        conversion.NewType.Should().Be(expectedType);
        conversion.PrecisionCapped.Should().BeFalse();
    }

    /// <summary>
    /// 注意の印が付くのは、元の型が精度なし・スケール 0 の decimal のときだけであることを、
    /// 5 方言の全ての組み合わせと各方言の型の選択肢で検証する
    /// </summary>
    [Fact(DisplayName = "注意の印は、精度なし・スケール 0 の decimal を変換したときだけ付く")]
    public void CreatePlan_PrecisionCap_OnlyForPrecisionlessIntegerDecimal()
    {
        ITypeCatalog[] catalogs =
        [
            new SqlServerTypeCatalog(),
            new PostgreSqlTypeCatalog(),
            new MySqlTypeCatalog(),
            new OracleTypeCatalog(),
            new SqliteTypeCatalog(),
        ];
        var capped = 0;

        foreach (var from in catalogs)
        {
            var types = from.DataTypes.Append("INTEGER").Append("NUMBER(*,0)").ToArray();

            foreach (var to in catalogs.Where(c => !ReferenceEquals(c, from)))
            {
                foreach (var type in types)
                {
                    var plan = DiagramTypeConverter.CreatePlan(BuildDiagram(type), from, to);

                    foreach (var conversion in plan.Converted.Where(c => c.PrecisionCapped))
                    {
                        var where = $"{from.GetType().Name} の {type} → {to.GetType().Name}";

                        from.TryParse(type, out var canonical).Should().BeTrue(where);
                        canonical
                            .Should()
                            .Be(
                                new CanonicalType(
                                    CanonicalTypeKind.Decimal,
                                    Precision: null,
                                    Scale: 0
                                ),
                                where
                            );
                        to.TryParse(conversion.NewType!, out var written).Should().BeTrue(where);
                        written
                            .Precision.Should()
                            .Be(DiagramTypeConverter.CappedPrecision, where + "の変換後の精度");
                        written.Scale.Should().Be(0, where + "の変換後のスケール");
                        capped++;
                    }
                }
            }
        }

        capped.Should().BePositive("Oracle の整数を精度を書く方言へ変換した組み合わせがある");
    }

    private static ITypeCatalog Catalog(string dialect) =>
        dialect switch
        {
            "sqlserver" => new SqlServerTypeCatalog(),
            "postgresql" => new PostgreSqlTypeCatalog(),
            "mysql" => new MySqlTypeCatalog(),
            "sqlite" => new SqliteTypeCatalog(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };

    private static ErDiagram BuildDiagram(string dataType)
    {
        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "customers",
                    Columns = [new Column { Name = "name", DataType = dataType }],
                },
            ],
        };
    }

    /// <summary>SqlServerTypeCatalog の正規型を大文字化したネイティブ型文字列に変換するテスト専用カタログ</summary>
    private sealed class UpperCaseTypeCatalog : ITypeCatalog
    {
        private readonly SqlServerTypeCatalog _inner = new();

        public IReadOnlyList<string> DataTypes => _inner.DataTypes;

        public string DefaultDataType => _inner.DefaultDataType;

        public bool TryParse(string nativeType, out CanonicalType canonical) =>
            _inner.TryParse(nativeType, out canonical);

        public bool TryFormat(CanonicalType canonical, out string nativeType)
        {
            if (!_inner.TryFormat(canonical, out var formatted))
            {
                nativeType = string.Empty;
                return false;
            }

            nativeType = formatted.ToUpperInvariant();
            return true;
        }
    }

    /// <summary>Xml 型のみフォーマット不能にしたテスト専用カタログ（to 側変換不能パスの検証用）</summary>
    private sealed class NoJsonTypeCatalog : ITypeCatalog
    {
        private readonly SqlServerTypeCatalog _inner = new();

        public IReadOnlyList<string> DataTypes => _inner.DataTypes;

        public string DefaultDataType => _inner.DefaultDataType;

        public bool TryParse(string nativeType, out CanonicalType canonical) =>
            _inner.TryParse(nativeType, out canonical);

        public bool TryFormat(CanonicalType canonical, out string nativeType)
        {
            if (canonical.Kind == CanonicalTypeKind.Xml)
            {
                nativeType = string.Empty;
                return false;
            }

            return _inner.TryFormat(canonical, out nativeType);
        }
    }
}
