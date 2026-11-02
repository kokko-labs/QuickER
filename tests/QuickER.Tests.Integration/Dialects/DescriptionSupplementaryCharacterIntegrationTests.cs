using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 説明の BMP の外の文字（絵文字など）を含む図の DDL が、方言の宣言（<see cref="IdentifierLengthLimit.RepresentsSupplementaryCharacters"/>）
/// どおりに注記することを 5 方言で確かめる（DDL の文字列だけ・DB を使わない）
/// </summary>
/// <remarks>
/// DDL を当てて取り込み直した説明が方言の保存する形になり差分が出ないこと・説明を足す同期の警告は、同期計画の期待の行
/// <c>realdb-description-supplementary-*</c>（<see cref="SyncPlanExpectationRows"/>）が実 DB で確かめる。
/// 単体の対応は <c>DescriptionStoredTextTests</c>。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DescriptionSupplementaryCharacterDdlNoteTests
{
    [Theory(DisplayName = "[Integration] 説明の BMP の外の文字: DDL の注記は宣言どおり")]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.SqlServer, false)]
    [InlineData(DialectNames.PostgreSql, true)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, true)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, true)]
    [InlineData(DialectNames.Oracle, false)]
    [InlineData(DialectNames.Sqlite, true)]
    [InlineData(DialectNames.Sqlite, false)]
    public void DdlNote_FollowsDeclaration(string dialect, bool withEmoji)
    {
        var provider = SyncPlanExpectationCapabilities.Provider(dialect);
        var description = RealCrossDialectScenarios.SupplementaryDescription(withEmoji);
        var type = RealCrossDialectScenarios.IntType(dialect);
        var diagram = new ErDiagram { TargetDbms = provider.Name };
        diagram.Entities.Add(
            new Entity
            {
                TableName = "t",
                Description = description,
                Columns =
                {
                    new Column
                    {
                        Name = "id",
                        DataType = type,
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "v",
                        DataType = type,
                        IsNullable = true,
                        Description = description,
                    },
                },
            }
        );
        var ddl = provider.DdlGenerator.Build(diagram);

        var folds =
            withEmoji
            && !IdentifierLengthLimits.ForDbms(provider.Name).RepresentsSupplementaryCharacters;
        ddl.Split('\n')
            .Count(l => l.Contains("Basic Multilingual Plane", StringComparison.Ordinal))
            .Should()
            .Be(folds ? 2 : 0, ddl);
    }
}
