using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="OutputFolderNamespaceSuggester" /> の名前空間導出（csproj 探索・RootNamespace 読み取り・
/// 相対階層連結・サニタイズ・導出不能時の null）を、一時フォルダに実 csproj を置いて検証するテストクラス
/// </summary>
public class OutputFolderNamespaceSuggesterTests
{
    /// <summary>一時作業フォルダ（テストごとに一意）を作って返す</summary>
    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            "NsSuggest",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>指定ディレクトリに csproj を書き出す（RootNamespace の有無を選べる）</summary>
    private static void WriteCsproj(string directory, string fileName, string? rootNamespace)
    {
        Directory.CreateDirectory(directory);

        var rootNamespaceElement = rootNamespace is null
            ? string.Empty
            : $"    <RootNamespace>{rootNamespace}</RootNamespace>\n";

        var content =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            + "  <PropertyGroup>\n"
            + "    <TargetFramework>net10.0</TargetFramework>\n"
            + rootNamespaceElement
            + "  </PropertyGroup>\n"
            + "</Project>\n";

        File.WriteAllText(Path.Combine(directory, fileName), content);
    }

    /// <summary>RootNamespace 付き csproj のサブフォルダでは「ルート ＋ 相対階層」を連結する</summary>
    [Fact(DisplayName = "csproj（RootNamespace あり）＋サブフォルダ → ルート.相対階層 を連結する")]
    public void WithRootNamespace_AndSubFolders_JoinsRelativeSegments()
    {
        var root = CreateTempRoot();

        try
        {
            // プロジェクト = root、RootNamespace = Root。出力先 = root/Sub/Folder
            WriteCsproj(root, "MyProject.csproj", rootNamespace: "Root");
            var target = Path.Combine(root, "Sub", "Folder");
            Directory.CreateDirectory(target);

            OutputFolderNamespaceSuggester.TryDerive(target).Should().Be("Root.Sub.Folder");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>RootNamespace が無い csproj では、ファイル名（拡張子除去）をベースにする</summary>
    [Fact(DisplayName = "csproj（RootNamespace なし）→ ファイル名ベースを使う")]
    public void WithoutRootNamespace_UsesCsprojFileName()
    {
        var root = CreateTempRoot();

        try
        {
            WriteCsproj(root, "Contoso.Sales.csproj", rootNamespace: null);
            var target = Path.Combine(root, "Repositories");
            Directory.CreateDirectory(target);

            // ベース "Contoso.Sales"（ドットは各セグメントに分割）＋ 相対 "Repositories"
            OutputFolderNamespaceSuggester
                .TryDerive(target)
                .Should()
                .Be("Contoso.Sales.Repositories");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>プロジェクトディレクトリ直下（相対階層なし）ではベース名前空間のみになる</summary>
    [Fact(DisplayName = "csproj と同じフォルダ → ベース名前空間のみ")]
    public void InProjectDirectory_ReturnsBaseNamespaceOnly()
    {
        var root = CreateTempRoot();

        try
        {
            WriteCsproj(root, "Acme.App.csproj", rootNamespace: "Acme.App");

            OutputFolderNamespaceSuggester.TryDerive(root).Should().Be("Acme.App");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>csproj が一切見つからない場合は、選択フォルダ名 1 セグメントのみを候補にする</summary>
    [Fact(DisplayName = "csproj なし → フォルダ名のみ")]
    public void WithoutAnyCsproj_UsesFolderNameOnly()
    {
        var root = CreateTempRoot();

        try
        {
            var target = Path.Combine(root, "Generated");
            Directory.CreateDirectory(target);

            OutputFolderNamespaceSuggester.TryDerive(target).Should().Be("Generated");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>無効文字・数字始まりのセグメントはサニタイズ（'_' 置換・'_' 前置）される</summary>
    [Fact(DisplayName = "無効文字・数字始まりのセグメントはサニタイズされる")]
    public void InvalidCharsAndLeadingDigits_AreSanitized()
    {
        var root = CreateTempRoot();

        try
        {
            WriteCsproj(root, "Base.csproj", rootNamespace: "Base");
            // 無効文字（ハイフン・空白）と数字始まりを含むフォルダ
            var target = Path.Combine(root, "1st-Layer", "My Folder");
            Directory.CreateDirectory(target);

            // "1st-Layer" → "_1st_Layer"、"My Folder" → "My_Folder"
            OutputFolderNamespaceSuggester
                .TryDerive(target)
                .Should()
                .Be("Base._1st_Layer.My_Folder");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>C# の予約語と同名のフォルダはアンダースコアを前置してサニタイズされる</summary>
    /// <remarks>
    /// 「導出結果は必ず namespace 検証（<see cref="CSharpNamespaceValidator"/>）を通る」不変条件を守る。
    /// 素通しすると <c>namespace Base.class;</c> というコンパイル不能な候補を提示してしまう
    /// </remarks>
    [Fact(DisplayName = "予約語と同名のフォルダはサニタイズされる")]
    public void ReservedKeywordFolderName_IsSanitized()
    {
        var root = CreateTempRoot();

        try
        {
            WriteCsproj(root, "Base.csproj", rootNamespace: "Base");
            var target = Path.Combine(root, "class");
            Directory.CreateDirectory(target);

            var derived = OutputFolderNamespaceSuggester.TryDerive(target);

            derived.Should().Be("Base._class");
            CSharpNamespaceValidator.IsValid(derived).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>存在しないパスは導出不能（null）</summary>
    [Fact(DisplayName = "存在しないパス → null")]
    public void NonExistentPath_ReturnsNull()
    {
        var missing = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            "DoesNotExist",
            Guid.NewGuid().ToString("N")
        );

        OutputFolderNamespaceSuggester.TryDerive(missing).Should().BeNull();
    }

    /// <summary>空・空白のパスは導出不能（null）</summary>
    [Theory(DisplayName = "空・空白のパス → null")]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespacePath_ReturnsNull(string path)
    {
        OutputFolderNamespaceSuggester.TryDerive(path).Should().BeNull();
    }

    /// <summary>同一ディレクトリに csproj が複数あるときは、名前順で最初の 1 つを使う</summary>
    [Fact(DisplayName = "csproj が複数 → 名前順で最初の 1 つを使う")]
    public void MultipleCsproj_UsesFirstByName()
    {
        var root = CreateTempRoot();

        try
        {
            // 名前順で "Alpha" が先。Beta の RootNamespace は選ばれない
            WriteCsproj(root, "Beta.csproj", rootNamespace: "BetaNamespace");
            WriteCsproj(root, "Alpha.csproj", rootNamespace: "AlphaNamespace");

            OutputFolderNamespaceSuggester.TryDerive(root).Should().Be("AlphaNamespace");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
