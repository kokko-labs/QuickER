using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Tests.Document;

/// <summary>
/// 一意制約（<see cref="UniqueConstraint"/>）の図ファイル JSON 往復と、旧形式 JSON との互換を検証するテストクラス
/// </summary>
/// <remarks>
/// <see cref="DiagramDocument.Version"/> は据え置き（1 のまま）で、旧形式 JSON は
/// プロパティ欠落＝空リストとして読める後方互換を保つ。
/// </remarks>
public class UniqueConstraintPersistenceTests
{
    /// <summary>一時ファイルのパスを作る</summary>
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"er-uq-{Guid.NewGuid()}.json");

    /// <summary>一意制約（名前あり単一列・名前なし複合列）が保存→読込で往復することを検証する</summary>
    [Fact(DisplayName = "Save → Load で一意制約（名前・構成列・宣言順）が往復する")]
    public void SaveAndLoad_RoundTripsUniqueConstraints()
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var region = new Column { Name = "Region", DataType = "nvarchar(10)" };
        var entity = new Entity { TableName = "Shop", Columns = { code, region } };

        var named = new UniqueConstraint { Name = "UQ_Shop_Code", ColumnIds = [code.Id] };
        // 名前なし（SQLite 取込のように自動名しか無いケース）＋複合列
        var unnamed = new UniqueConstraint { Name = null, ColumnIds = [region.Id, code.Id] };
        entity.UniqueConstraints.Add(named);
        entity.UniqueConstraints.Add(unnamed);

        var document = new DiagramDocument { Schema = new ErDiagram { Entities = { entity } } };
        var path = TempPath();

        try
        {
            JsonStorageService.Save(path, document);
            var loaded = JsonStorageService.Load(path);

            var loadedEntity = loaded.Schema.Entities.Should().ContainSingle().Which;
            loadedEntity.UniqueConstraints.Should().HaveCount(2);

            var loadedNamed = loadedEntity.UniqueConstraints[0];
            loadedNamed.Id.Should().Be(named.Id);
            loadedNamed.Name.Should().Be("UQ_Shop_Code");
            loadedNamed.ColumnIds.Should().Equal(code.Id);

            var loadedUnnamed = loadedEntity.UniqueConstraints[1];
            loadedUnnamed.Name.Should().BeNull();
            // 宣言順（Region → Code）が維持される
            loadedUnnamed.ColumnIds.Should().Equal(region.Id, code.Id);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>UniqueConstraints プロパティを持たない旧形式 JSON が空リストとして読めることを検証する</summary>
    [Fact(DisplayName = "旧形式 JSON（UniqueConstraints なし）は空リストとして読める")]
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
                    "TableName": "Shop",
                    "Columns": [
                      { "Id": "22222222-2222-2222-2222-222222222222", "Name": "Code", "DataType": "int" }
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
            entity.UniqueConstraints.Should().NotBeNull();
            entity.UniqueConstraints.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>UniqueConstraints / ColumnIds に明示的な null が書かれた JSON も空リストへ修復されることを検証する</summary>
    [Fact(DisplayName = "明示的な null の UniqueConstraints / ColumnIds は空リストへ修復される")]
    public void Load_ExplicitNulls_AreNormalized()
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
                    "UniqueConstraints": null
                  },
                  {
                    "Id": "33333333-3333-3333-3333-333333333333",
                    "TableName": "B",
                    "Columns": [],
                    "UniqueConstraints": [
                      { "Id": "44444444-4444-4444-4444-444444444444", "ColumnIds": null }
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

            loaded.Schema.Entities[0].UniqueConstraints.Should().BeEmpty();
            loaded
                .Schema.Entities[1]
                .UniqueConstraints.Should()
                .ContainSingle()
                .Which.ColumnIds.Should()
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
