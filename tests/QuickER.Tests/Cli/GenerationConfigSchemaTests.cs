using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.Cli;

/// <summary>
/// <see cref="GenerationConfigSchema"/>（MCP の <c>get_generation_config_schema</c> ツールが返すカタログ）が、
/// 設定ローダーの正となるキー集合（<see cref="CodeGenerationOptions"/> の init 設定可能プロパティ）と
/// 完全一致していることを守るドリフト防止ガード＋出力形式・英語ガードのテスト。
/// </summary>
/// <remarks>
/// <see cref="GenerationConfigLoader"/> は <c>node.Deserialize&lt;CodeGenerationOptions&gt;</c> でリフレクションにより
/// 設定 JSON を認識するため、正となるキー集合は <see cref="CodeGenerationOptions"/> の
/// init 設定可能なインスタンスプロパティ（get-only の計算プロパティ・static は除く）である。
/// 将来オプションが増えてカタログ未更新なら、ここが赤になる。
/// </remarks>
public sealed class GenerationConfigSchemaTests
{
    /// <summary>CJK 文字の検出パターン（ErDiagramToolCatalogEnglishGuardTests と同一範囲）</summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>設定ローダーが認識する正となるキー集合＝init 設定可能なインスタンスプロパティ</summary>
    private static IReadOnlyList<PropertyInfo> RecognizedProperties =>
        typeof(CodeGenerationOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite) // init セッター有り＝設定 JSON からデシリアライズされる。計算 get-only は除外
            .ToList();

    /// <summary>カタログのキー集合が正となるキー集合と完全一致する（過不足の両方向を検知）</summary>
    [Fact(DisplayName = "カタログのキー集合は CodeGenerationOptions のプロパティと完全一致する")]
    public void Keys_MatchCodeGenerationOptionsExactly()
    {
        var catalogNames = GenerationConfigSchema.Keys.Select(k => k.Name).ToHashSet();
        var recognizedNames = RecognizedProperties.Select(p => p.Name).ToHashSet();

        catalogNames
            .Should()
            .BeEquivalentTo(
                recognizedNames,
                "get_generation_config_schema のカタログ（src/QuickER.Cli/GenerationConfigSchema.cs）は "
                    + "CodeGenerationOptions の設定キーと 1:1 でなければなりません。差分がある場合はカタログを更新してください"
            );
    }

    /// <summary>各カタログ項目の型・既定値が CodeGenerationOptions の実宣言と一致する</summary>
    [Fact(DisplayName = "カタログの型・既定値は実宣言と一致する")]
    public void Keys_TypesAndDefaults_MatchDeclarations()
    {
        var defaults = new CodeGenerationOptions();
        var byName = RecognizedProperties.ToDictionary(p => p.Name);

        foreach (var key in GenerationConfigSchema.Keys)
        {
            byName.ContainsKey(key.Name).Should().BeTrue($"'{key.Name}' は既知のプロパティのはず");
            var prop = byName[key.Name];

            // 型: bool→boolean / string→string / IReadOnlyList<string>→string[]
            ExpectedType(prop.PropertyType)
                .Should()
                .Be(key.Type, $"'{key.Name}' の型はプロパティ宣言に一致するはず");

            // 既定値: 新規インスタンスの値と一致する（bool は既定値・string は既定文字列・nullable は null）
            var actualDefault = prop.GetValue(defaults);
            NormalizeDefault(actualDefault)
                .Should()
                .Be(
                    NormalizeDefault(key.Default),
                    $"'{key.Name}' の既定値は CodeGenerationOptions のインスタンス既定に一致するはず"
                );
        }
    }

    /// <summary>ツール出力は JSON としてパース可能で、代表キー・rules・example を含む</summary>
    [Fact(DisplayName = "get_generation_config_schema は代表キーを含む JSON を返す")]
    public void BuildJson_ProducesParseableCatalog()
    {
        var json = GenerationConfigSchema.BuildJson();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("description", out _).Should().BeTrue();
        root.TryGetProperty("rules", out var rules).Should().BeTrue();
        rules.GetArrayLength().Should().BeGreaterThan(0);
        root.TryGetProperty("example", out _).Should().BeTrue();

        root.TryGetProperty("keys", out var keys).Should().BeTrue();
        var names = keys.EnumerateArray()
            .Select(k => k.GetProperty("name").GetString())
            .ToHashSet();

        names.Should().Contain("GenerateRepositories");
        names.Should().Contain("GenerateEfCoreRepositories");
        names.Should().Contain("RootNamespace");
        names.Should().Contain("RepositoryDialects");

        // RepositoryDialects は allowedValues を持ち、default は null で明示される
        var repositoryDialects = keys.EnumerateArray()
            .Single(k => k.GetProperty("name").GetString() == "RepositoryDialects");
        repositoryDialects.GetProperty("default").ValueKind.Should().Be(JsonValueKind.Null);
        repositoryDialects
            .GetProperty("allowedValues")
            .EnumerateArray()
            .Select(v => v.GetString())
            .Should()
            .BeEquivalentTo("sqlserver", "sqlite");
    }

    /// <summary>ツールの実行デリゲート経由（引数なし）でも同じカタログ JSON が返る</summary>
    [Fact(DisplayName = "get_generation_config_schema は file 引数なしで実行できる")]
    public void Execute_WithoutFileArgument_ReturnsCatalog()
    {
        var (result, success) = CodeGenToolSet
            .Create()
            .Execute("get_generation_config_schema", "{}");

        success.Should().BeTrue(result);
        using var doc = JsonDocument.Parse(result);
        doc.RootElement.TryGetProperty("keys", out _).Should().BeTrue();
    }

    /// <summary>カタログ全文（説明・rules）に日本語（CJK）が混入しない（英語ガード）</summary>
    [Fact(DisplayName = "カタログに日本語（CJK）が含まれない")]
    public void Catalog_ContainsNoCjk()
    {
        var json = GenerationConfigSchema.BuildJson();

        CjkPattern
            .IsMatch(json)
            .Should()
            .BeFalse(
                "get_generation_config_schema のカタログは英語で統一する必要があります"
                    + "（src/QuickER.Cli/GenerationConfigSchema.cs を確認してください）"
            );
    }

    /// <summary>プロパティ型を JSON 型トークン（boolean / string / string[]）へ写像する</summary>
    private static string ExpectedType(System.Type type)
    {
        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(type))
        {
            return "string[]";
        }

        return type.Name;
    }

    /// <summary>既定値を比較しやすい形へ正規化する（IEnumerable&lt;string&gt; は要素列へ）</summary>
    private static object? NormalizeDefault(object? value) =>
        value is IEnumerable enumerable && value is not string
            ? enumerable.Cast<object?>().ToList()
            : value;
}
