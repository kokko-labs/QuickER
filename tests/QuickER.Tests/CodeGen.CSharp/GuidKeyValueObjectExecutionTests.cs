using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// GuidKey 値オブジェクト（string 主キー × <c>UseGuidKeyForStringPrimaryKey</c>）の実行時挙動を検証する。
/// </summary>
/// <remarks>
/// <para>
/// GuidKey VO の具象はコミット済みフィクスチャに存在しない（フィクスチャ図は int 主キー）。そこで小さな図を
/// string 主キー＋<c>UseGuidKeyForStringPrimaryKey=true</c>＋<c>GenerateValueObjects=true</c> で生成し、
/// Roslyn で実コンパイル→アセンブリをロード→リフレクションで実行する（DB 不要・Docker 不要・CI 常時実行）。
/// </para>
/// <para>
/// 生成カテゴリは Entity＋値オブジェクトのみ（EditModel / Mapper / Repository / EF Core は無効）に絞り、依存を
/// BCL（TPA）だけに閉じる。<see cref="GeneratedCodeCompiler"/> はコンパイル可否のみを返すため、実行（Invoke）まで
/// 踏み込む本テストは同一方針の参照集合（TRUSTED_PLATFORM_ASSEMBLIES）で自前にコンパイル・ロードする。
/// </para>
/// </remarks>
public sealed class GuidKeyValueObjectExecutionTests
{
    /// <summary>生成→コンパイル→ロード済みの GuidKey VO 型（プロセス内で一度だけ構築してキャッシュ）。</summary>
    private static readonly Lazy<Type> GuidKeyValueObjectType = new(BuildGuidKeyValueObjectType);

    private static Type Vo => GuidKeyValueObjectType.Value;

    // ===== 実行検証 =====

    [Fact(DisplayName = "引数なし Create(): 新しい GUID を採番し、呼び出しごとに一意な値を返す")]
    public void 引数なしCreateはGUIDを採番する()
    {
        var first = InvokeCreateParameterless();
        var second = InvokeCreateParameterless();

        var firstValue = GetValue(first);
        var secondValue = GetValue(second);

        // GUID として解釈可能
        Guid.TryParse(firstValue, out _).Should().BeTrue();
        Guid.TryParse(secondValue, out _).Should().BeTrue();
        // 呼び出しごとに異なる
        firstValue.Should().NotBe(secondValue);
    }

    [Fact(DisplayName = "引数なし Create() を多数回呼んでも値が重複しない")]
    public void 引数なしCreateは重複しない()
    {
        var values = Enumerable
            .Range(0, 100)
            .Select(_ => GetValue(InvokeCreateParameterless()))
            .ToList();

        values.Distinct().Should().HaveCount(100);
    }

    [Fact(
        DisplayName = "値あり Create(string): 与えた文字列をそのまま保持する（GuidKey は追加検証なし）"
    )]
    public void 値ありCreate()
    {
        var vo = InvokeCreateString("my-key-123");

        GetValue(vo).Should().Be("my-key-123");
    }

    [Fact(DisplayName = "TryCreate(string): 正常入力で true・結果あり・エラー空")]
    public void TryCreateは正常入力でtrue()
    {
        var (ok, result, errors) = InvokeTryCreate("abc");

        ok.Should().BeTrue();
        result.Should().NotBeNull();
        GetValue(result!).Should().Be("abc");
        errors.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "ValueObjectGuidKeyBase: 同値は Equals/GetHashCode で等しく、異値は等しくない"
    )]
    public void 等値()
    {
        var a = InvokeCreateString("same");
        var b = InvokeCreateString("same");
        var c = InvokeCreateString("other");

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Equals(c).Should().BeFalse();
    }

    [Fact(DisplayName = "ValueObjectGuidKeyBase: CompareTo が序数比較で順序付けする")]
    public void 順序()
    {
        var a = (IComparable)InvokeCreateString("aaa");
        var b = InvokeCreateString("bbb");
        var sameAsA = InvokeCreateString("aaa");

        a.CompareTo(b).Should().BeNegative();
        ((IComparable)b).CompareTo(a).Should().BePositive();
        a.CompareTo(sameAsA).Should().Be(0);
        // null との比較は自分が大きい
        a.CompareTo(null).Should().BePositive();
    }

    // ===== リフレクション補助 =====

    private static object InvokeCreateParameterless()
    {
        // 引数なし Create() は基底 ValueObjectGuidKeyBase<TSelf> に定義される（閉じたジェネリック基底経由で解決）。
        var method =
            Vo.BaseType!.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            ) ?? throw new InvalidOperationException("引数なし Create() が見つからない");
        return method.Invoke(null, null)!;
    }

    private static object InvokeCreateString(string value)
    {
        var method =
            Vo.GetMethod("Create", new[] { typeof(string) })
            ?? throw new InvalidOperationException("Create(string) が見つからない");
        return method.Invoke(null, new object[] { value })!;
    }

    private static (bool Ok, object? Result, IEnumerable<string> Errors) InvokeTryCreate(
        string value
    )
    {
        var method =
            Vo.GetMethod("TryCreate")
            ?? throw new InvalidOperationException("TryCreate が見つからない");
        var args = new object?[] { value, null, null };
        var ok = (bool)method.Invoke(null, args)!;
        return (ok, args[1], (IEnumerable<string>)args[2]!);
    }

    private static string GetValue(object vo) => (string)Vo.GetProperty("Value")!.GetValue(vo)!;

    // ===== 生成・コンパイル・ロード =====

    /// <summary>string 主キーの小さな図を GuidKey VO 付きで生成・コンパイルし、GuidKey VO 型をロードして返す。</summary>
    private static Type BuildGuidKeyValueObjectType()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "widgets",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "widget_id",
                            DataType = "nvarchar(36)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "label",
                            DataType = "nvarchar(50)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateValueObjects = true,
            UseGuidKeyForStringPrimaryKey = true,
            // 依存を BCL のみへ閉じるため Entity＋VO 以外は生成しない
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);
        result
            .HasErrors.Should()
            .BeFalse(
                "GuidKey 生成自体が失敗: "
                    + string.Join(
                        " / ",
                        result
                            .Diagnostics.Where(d =>
                                d.Severity == GenerationDiagnosticSeverity.Error
                            )
                            .Select(d => d.Message)
                    )
            );

        var assembly = CompileAndLoad(result);

        // 基底が ValueObjectGuidKeyBase<> の具象型を探す（型名の命名規約に依存しない）
        return assembly
            .GetTypes()
            .Single(t =>
                t.BaseType is { IsGenericType: true }
                && t.BaseType.GetGenericTypeDefinition().Name == "ValueObjectGuidKeyBase`1"
            );
    }

    /// <summary>生成結果を TPA 参照集合でコンパイルし、成功時にロードしたアセンブリを返す。</summary>
    private static Assembly CompileAndLoad(CodeGenerationResult result)
    {
        var syntaxTrees = result
            .Files.Select(file =>
                CSharpSyntaxTree.ParseText(
                    file.Content,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path: file.FileName
                )
            )
            .ToArray();

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trustedAssemblies
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"QuickER.GuidKey.Tests.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        emitResult
            .Success.Should()
            .BeTrue(
                "GuidKey 生成コードのコンパイルに失敗:"
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        emitResult
                            .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => d.ToString())
                    )
            );

        peStream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(peStream.ToArray());
    }
}
