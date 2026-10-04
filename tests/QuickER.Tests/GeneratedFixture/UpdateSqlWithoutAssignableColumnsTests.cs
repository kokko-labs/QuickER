using AwesomeAssertions;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using Xunit;
using SqliteMetadata = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.Sqlite.EntitySaveMetadata;
using SqlServerMetadata = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.SqlServer.EntitySaveMetadata;

namespace QuickER.Tests.GeneratedTranslatorParityFixture;

/// <summary>
/// 更新できる列が 1 本も無い表の UPDATE 文が、空の SET でなくキー列の自己代入になることを固定する単体テスト。
/// </summary>
/// <remarks>
/// 実 DB での更新・版の照合は <c>TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests</c> が固定する。
/// </remarks>
public sealed class UpdateSqlWithoutAssignableColumnsTests
{
    [Fact(DisplayName = "キー列だけの表の UPDATE はキー列を自分自身へ代入する（両方言）")]
    public void KeyOnlyTable_AssignsTheKeyToItself()
    {
        SqlServerMetadata
            .For(typeof(TagEntity))
            .UpdateSql.Should()
            .Be("UPDATE [tags] SET [name] = [name] WHERE [name] = @id;");
        SqliteMetadata
            .For(typeof(TagEntity))
            .UpdateSql.Should()
            .Be("UPDATE \"tags\" SET \"name\" = \"name\" WHERE \"name\" = @id;");
    }

    [Fact(
        DisplayName = "SQL Server の版つきの表は、キー列の自己代入のまま版の照合と OUTPUT を保つ"
    )]
    public void VersionOnlyTable_KeepsTheGuardAndOutput()
    {
        SqlServerMetadata
            .For(typeof(VersionedTagEntity))
            .UpdateVersionedSql.Should()
            .Be(
                "UPDATE [versioned_tags] SET [name] = [name] OUTPUT INSERTED.[row_ver] WHERE [name] = @id AND [row_ver] = @originalRowVersion;"
            );
    }

    [Fact(DisplayName = "更新できる列がある表の UPDATE は従来どおりその列だけを代入する")]
    public void TableWithAssignableColumns_IsUnchanged()
    {
        SqlServerMetadata
            .For(typeof(OrderEntity))
            .UpdateSql.Should()
            .Be("UPDATE [order] SET [group] = @Group WHERE [order_id] = @id;");
    }
}
