using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Dialects;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Dialects;

/// <summary>
/// 方言名の文字列が、方言の宣言のプロジェクト（<c>QuickER.Dialects</c>）の外に現れないことを固定する（規則 3）
/// </summary>
/// <remarks>
/// <para>
/// 機能の処理は方言名で分岐せず、違いは方言の宣言に問い合わせる（規則 1）。方言名の文字列が機能の側に現れるのは、
/// 方言名で分岐しているか、方言名を写しているかのどちらかなので、ここで名指しする。
/// </para>
/// <para>
/// 拾う基準：<c>src/</c> の <c>QuickER.Dialects</c> 以外の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）にある文字列リテラルのうち、
/// 値が 5 つの方言名（<see cref="DialectNames.All"/>）のどれかと、大文字小文字を無視して<b>完全に一致</b>するもの。
/// 部分一致は拾わない（<c>"SqliteConnection"</c> や <c>"mysql.data"</c> のような語は方言名ではない）。前後の空白も除かない。
/// 構文木で数えるので、コメント・XmlDoc の中の言及は数えない。補間文字列の中の文字列は拾わない（方言名だけの補間文字列は書かない）。
/// </para>
/// <para>
/// 正当な出現は理由つきで <see cref="Known"/> に載せる。照合は厳密で、一覧に無い出現も、一覧にあるのに無い出現（件数の食い違いを含む）も赤にする。
/// 「後の区切りで空にする」に分類したものは、方言の宣言へ置き換えた区切りで一覧から消す。
/// </para>
/// </remarks>
public sealed class DialectNameLiteralGuardTests
{
    /// <summary>後の区切りで空にする：C# 生成の Repository 方言（sqlserver / sqlite）で分岐している</summary>
    private const string CodeGenBranch =
        "C# 生成の Repository 方言で分岐している（生成の方言の宣言へ置き換える区切りで消す）";

    /// <summary>後の区切りで空にする：DB 接続ダイアログが方言名で分岐している</summary>
    private const string ConnectionDialogBranch =
        "DB 接続ダイアログが方言名で表示を出し分けている（接続の設定項目の宣言へ置き換える区切りで消す）";

    /// <summary>既定の方言</summary>
    private const string DefaultDialect =
        "既定の方言（図・設定に方言が無いとき・解決できないときの SQL Server）";

    /// <summary>C# 生成の既定・対応一覧と分岐が同じファイルにある</summary>
    private const string CodeGenDefaultAndBranch =
        "C# 生成の Repository 方言の既定・対応一覧と分岐（分岐は生成の方言の宣言へ置き換える区切りで消す）";

    /// <summary>利用者に見せる表示名</summary>
    private const string ShownName = "利用者に見せる方言の表示名";

    /// <summary>生成物の名前空間の接尾辞</summary>
    private const string NamespaceSuffix =
        "生成物の方言別の名前空間の接尾辞（パッケージ名の接尾辞と同じ綴り）";

    /// <summary>永続化した値・設定の許可値</summary>
    private const string PersistedValue =
        "図ファイル・設定ファイルに保存する方言の値（名前付きクエリの生 SQL のキー・quicker.json の許可値と例）";

    /// <summary>ランタイムパッケージ用ソースの描画</summary>
    private const string RuntimePackageRendering =
        "ランタイムパッケージ用ソースは方言を固定して描画する（パッケージ 1 本＝方言 1 つ）";

    /// <summary>生成ランタイム</summary>
    private const string GeneratedRuntime =
        "依存ゼロの生成ランタイムは QuickER.Dialects を参照できない（EF Core のプロバイダ名の部分を見て LIKE のエスケープを決める）";

