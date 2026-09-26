using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML 取込が、解釈できない行を黙って捨てず、かつ正当な DBML を誤って拒否しないことを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 従来はトップレベルの解釈できない行をすべて黙って読み飛ばしており、書式に一致しなかった
/// <c>Table</c> 行ごとテーブルが消えても気づけなかった。解釈できない行は行番号つきで拒否する。
/// </para>
/// <para>
/// 拒否を導入すると、これまで読めていた他ツール製の DBML を落としかねない。
/// QuickER の意味モデルが表せない情報だけを持つブロック（<c>Project</c> / <c>Enum</c> /
/// <c>TableGroup</c> / <c>TablePartial</c> / <c>Note</c> / <c>records</c>）は読み飛ばす。
/// リレーションを運ぶ <c>Ref</c> だけは読み飛ばさず、未対応の記法として名指しで拒否する。
/// </para>
/// </remarks>
public class DbmlUnsupportedSyntaxTests
{
    /// <summary>最小のテーブル定義（各ケースへ前後に足して使う）</summary>
    private const string MinimalTable = "Table t {\n  id int [pk, not null]\n}";

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    // ---------------- 読み飛ばすブロック ----------------

    [Theory(DisplayName = "DBML 取込: QuickER が扱わないトップレベルブロックは読み飛ばす")]
    [InlineData("Project my_project {\n  database_type: 'PostgreSQL'\n  Note: 'desc'\n}")]
    [InlineData("enum job_status {\n  created\n  running\n}")]
    [InlineData("TableGroup g {\n  t\n}")]
    [InlineData("TablePartial base {\n  created_at timestamp\n}")]
    [InlineData("Note {\n  'project level note'\n}")]
    [InlineData("records t(id) {\n  1\n}")]
    public void Import_SkippableTopLevelBlocks_AreIgnored(string block)
    {
        var diagram = DbmlImporter.Parse(Lines(block, string.Empty, MinimalTable));

        diagram.Entities.Should().ContainSingle().Which.TableName.Should().Be("t");
    }

    [Theory(DisplayName = "DBML 取込: テーブル内の未対応ブロックは幻のカラムを作らない")]
    [InlineData("  checks {\n    `id > 0` [name: 'chk']\n  }")]
    [InlineData("  records {\n    1\n  }")]
    public void Import_SkippableTableBlocks_DoNotCreatePhantomColumns(string block)
    {
        var diagram = DbmlImporter.Parse(Lines("Table t {", "  id int [pk, not null]", block, "}"));

        diagram.Entities.Single().Columns.Select(column => column.Name).Should().Equal("id");
    }

    /// <summary>
    /// 読み飛ばすブロックの波括弧の数え方が、リテラル内の <c>{</c> <c>}</c> で狂わない。
    /// </summary>
    [Fact(DisplayName = "DBML 取込: 読み飛ばしの波括弧はリテラル内の括弧を数えない")]
    public void Import_SkippedBlock_IgnoresBracesInsideLiterals()
    {
        var diagram = DbmlImporter.Parse(
            Lines(
                "Project p {",
                "  Note: 'a { brace } inside'",
                "  database_type: '}'",
                "}",
                string.Empty,
                MinimalTable
            )
        );

        diagram.Entities.Should().ContainSingle().Which.TableName.Should().Be("t");
    }

    /// <summary>複数行リテラル（<c>'''</c>）に含まれる波括弧も数えない。</summary>
    [Fact(DisplayName = "DBML 取込: 読み飛ばしの波括弧は複数行リテラル内の括弧を数えない")]
    public void Import_SkippedBlock_IgnoresBracesInsideTripleQuotedLiterals()
    {
        var diagram = DbmlImporter.Parse(
            Lines(
                "Project p {",
                "  Note: '''",
                "  a { brace",
                "  } inside",
                "  '''",
                "}",
                string.Empty,
                MinimalTable
            )
        );

        diagram.Entities.Should().ContainSingle().Which.TableName.Should().Be("t");
    }

    // ---------------- 拒否する行 ----------------

