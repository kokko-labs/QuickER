using System.IO;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 図の取込（DBML・Mermaid・Excel 定義書）へ名前の総当たり（<see cref="NameFuzz"/>）を流し、重複の判定が
/// <see cref="NameIdentityRule"/> と同じ答えになることを確かめるテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 取込が読むテキスト・ブックは、名前の組を持つ図を同じ形式のエクスポータで書き出して作る（形式の引用・
/// エスケープの規則を書き写さないため）。規則が「同じ名前」と言う組は取込が断ること、「別の名前」と言う組は
/// 2 つとも書いた名前のまま取り込むことを表明する。
/// </para>
/// <para>
/// 1 つだけでも書き出しと取込で名前が変わる名前（Mermaid が畳む記号など＝形式の欠落として告知済み）は、
/// 重複の判定とは別の理由なので組ごと流さない。結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と
/// 厳密に照合する。
/// </para>
/// </remarks>
public sealed class NameIdentityImportFuzzTests
{
    /// <summary>取込の形式</summary>
    public enum Format
    {
        /// <summary>DBML</summary>
        Dbml,

        /// <summary>Mermaid</summary>
        Mermaid,

        /// <summary>Excel 定義書</summary>
        Excel,
    }

    [Theory(DisplayName = "図の取込の重複の判定は名前の規則と同じ答えになる")]
    [InlineData(Format.Dbml)]
    [InlineData(Format.Mermaid)]
    [InlineData(Format.Excel)]
    public void Import_AgreesWithNameRule(Format format)
    {
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);
        var prefix = $"import:{format.ToString().ToLowerInvariant()}:";

        NameFuzzRunner.Run(
            ledger,
            prefix + "table",
            NameKind.Table,
            name => RoundTripTables(format, name).Accepted,
            pair => RoundTripTables(format, pair.First, pair.Second).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            prefix + "column",
            NameKind.Column,
            name => RoundTripColumns(format, name).Accepted,
            pair => RoundTripColumns(format, pair.First, pair.Second).Outcome
        );

        ledger.AssertMatchesKnown(prefix);
    }

    /// <summary>名前ごとにテーブルを 1 つ持つ図を書き出して取り込む</summary>
    private static (bool Accepted, PairOutcome Outcome) RoundTripTables(
        Format format,
        params string[] names
    )
    {
        var diagram = new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities = names
                .Select(name => new Entity
                {
                    TableName = name,
                    Columns =
                    [
                        new Column
                        {
                            Name = "Id",
                            DataType = "INTEGER",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                })
                .ToList(),
        };

        return RoundTrip(
            format,
            diagram,
            names.Select(name => name.Trim()).ToList(),
            imported => imported.Entities.Select(entity => entity.TableName).ToList()
        );
    }

    /// <summary>テーブル T に名前ごとの列を持つ図を書き出して取り込む</summary>
    private static (bool Accepted, PairOutcome Outcome) RoundTripColumns(
        Format format,
        params string[] names
    )
    {
        var diagram = new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities =
            [
                new Entity
                {
                    TableName = "T",
                    Columns = names
                        .Select(name => new Column { Name = name, DataType = "INTEGER" })
                        .ToList(),
                },
            ],
        };

        return RoundTrip(
            format,
            diagram,
            names,
            imported =>
                imported.Entities.SelectMany(entity => entity.Columns).Select(c => c.Name).ToList()
        );
    }

    /// <summary>
    /// 図を書き出して取り込み、取り込んだ名前が書いた名前のとおりか（組なら 2 つめを別の要素として受け付けたか）を返す
    /// </summary>
    private static (bool Accepted, PairOutcome Outcome) RoundTrip(
        Format format,
        ErDiagram diagram,
        IReadOnlyList<string> expectedNames,
        Func<ErDiagram, List<string>> namesOf
    )
    {
        ErDiagram imported;

        try
        {
            imported = Import(format, diagram);
        }
        catch (Exception ex)
        {
            // 断るときは形式の誤りとして InvalidDataException で名指しする（ほかの例外は利用者に原因が伝わらない）
            var failure = $"{ex.GetType().Name}: {ex.Message}";

            return ex is InvalidDataException
                ? (false, PairOutcome.Of(false, failure))
                : (false, new PairOutcome(false, ["unexpected-exception"], failure));
        }

        var names = namesOf(imported);

        return NameFuzzRunner.JudgeImported(
            names,
            expectedNames,
            "[" + string.Join(", ", names.Select(NameFuzz.Describe)) + "]"
        );
    }

    private static ErDiagram Import(Format format, ErDiagram diagram)
    {
        switch (format)
        {
            case Format.Dbml:
                return DbmlImporter.Parse(DbmlExporter.Build(diagram), "sqlserver");

            case Format.Mermaid:
                return MermaidImporter.Parse(MermaidExporter.Build(diagram), "sqlserver");

            default:
                using (var workbook = TableDefinitionDocumentExporter.BuildWorkbook(diagram))
                {
                    return TableDefinitionDocumentImporter.Load(workbook, "sqlserver");
                }
        }
    }
}
