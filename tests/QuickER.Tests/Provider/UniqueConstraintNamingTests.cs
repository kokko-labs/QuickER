using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="UniqueConstraintNaming"/> の制約名解決（モデル名の優先・合成名の規則・構成列の解決）を検証するテストクラス
/// </summary>
public class UniqueConstraintNamingTests
{
    /// <summary>テスト用の識別子安全化関数（"." と空白を "_" へ置換する。方言実装と同じ流儀）</summary>
    private static string SafeName(string name) => name.Replace(".", "_").Replace(" ", "_");

    /// <summary>モデルに制約名があればそのまま返すことを検証する</summary>
    [Fact(DisplayName = "Resolve: モデルの制約名があればそのまま使う")]
    public void Resolve_UsesModelName()
    {
        UniqueConstraintNaming
            .Resolve("UQ_Custom", "Shop", ["Code"], SafeName)
            .Should()
            .Be("UQ_Custom");
    }

    /// <summary>制約名が null / 空白なら UQ_{テーブル}_{列…} を宣言順で合成することを検証する</summary>
    [Theory(DisplayName = "Resolve: 制約名が未設定なら UQ_テーブル_列… を合成する")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_SynthesizesName(string? name)
    {
        UniqueConstraintNaming
            .Resolve(name, "Shop", ["Region", "Code"], SafeName)
            .Should()
            .Be("UQ_Shop_Region_Code");
    }

    /// <summary>合成名がテーブル名・列名の双方へ SafeName を適用することを検証する</summary>
    [Fact(DisplayName = "Resolve: 合成名はテーブル名・列名とも SafeName で正規化する")]
    public void Resolve_AppliesSafeNameToAllParts()
    {
        UniqueConstraintNaming
            .Resolve(null, "sales.Shop", ["Zip Code"], SafeName)
            .Should()
            .Be("UQ_sales_Shop_Zip_Code");
    }

    /// <summary>ResolveAll がモデルの並び順を保ち、列 ID を宣言順の列名へ解決することを検証する</summary>
    [Fact(DisplayName = "ResolveAll: モデルの並び順・宣言順を保って列名へ解決する")]
    public void ResolveAll_KeepsOrder()
    {
        var code = new Column { Name = "Code", DataType = "int" };
        var region = new Column { Name = "Region", DataType = "int" };
        var entity = new Entity { TableName = "Shop", Columns = { code, region } };
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Named", ColumnIds = [region.Id, code.Id] }
        );
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [code.Id] });

        var resolved = UniqueConstraintNaming.ResolveAll(entity, SafeName);

        resolved.Should().HaveCount(2);
        resolved[0].Name.Should().Be("UQ_Named");
        resolved[0].ColumnNames.Should().Equal("Region", "Code");
        // 名前なしは合成名になる
        resolved[1].Name.Should().Be("UQ_Shop_Code");
        resolved[1].ColumnNames.Should().Equal("Code");
    }

    /// <summary>構成列が空、または解決できないカラム ID を含む制約が除外されることを検証する</summary>
    [Fact(DisplayName = "ResolveAll: 空の制約・解決できない列を含む制約は除外する")]
    public void ResolveAll_SkipsBrokenConstraints()
    {
        var code = new Column { Name = "Code", DataType = "int" };
        var entity = new Entity { TableName = "Shop", Columns = { code } };
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [] });
        entity.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [code.Id, Guid.NewGuid()] }
        );

        UniqueConstraintNaming.ResolveAll(entity, SafeName).Should().BeEmpty();
    }
}
