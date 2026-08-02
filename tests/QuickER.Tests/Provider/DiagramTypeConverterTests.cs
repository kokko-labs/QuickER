using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

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
