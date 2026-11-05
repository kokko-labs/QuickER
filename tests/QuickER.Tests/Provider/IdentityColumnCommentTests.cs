using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>DB が採番する列の注記（<see cref="IdentityColumnComment"/>）の、出力からは観測できない防壁を検証するテストクラス</summary>
/// <remarks>
/// DDL・同期スクリプトに出る注記の有無・位置・文面と、注記する方言が扱いの表と一致することは、方言の DDL の表
/// （<c>DialectDdlRows</c> の <c>ddl-identity-column-*</c>・<c>sync-*-identity-column-*</c> と Treats の自動の照合）が固定する。
/// </remarks>
public class IdentityColumnCommentTests
{
    /// <summary>コメントへ載せる列名は制御文字を畳んでからでないと行を突き破ることを検証する（入口の検証が先に断るので出力からは観測できない）</summary>
    [Fact(DisplayName = "注記の列名は改行を空白へ畳む")]
    public void Note_ColumnNameWithNewline_IsFoldedToOneLine()
    {
        var note = IdentityColumnComment.Build(
            new Column
            {
                Name = "a\nDROP TABLE users; --",
                DataType = "int",
                IsIdentity = true,
            },
            DialectNames.Oracle,
            UnsupportedFeature.Sync
        );

        note.Should().NotBeNull();
        note.Should().NotContain("\n").And.NotContain("\r");
        note.Should().Contain("a DROP TABLE users; --");
    }
}
