using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Tests.Document;

/// <summary>
/// 主キーの順序（<see cref="Entity.PrimaryKeyColumnIds"/>）の図ファイル JSON 往復と、旧形式 JSON との互換を検証するテストクラス
/// </summary>
/// <remarks>
/// <see cref="DiagramDocument.Version"/> は据え置き（1 のまま）で、旧形式 JSON は
/// プロパティ欠落＝空リスト（＝列宣言順）として読める後方互換を保つ。
/// </remarks>
public class PrimaryKeyOrderPersistenceTests
{
    /// <summary>一時ファイルのパスを作る</summary>
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"er-pk-{Guid.NewGuid()}.json");

    /// <summary>主キーの順序が列宣言順と異なっても保存→読込で往復することを検証する</summary>
    [Fact(DisplayName = "Save → Load で主キーの順序が往復する")]
    public void SaveAndLoad_RoundTripsPrimaryKeyColumnIds()
    {
        var a = new Column
        {
            Name = "a",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var b = new Column
        {
            Name = "b",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var entity = new Entity
        {
            TableName = "Pair",
            Columns = { a, b },
            // 列宣言順（a → b）とは逆の主キー順
            PrimaryKeyColumnIds = [b.Id, a.Id],
        };

        var document = new DiagramDocument { Schema = new ErDiagram { Entities = { entity } } };
        var path = TempPath();

        try
        {
            JsonStorageService.Save(path, document);
            var loaded = JsonStorageService.Load(path);

            var loadedEntity = loaded.Schema.Entities.Should().ContainSingle().Which;
            loadedEntity.PrimaryKeyColumnIds.Should().Equal(b.Id, a.Id);
            loadedEntity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("b", "a");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>PrimaryKeyColumnIds を持たない旧形式 JSON が空リスト（＝列宣言順）として読めることを検証する</summary>
    [Fact(DisplayName = "旧形式 JSON（PrimaryKeyColumnIds なし）は空リストとして読める")]
    public void Load_LegacyJsonWithoutProperty_YieldsEmptyList()
    {
        var path = TempPath();
        var json = """
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  {
                    "Id": "11111111-1111-1111-1111-111111111111",
                    "TableName": "Pair",
                    "Columns": [
                      { "Id": "22222222-2222-2222-2222-222222222222", "Name": "a", "DataType": "int", "IsPrimaryKey": true },
                      { "Id": "33333333-3333-3333-3333-333333333333", "Name": "b", "DataType": "int", "IsPrimaryKey": true }
                    ]
                  }
                ],
                "Relationships": []
              }
            }
            """;

        try
        {
            File.WriteAllText(path, json);

            var loaded = JsonStorageService.Load(path);

            var entity = loaded.Schema.Entities.Should().ContainSingle().Which;
            entity.PrimaryKeyColumnIds.Should().NotBeNull();
            entity.PrimaryKeyColumnIds.Should().BeEmpty();
            // 順序指定が無いので列宣言順がそのまま実効順になる
            entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("a", "b");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>PrimaryKeyColumnIds に明示的な null が書かれた JSON も空リストへ修復されることを検証する</summary>
    [Fact(DisplayName = "明示的な null の PrimaryKeyColumnIds は空リストへ修復される")]
    public void Load_ExplicitNull_IsNormalized()
    {
        var path = TempPath();
        var json = """
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  {
                    "Id": "11111111-1111-1111-1111-111111111111",
                    "TableName": "A",
                    "Columns": [],
                    "PrimaryKeyColumnIds": null
                  }
                ],
                "Relationships": []
              }
            }
            """;

        try
        {
            File.WriteAllText(path, json);

            var loaded = JsonStorageService.Load(path);

            loaded
                .Schema.Entities.Should()
                .ContainSingle()
                .Which.PrimaryKeyColumnIds.Should()
                .BeEmpty();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
