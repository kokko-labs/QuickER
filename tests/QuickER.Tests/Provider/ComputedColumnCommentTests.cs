using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>計算列の注意コメント（<see cref="ComputedColumnComment"/>）の、出力からは観測できない防壁を検証するテストクラス</summary>
/// <remarks>
/// DDL・同期スクリプトに出る注意コメントの有無・位置・文面は、方言の DDL の表（<c>DialectDdlRows</c> の
/// <c>ddl-computed-column-*</c>・<c>sync-*-computed-column-*</c>）が固定する。
/// </remarks>
public class ComputedColumnCommentTests
{
    /// <summary>コメントへ載せる列名は制御文字を畳んでからでないと行を突き破ることを検証する</summary>
    /// <remarks>
    /// 名前は入口（<see cref="SqlNameText"/>）でも拒否されるが、この文面自体が
    /// <see cref="SqlComment.Sanitize"/> を通っていることをヘルパー直呼びで固定する
    /// （入口ゲートがあるとこの防壁は出力側から観測できないため）。
    /// </remarks>
    [Fact(DisplayName = "注意コメントの列名は改行を空白へ畳む")]
    public void Note_ColumnNameWithNewline_IsFoldedToOneLine()
    {
        var note = ComputedColumnComment.Build(
            new Column
            {
                Name = "a\nDROP TABLE users; --",
                DataType = "int",
                IsComputed = true,
            }
        );

        note.Should().NotBeNull();
        note.Should().NotContain("\n").And.NotContain("\r");
        note.Should().Contain("a DROP TABLE users; --");
    }
}