    [Fact(DisplayName = "DBML 取込: 解釈できないトップレベル行は行番号つきで拒否する")]
    public void Import_UnknownTopLevelLine_ThrowsWithLineNumber()
    {
        var act = () => DbmlImporter.Parse(Lines(MinimalTable, string.Empty, "Gibberish here"));

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    5,
                    string.Format(Strings.Dbml_UnsupportedLine, "Gibberish here")
                )
            );
    }

    [Theory(DisplayName = "DBML 取込: 未対応のリレーション記法は行番号つきで拒否する")]
    [InlineData("Ref fk_name: t.id < t.id")]
    [InlineData("Ref {")]
    [InlineData("Ref fk_name {")]
    public void Import_UnsupportedRelationshipForms_ThrowWithLineNumber(string line)
    {
        var act = () => DbmlImporter.Parse(Lines(MinimalTable, string.Empty, line));

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    5,
                    string.Format(Strings.Dbml_UnsupportedRelationshipForm, line)
                )
            );
    }

    /// <summary>
    /// 書式に一致しない <c>Table</c> 行は、テーブルごと黙って消えるのでなく拒否される（IF2 の本体）。
    /// </summary>
    [Fact(DisplayName = "DBML 取込: 書式に一致しない Table 行はテーブルごと消えずに拒否される")]
    public void Import_MalformedTableHeader_IsRejectedInsteadOfDropped()
    {
        var act = () =>
            DbmlImporter.Parse(
                Lines(MinimalTable, string.Empty, "Table very_long_name as U {", "  id int", "}")
            );

        act.Should().Throw<InvalidDataException>().WithMessage("*Table very_long_name as U {*");
    }

    /// <summary>1 行で閉じない <c>Note</c> はカラムとして解釈せず拒否する。</summary>
    [Fact(DisplayName = "DBML 取込: 複数行 Note は幻のカラムにせず拒否する")]
    public void Import_MultilineNoteInTable_IsRejected()
    {
        var act = () =>
            DbmlImporter.Parse(
                Lines("Table t {", "  id int [pk, not null]", "  Note: '''", "  x", "  '''", "}")
            );

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    3,
                    string.Format(Strings.Dbml_UnsupportedLine, "Note: '''")
                )
            );
    }

    // ---------------- コメント ----------------

    [Fact(
        DisplayName = "DBML 取込: 行コメントはトップレベル・ブロック内・行末のどこでも無視される"
    )]
    public void Import_LineComments_AreIgnored()
    {
        var diagram = DbmlImporter.Parse(
            Lines(
                "// leading comment",
                "Table t { // trailing comment",
                "  // inside the block",
                "  id int [pk, not null] // after the column",
                "}"
            )
        );

        diagram.Entities.Single().Columns.Select(column => column.Name).Should().Equal("id");
    }

    [Fact(DisplayName = "DBML 取込: 複数行コメントはトップレベル・ブロック内・行末で無視される")]
    public void Import_BlockComments_AreIgnored()
    {
        var diagram = DbmlImporter.Parse(
            Lines(
                "/* leading",
                "   spanning lines */",
                "Table t { /* trailing */",
                "  /* inside */",
                "  id int [pk, not null] /* after */",
                "}"
            )
        );

        diagram.Entities.Single().Columns.Select(column => column.Name).Should().Equal("id");
    }

    /// <summary>
    /// リテラルの中の <c>//</c> はコメントとして切り落とさない（説明に URL を書ける）。
    /// </summary>
    [Fact(DisplayName = "DBML 取込: リテラル内の // はコメント扱いしない")]
    public void Import_SlashesInsideLiteral_AreNotComments()
    {
        const string Note = "see https://example.com/docs";

        var diagram = DbmlImporter.Parse(
            Lines("Table t {", $"  id int [pk, not null, note: '{Note}']", "}")
        );

        diagram.Entities.Single().Columns.Single().Description.Should().Be(Note);
    }

    /// <summary>URL を含む説明が書き出し→取込で往復する。</summary>
    [Fact(DisplayName = "DBML 往復: URL を含む説明が保たれる")]
    public void RoundTrip_DescriptionWithUrl_IsPreserved()
    {
        const string Note = "see https://example.com/docs";

        var diagram = new QuickER.Model.ErDiagram
        {
            Entities =
            [
                new QuickER.Model.Entity
                {
                    TableName = "t",
                    Description = Note,
                    Columns =
                    [
                        new QuickER.Model.Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram));

        restored.Entities.Single().Description.Should().Be(Note);
    }

    // ---------------- キーワードの大文字小文字 ----------------

    /// <summary>
    /// DBML のキーワードは大文字小文字を区別しない（dbdiagram.io の記法どおり）。
    /// </summary>
    /// <remarks>
    /// 公式の記法解説は <c>Table</c> / <c>enum</c> / <c>indexes</c> / <c>Ref</c> を綴りを揃えずに書いており、
    /// キーワードは大文字小文字を区別しない。従来は <c>Ref:</c> 行の正規表現だけが区別しており、
    /// 小文字の <c>ref:</c> が「行としては Ref と認識されるのに解析に失敗する」形で落ちていた。
    /// </remarks>
    [Fact(DisplayName = "DBML 取込: キーワードの大文字小文字を区別しない")]
    public void Import_KeywordCasing_IsIgnored()
    {
        var diagram = DbmlImporter.Parse(
            Lines(
                "table parent {",
                "  id int [PK, NOT NULL]",
                "  code int [not null]",
                "  indexes {",
                "    (id, code) [UNIQUE, name: 'uq']",
                "  }",
                "}",
                "table child {",
                "  parent_id int [not null]",
                "}",
                "ref: parent.id < child.parent_id"
            )
        );

        diagram.Entities.Should().HaveCount(2);
        diagram.Relationships.Should().ContainSingle();
        diagram
            .Entities.Single(entity => entity.TableName == "parent")
            .UniqueConstraints.Should()
            .ContainSingle()
            .Which.Name.Should()
            .Be("uq");
    }
}
