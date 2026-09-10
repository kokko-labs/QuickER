using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 拡張シム層（Generation Gap）の出力を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 生成コードの基底は「実装の置き場＝固定ランタイムの <c>*Core</c>」と「利用者が拡張する面＝現行名を名乗る
/// 生成 partial」の 2 層で、後者はスキーマ依存物なのでパッケージ参照モードでも per-型コードと同じファイル
/// （＝同じ層）へ必ず出る。これがこの層の存在理由そのもの（コンパイル済みパッケージのクラスへは partial を
/// 書けない）。
/// </para>
/// <para>
/// 値オブジェクトでは <c>ValueObjectBase&lt;TSelf, TValue&gt;</c> が全 VO の共通ルートで、系統別基底
/// （Ordered / String / Boolean / DateTime / Binary / GuidKey）はその派生として生成側に出る＝ルートへ足した
/// メンバーが系統に依らず全 VO へ届く。マーカー <c>IValueObject</c> はルートだけが実装し、系統別へは継承で
/// 波及する。
/// </para>
/// <para>
/// 検証対象: (1) インライン・分割・パッケージ参照モードのいずれでも拡張面が出ること、(2) per-型の基底句が
/// 現行名のままで、共通ルートを経由して固定ランタイムへ着地すること、(3) マーカーを実装するのはルート 1 本
/// だけで、系統別 6 本はルート（または Ordered）から派生すること、(4) 文字列 VO の基底が翻訳判定契約
/// <c>IStringMatchValueObject&lt;TSelf&gt;</c> を実装すること、(5) 層別出力で Entity/VO がドメイン層・
/// EditModel がプレゼンテーション層へ配置されること。
/// </para>
/// </remarks>
public sealed class ExtensionShimGenerationTests
{
    /// <summary>Entity シムの宣言（固定ランタイム <c>EntityBaseCore</c> を継承する空の partial）</summary>
    private const string EntityShim =
        "public abstract partial class EntityBase : EntityBaseCore { }";

    /// <summary>EditModel シムの宣言（CRTP 層 <c>EditModelBaseCore&lt;TSelf&gt;</c> を継承する空の partial）</summary>
    private const string EditModelShim =
        "public abstract partial class EditModelBase<TSelf> : EditModelBaseCore<TSelf>";

    /// <summary>値オブジェクトのマーカー（インターフェイス注入の一点化用）</summary>
    private const string ValueObjectMarkerShim =
        "public partial interface IValueObject : IValueObjectCore { }";

    /// <summary>全 VO の共通ルート（固定ランタイムの Core を継承し、マーカーを実装する唯一の宣言）</summary>
    private static readonly string ValueObjectRoot = string.Join(
        Environment.NewLine,
        "public abstract partial class ValueObjectBase<TSelf, TValue>",
        "    : ValueObjectBaseCore<TSelf, TValue>,",
        "        IValueObject",
        ""
    );

    /// <summary>文字列 VO の基底宣言（共通ルート派生＋翻訳判定契約の実装）</summary>
    private static readonly string ValueObjectStringBase = string.Join(
        Environment.NewLine,
        "public abstract partial class ValueObjectStringBase<TSelf>",
        "    : ValueObjectBase<TSelf, string>,",
        "        IStringMatchValueObject<TSelf>,",
        ""
    );

    /// <summary>系統別基底 6 本の「クラス名 → 直接の基底」対応（すべて共通ルートへ着地する）</summary>
    private static readonly (string Class, string BaseType)[] ValueObjectFamilyBases =
    [
        ("ValueObjectOrderedBase<TSelf, TValue>", "ValueObjectBase<TSelf, TValue>"),
        ("ValueObjectStringBase<TSelf>", "ValueObjectBase<TSelf, string>"),
        ("ValueObjectBooleanBase<TSelf>", "ValueObjectBase<TSelf, bool>"),
        ("ValueObjectDateTimeBase<TSelf>", "ValueObjectOrderedBase<TSelf, DateTime>"),
        ("ValueObjectBinaryBase<TSelf>", "ValueObjectBase<TSelf, byte[]>"),
        ("ValueObjectGuidKeyBase<TSelf>", "ValueObjectBase<TSelf, string>"),
    ];

