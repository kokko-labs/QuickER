using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML のキーワードと同じ名前を持つ列（<c>note</c> / <c>records</c> / <c>checks</c>）の往復を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 取込はテーブルブロック内の行を、未対応ブロック（<c>checks</c> / <c>records</c>）と複数行 <c>Note</c> の
/// 開始行かどうかで振り分ける。ここをキーワードだけで判定すると、<c>records int</c> という<b>列</b>が
/// ブロックとみなされて無言で消え、<c>note varchar</c> は「未対応の行」として取込全体を失敗させる。
/// </para>
/// <para>
/// エクスポートは列名を素の識別子で書く（これらの名前は引用符を必要としない）ので、
/// 判定を誤ると<b>自分が書き出したファイルを読み戻せない</b>。往復で固定する。
/// </para>
/// </remarks>
public class DbmlKeywordColumnNameTests
{
    /// <summary>DBML のキーワードと同じ名前の列を並べた図</summary>
    private static ErDiagram BuildDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Audit",
                    Columns =
                    {
                        new Column
                        {
                            Name = "AuditId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "note",
                            DataType = "varchar(200)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Name = "records",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "checks",
                            DataType = "int",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    /// <summary>キーワードと同じ名前の列が、書き出して読み戻しても保たれることを検証する</summary>
    [Fact(DisplayName = "DBML: キーワードと同じ名前の列も往復できる")]
    public void SaveAndLoad_ColumnsNamedLikeKeywords_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.dbml");

        try
        {
            DbmlExporter.SaveTo(BuildDiagram(), path);

            var loaded = DbmlImporter.Load(path);
            var columns = loaded.Entities.Should().ContainSingle().Subject.Columns;

            columns
                .Select(column => column.Name)
                .Should()
                .Equal("AuditId", "note", "records", "checks");
            columns
                .Select(column => column.DataType)
                .Should()
                .Equal("int", "varchar(200)", "int", "int");
            columns.Select(column => column.IsNullable).Should().Equal(false, true, false, true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>1 行で閉じるブロックも列にならないことを検証する</summary>
    /// <remarks>
    /// 判定を「行が <c>{</c> で終わる」にすると、この形を取りこぼして <c>checks</c> という幻の列ができる。
    /// </remarks>
    [Theory(DisplayName = "DBML: 1 行で閉じる checks / records ブロックは列にならない")]
    [InlineData("checks { `price > 0` }")]
    [InlineData("records { 1 }")]
    [InlineData("records t(id) { 1 }")]
    public void Load_SingleLineSkippableBlock_IsNotReadAsColumn(string block)
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.dbml");

        try
        {
            File.WriteAllText(
                path,
                $"Table Audit {{{Environment.NewLine}  AuditId int [pk]{Environment.NewLine}  {block}{Environment.NewLine}}}{Environment.NewLine}"
            );

            var loaded = DbmlImporter.Load(path);

            loaded
                .Entities.Should()
                .ContainSingle()
                .Subject.Columns.Select(column => column.Name)
                .Should()
                .Equal(["AuditId"], "ブロックは読み飛ばし、幻の列を作らない");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
