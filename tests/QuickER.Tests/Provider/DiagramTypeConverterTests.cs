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
/// <see cref="DiagramTypeConverter"/> の変換計画作成・適用のうち、偽のカタログを使う検証と、
/// 全方言の型の選択肢を総当たりする検証を持つテストクラス。
/// </summary>
/// <remarks>
/// 実在の方言の組の変換結果は方言の型の表（<c>DialectTypeRows.Convert.cs</c> の <c>convert-x-*</c>）が固定する。
/// </remarks>
public class DiagramTypeConverterTests
{
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