    /// <summary>既知の出現（ファイル・リテラルの値〔綴りどおり〕・件数・理由）</summary>
    private static readonly (string File, string Value, int Count, string Reason)[] Known =
    [
        ("src/QuickER.AI.Mock/MockProjectScaffoldService.cs", "sqlserver", 1, DefaultDialect),
        ("src/QuickER.AI.Mock/MockProjectTargetProfile.cs", "sqlite", 2, CodeGenBranch),
        ("src/QuickER.AI.Mock/MockProjectTargetProfile.cs", "sqlserver", 2, CodeGenBranch),
        ("src/QuickER.Cli/GenerationConfigSchema.cs", "sqlite", 2, PersistedValue),
        ("src/QuickER.Cli/GenerationConfigSchema.cs", "sqlserver", 1, PersistedValue),
        ("src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs", "sqlite", 1, CodeGenBranch),
        (
            "src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs",
            "sqlserver",
            1,
            CodeGenBranch
        ),
        (
            "src/QuickER.CodeGen.CSharp/CodeGenerationOptions.cs",
            "sqlite",
            1,
            CodeGenDefaultAndBranch
        ),
        (
            "src/QuickER.CodeGen.CSharp/CodeGenerationOptions.cs",
            "sqlserver",
            4,
            CodeGenDefaultAndBranch
        ),
        ("src/QuickER.CodeGen.CSharp/GeneratedFilePlanner.cs", "SqlServer", 1, NamespaceSuffix),
        ("src/QuickER.CodeGen.CSharp/GeneratedFilePlanner.cs", "Sqlite", 1, NamespaceSuffix),
        ("src/QuickER.CodeGen.CSharp/GeneratedFilePlanner.cs", "sqlite", 1, CodeGenBranch),
        ("src/QuickER.CodeGen.CSharp/GeneratedFileUsings.cs", "sqlite", 1, CodeGenBranch),
        ("src/QuickER.CodeGen.CSharp/MultiDialectTypeReconciler.cs", "sqlserver", 1, CodeGenBranch),
        (
            "src/QuickER.CodeGen.CSharp/RuntimePackageSourceRenderer.cs",
            "sqlite",
            1,
            RuntimePackageRendering
        ),
        (
            "src/QuickER.CodeGen.CSharp/RuntimePackageSourceRenderer.cs",
            "sqlserver",
            11,
            RuntimePackageRendering
        ),
        ("src/QuickER.CodeGen.CSharp/RuntimePackages.cs", "sqlite", 1, CodeGenBranch),
        ("src/QuickER.CodeGen.CSharp/ScribanCSharpRenderer.cs", "SQLite", 1, ShownName),
        ("src/QuickER.CodeGen.CSharp/ScribanCSharpRenderer.cs", "sqlite", 2, CodeGenBranch),
        (
            "src/QuickER.CodeGen.CSharp/ScribanCSharpRenderer.cs",
            "sqlserver",
            2,
            CodeGenDefaultAndBranch
        ),
        ("src/QuickER.CodeGen.UI/QueryItemViewModel.cs", "sqlite", 3, PersistedValue),
        ("src/QuickER.CodeGen.UI/QueryItemViewModel.cs", "sqlserver", 3, PersistedValue),
        ("src/QuickER.Db.UI/DbConnectionDialogViewModel.cs", "mysql", 1, ConnectionDialogBranch),
        ("src/QuickER.Db.UI/DbConnectionDialogViewModel.cs", "oracle", 1, ConnectionDialogBranch),
        (
            "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
            "postgresql",
            1,
            ConnectionDialogBranch
        ),
        ("src/QuickER.Db.UI/DbConnectionDialogViewModel.cs", "sqlserver", 1, DefaultDialect),
        ("src/QuickER.Db.UI/IDbConnectionDialogPresenter.cs", "sqlserver", 1, DefaultDialect),
        ("src/QuickER.Gui/ViewModels/MainViewModel.cs", "sqlserver", 1, DefaultDialect),
        ("src/QuickER.Provider.MySql/MySqlProvider.cs", "MySQL", 1, ShownName),
        ("src/QuickER.Provider.Oracle/OracleProvider.cs", "Oracle", 1, ShownName),
        ("src/QuickER.Provider.PostgreSql/PostgreSqlProvider.cs", "PostgreSQL", 1, ShownName),
        ("src/QuickER.Provider.Sqlite/SqliteProvider.cs", "SQLite", 1, ShownName),
        (
            "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
            "MySql",
            1,
            GeneratedRuntime
        ),
        (
            "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
            "Oracle",
            1,
            GeneratedRuntime
        ),
        (
            "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
            "SqlServer",
            1,
            GeneratedRuntime
        ),
        (
            "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
            "Sqlite",
            1,
            GeneratedRuntime
        ),
    ];

    [Fact(
        DisplayName = "方言名と完全に一致する文字列リテラルは、QuickER.Dialects の外では理由つきの一覧にあるものだけ"
    )]
    public void DialectNameLiterals_OutsideDialects_AreOnlyKnown()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var dialects = Path.Combine(src, "QuickER.Dialects") + Path.DirectorySeparatorChar;
        var found = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(dialects, StringComparison.OrdinalIgnoreCase))
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")
            )
            .SelectMany(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var tree = CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    cancellationToken: TestContext.Current.CancellationToken
                );

                return tree.GetRoot(TestContext.Current.CancellationToken)
                    .DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(literal => literal.Token.ValueText)
                    .Where(IsDialectName)
                    .Select(value => (File: relative, Value: value));
            })
            .GroupBy(hit => hit)
            .ToDictionary(group => group.Key, group => group.Count());

        var expected = Known.ToDictionary(known => (known.File, known.Value), known => known.Count);

        var unexpected = found
            .Where(pair => !expected.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: \"{pair.Key.Value}\" x{pair.Value}")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        var missing = expected
            .Where(pair => !found.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: \"{pair.Key.Value}\" x{pair.Value}")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, unexpected)
            .Should()
            .BeEmpty(
                "方言名の文字列は QuickER.Dialects に置き、機能は方言の宣言に問い合わせる（規則 1・3）"
            );
        string.Join(Environment.NewLine, missing)
            .Should()
            .BeEmpty("既知の一覧は実際の出現と厳密に一致させる（直したら一覧から消す）");
    }

    [Fact(DisplayName = "既知の一覧の各行は理由を持ち、同じファイル・値を 2 度載せない")]
    public void Known_HasReasonsAndNoDuplicates()
    {
        Known.Should().OnlyContain(known => !string.IsNullOrWhiteSpace(known.Reason));
        Known.Select(known => (known.File, known.Value)).Should().OnlyHaveUniqueItems();
    }

    /// <summary>値が方言名と大文字小文字を無視して完全に一致するか（部分一致・前後の空白は一致としない）</summary>
    private static bool IsDialectName(string value) =>
        DialectNames.All.Any(name =>
            string.Equals(name, value, StringComparison.OrdinalIgnoreCase)
        );
}
