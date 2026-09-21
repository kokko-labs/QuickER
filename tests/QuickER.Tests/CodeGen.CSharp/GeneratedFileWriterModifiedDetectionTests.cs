using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 上書き前の手編集検出（<see cref="GeneratedFileWriter.FindModifiedFiles"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 実経路で生成・書き出ししたファイルをディスク上で書き換え、列挙される／されないの境界を固定する。
/// 正規化規則そのものの網羅は <see cref="GeneratedContentHashTests"/> が持ち、ここはライターが
/// 「どのファイルを照合し、どのパスで報告するか」だけを見る。
/// </remarks>
public sealed class GeneratedFileWriterModifiedDetectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "QuickER.Tests",
        nameof(GeneratedFileWriterModifiedDetectionTests),
        Guid.NewGuid().ToString("N")
    );

    private readonly GeneratedFileWriter _writer = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Order エンティティ 1 つの最小図を作る</summary>
    private static ErDiagram CreateDiagram()
    {
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });

        return new ErDiagram { Entities = { order } };
    }

    /// <summary>実経路（SqlServer プロバイダ）で生成する</summary>
    private static CodeGenerationResult Generate(CodeGenerationOptions options)
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            CreateDiagram(),
            options
        );

        result.HasErrors.Should().BeFalse();
        return result;
    }

    private static readonly CodeGenerationOptions SplitWithDocs = new()
    {
        GenerateRepositories = true,
        SplitFilesByCategory = true,
        GenerateApiDocs = true,
    };

    [Fact(DisplayName = "検出: 書き出した直後はどのファイルも編集済みとみなさない")]
    public void FreshlyWritten_ReportsNothing()
    {
        var result = Generate(SplitWithDocs);
        _writer.WriteFiles(_directory, result);

        _writer.FindModifiedFiles(_directory, result).Should().BeEmpty();
    }

    [Fact(DisplayName = "検出: 出力先にまだファイルが無ければ何も報告しない")]
    public void NothingOnDisk_ReportsNothing()
    {
        _writer.FindModifiedFiles(_directory, Generate(SplitWithDocs)).Should().BeEmpty();
        Directory.Exists(_directory).Should().BeFalse("照合はディスクへ何も書かない");
    }

    [Fact(
        DisplayName = "検出: 本文を手で編集した .g.cs だけを、書き出し先と同じ絶対パスで報告する"
    )]
    public void EditedCSharpFile_IsReportedWithWrittenPath()
    {
        var result = Generate(SplitWithDocs);
        var written = _writer.WriteFiles(_directory, result);
        var target = written.Single(path => path.EndsWith("Entities.g.cs"));

        File.WriteAllText(target, File.ReadAllText(target).Replace("OrderId", "OrderKey"));

        _writer.FindModifiedFiles(_directory, result).Should().Equal(target);
    }

    [Fact(DisplayName = "検出: 改行コードの変換・インデントの変更だけでは報告しない")]
    public void ReformattedFile_IsNotReported()
    {
        var result = Generate(SplitWithDocs);
        var written = _writer.WriteFiles(_directory, result);

        foreach (var path in written.Where(path => path.EndsWith(".g.cs")))
        {
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace("\r\n", "\n").Replace("    ", "\t")
            );
        }

        _writer.FindModifiedFiles(_directory, result).Should().BeEmpty();
    }

    [Fact(DisplayName = "検出: 内容ハッシュの行が無いファイルは判定せず報告しない")]
    public void FileWithoutHashLine_IsNotReported()
    {
        var result = Generate(SplitWithDocs);
        var written = _writer.WriteFiles(_directory, result);
        var target = written.Single(path => path.EndsWith("Entities.g.cs"));

        // この機能より前の版の生成物に相当する（ハッシュ行を消したうえで本文も編集されている）
        var withoutHash = string.Join(
            "\r\n",
            File.ReadAllText(target)
                .Split("\r\n")
                .Where(line => !line.StartsWith(GeneratedContentHash.HashLinePrefix))
        );
        File.WriteAllText(target, withoutHash.Replace("OrderId", "OrderKey"));

        _writer.FindModifiedFiles(_directory, result).Should().BeEmpty();
    }

    [Fact(DisplayName = "検出: API リファレンス（.g.md）は編集されていても対象外")]
    public void EditedMarkdown_IsNotReported()
    {
        var result = Generate(SplitWithDocs);
        var written = _writer.WriteFiles(_directory, result);
        var markdown = written.Single(path => path.EndsWith(".g.md"));

        File.AppendAllText(markdown, "\nhand-written note\n");

        _writer.FindModifiedFiles(_directory, result).Should().BeEmpty();
    }

    [Fact(DisplayName = "検出: 層別出力では層フォルダ内のファイルを照合する")]
    public void LayeredOutput_ChecksFilesInLayerDirectories()
    {
        var result = Generate(
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = true,
                GenerateRepositories = true,
                CodeSubdirectory = "Generated",
            }
        );
        var written = _writer.WriteFiles(_directory, result);
        var target = written.Single(path => path.EndsWith("Entities.g.cs"));

        target.Should().Be(Path.Combine(_directory, "Domain", "Generated", "Entities.g.cs"));
        File.AppendAllText(target, "// hand-written\r\n");

        _writer.FindModifiedFiles(_directory, result).Should().Equal(target);
    }

    [Fact(DisplayName = "検出: 書き出しと同じ検証で、生成ファイル以外の書き出し先を拒否する")]
    public void NonGeneratedFileName_IsRejected()
    {
        var result = new CodeGenerationResult
        {
            Files = [new GeneratedFile { FileName = "Program.cs", Content = "// x" }],
        };

        var act = () => _writer.FindModifiedFiles(_directory, result);

        act.Should().Throw<InvalidOperationException>();
    }
}