    [Fact(DisplayName = "非分割: 拡張面が出力され、per-型の基底句は現行名のまま着地する")]
    public void Inline_ShouldEmitExtensionSurfacesAndKeepPerTypeBaseClauses()
    {
        var content = Single(Generate(Options()));

        content.Should().Contain(EntityShim);
        content.Should().Contain(EditModelShim);
        content.Should().Contain("    where TSelf : EditModelBase<TSelf> { }");
        content.Should().Contain(ValueObjectMarkerShim);
        AssertValueObjectBases(content);

        // per-型の基底句は現行名のまま
        content
            .Should()
            .Contain($"public partial class CustomerEntity : EntityBase{Environment.NewLine}");
        content
            .Should()
            .Contain("public partial class CustomerEditModel : EditModelBase<CustomerEditModel>");
        content.Should().Contain(": ValueObjectStringBase<NameValue>,");
        content.Should().Contain(": ValueObjectBooleanBase<IsActiveValue>,");

        // 実装の置き場は固定ランタイム側（*Core）で、拡張面とは別の宣言として同居する
        content.Should().Contain("public abstract partial class EntityBaseCore");
        content
            .Should()
            .Contain("public abstract partial class EditModelBaseCore<TSelf> : EditModelBaseCore");
        content.Should().Contain("public interface IValueObjectCore");
        content
            .Should()
            .Contain("public abstract partial class ValueObjectBaseCore<TSelf, TValue>");

        // 系統別基底が使う共有ヘルパーも固定ランタイム側にある
        content.Should().Contain("public static class ValueObjectComparisons");
        content.Should().Contain("public static class ValueObjectBinaryOperations");

        // ジェネリック契約は拡張面ではない＝名前も継承元も現行のまま（アリティ 1 は Core を継承する）
        content.Should().Contain("public interface IValueObject<TSelf> : IValueObjectCore");
        content
            .Should()
            .Contain("public interface IValueObject<TSelf, TValue> : IValueObject<TSelf>");
    }

    [Fact(DisplayName = "分割: 拡張面は per-型ファイル側へ出て Runtime ファイルには出ない")]
    public void Split_ShouldPlaceExtensionSurfacesWithPerTypeCode()
    {
        var files = ByName(Generate(Options() with { SplitFilesByCategory = true }));

        files["Entities.g.cs"].Should().Contain(EntityShim);
        files["EditModels.g.cs"].Should().Contain(EditModelShim);
        files["ValueObjects.g.cs"].Should().Contain(ValueObjectMarkerShim);
        AssertValueObjectBases(files["ValueObjects.g.cs"]);

        // 固定 infra は Runtime ファイルに集約され、拡張面はそこには出ない
        files["Runtime.g.cs"].Should().Contain("public abstract partial class EntityBaseCore");
        files["Runtime.g.cs"].Should().Contain("public static class ValueObjectComparisons");
        files["Runtime.g.cs"].Should().NotContain(EntityShim);
        files["Runtime.g.cs"].Should().NotContain(EditModelShim);
        files["Runtime.g.cs"].Should().NotContain(ValueObjectMarkerShim);
        files["Runtime.g.cs"].Should().NotContain("class ValueObjectStringBase<TSelf>");
    }

    [Fact(
        DisplayName = "パッケージ参照モード: 固定 infra は出ないが拡張面は出る（この層の存在理由）"
    )]
    public void PackageMode_ShouldStillEmitExtensionSurfaces()
    {
        var files = ByName(
            Generate(Options() with { SplitFilesByCategory = true, UseRuntimePackages = true })
        );

        files.Keys.Should().NotContain("Runtime.g.cs");

        files["Entities.g.cs"].Should().Contain(EntityShim);
        files["EditModels.g.cs"].Should().Contain(EditModelShim);
        files["ValueObjects.g.cs"].Should().Contain(ValueObjectMarkerShim);
        AssertValueObjectBases(files["ValueObjects.g.cs"]);

        // 固定 infra の宣言は 1 つも出ない（パッケージが提供する）
        var allContent = string.Join(Environment.NewLine, files.Values);
        allContent.Should().NotContain("abstract partial class EntityBaseCore");
        allContent.Should().NotContain("abstract partial class EditModelBaseCore");
        allContent.Should().NotContain("public interface IValueObjectCore");
        allContent.Should().NotContain("abstract partial class ValueObjectBaseCore");
        allContent.Should().NotContain("static class ValueObjectComparisons");
        allContent.Should().NotContain("static class ValueObjectBinaryOperations");
        allContent.Should().NotContain("interface IStringMatchValueObject<TSelf>");
    }

    [Fact(DisplayName = "層別出力: Entity/VO はドメイン層・EditModel はプレゼンテーション層へ出る")]
    public void Layered_ShouldPlaceExtensionSurfacesInDomainAndPresentation()
    {
        var result = Generate(Options() with { LayeredOutput = true });

        Layer(result, "Entities.g.cs").Should().Be("Domain");
        Layer(result, "ValueObjects.g.cs").Should().Be("Domain");
        Layer(result, "EditModels.g.cs").Should().Be("Presentation");

        Content(result, "Entities.g.cs").Should().Contain(EntityShim);
        Content(result, "ValueObjects.g.cs").Should().Contain(ValueObjectMarkerShim);
        Content(result, "EditModels.g.cs").Should().Contain(EditModelShim);
    }

    /// <summary>
    /// VO の基底が「共通ルート 1 本＋その派生の系統別 6 本」で、マーカー <c>IValueObject</c> の実装が
    /// ルートだけであることを検証する（系統別は継承でマーカーを得るので、ルートを外れた系統は
    /// 拡張が届かない枝になる）。文字列基底が翻訳判定契約を実装することも併せて確認する。
    /// </summary>
    private static void AssertValueObjectBases(string content)
    {
        content
            .Should()
            .Contain(ValueObjectRoot, "全 VO の共通ルートだけがマーカー IValueObject を実装する");

        foreach (var (className, baseType) in ValueObjectFamilyBases)
        {
            content
                .Should()
                .MatchRegex(
                    $@"public abstract partial class {Regex.Escape(className)}\s*: {Regex.Escape(baseType)}",
                    $"系統別基底 {className} は {baseType} を継承して共通ルートへ着地する"
                );
        }

        // マーカーを名乗る宣言はルート 1 本だけ（系統別は継承で得る）
        Regex
            .Matches(content, @"^[ \t]+IValueObject\r?$", RegexOptions.Multiline)
            .Should()
            .HaveCount(1, "マーカー IValueObject を実装するのは共通ルートだけ");

        // 文字列 VO の基底は翻訳判定の契約を実装する（式木翻訳がインターフェイスマップで引く）
        content.Should().Contain(ValueObjectStringBase);
    }

    /// <summary>Entity・EditModel・Mapper・VO がすべて出る最小構成</summary>
    private static CodeGenerationOptions Options() =>
        new()
        {
            RootNamespace = "Shim.Sample",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateValueObjects = true,
        };

    /// <summary>string / bool の VO が出る 1 エンティティの小さな ER 図</summary>
    private static ErDiagram Diagram()
    {
        var customer = new Entity { TableName = "customers" };
        customer.Columns.Add(
            new Column
            {
                Name = "customer_id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        customer.Columns.Add(
            new Column
            {
                Name = "name",
                DataType = "nvarchar(50)",
                IsNullable = false,
            }
        );
        customer.Columns.Add(
            new Column
            {
                Name = "is_active",
                DataType = "bit",
                IsNullable = false,
            }
        );

        return new ErDiagram { Entities = { customer } };
    }

    private static CodeGenerationResult Generate(CodeGenerationOptions options)
    {
        var diagram = Diagram();
        return new CSharpCodeGenerationService().Generate(
            diagram,
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            options
        );
    }

    private static string Single(CodeGenerationResult result)
    {
        result.HasErrors.Should().BeFalse();
        return result
            .Files.Single(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Content;
    }

    private static IReadOnlyDictionary<string, string> ByName(CodeGenerationResult result)
    {
        result.HasErrors.Should().BeFalse();
        return result.Files.ToDictionary(file => file.FileName, file => file.Content);
    }

    private static string Content(CodeGenerationResult result, string fileName)
    {
        result.HasErrors.Should().BeFalse();
        return result.Files.Single(file => file.FileName == fileName).Content;
    }

    private static string? Layer(CodeGenerationResult result, string fileName) =>
        result.Files.Single(file => file.FileName == fileName).RelativeDirectory;
}
