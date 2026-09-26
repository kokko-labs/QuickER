using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Tests.Document;

/// <summary>
/// 計算列フラグ（<see cref="Column.IsComputed"/>）の図ファイル JSON 往復と、旧形式 JSON との互換を検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DiagramDocument.Version"/> は据え置き（1 のまま）。旧形式 JSON はプロパティ欠落＝
/// <c>false</c> として読め、失われるのは「書き込みから外す」指定だけ（列そのものは残る）＝
/// 前方互換の「無害に扱える」条件を満たす。
/// </para>
/// <para>
/// <see cref="Entity.PrimaryKeyColumnIds"/> と違い <c>bool</c> なので、明示的な <c>null</c> は
/// System.Text.Json が型不一致として拒否する（<see cref="Entity.Normalize"/> のような修復口がない）。
/// その挙動もここで固定する。
/// </para>
/// </remarks>
public class ComputedColumnPersistenceTests
{
    /// <summary>一時ファイルのパスを作る</summary>
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"er-computed-{Guid.NewGuid()}.json");

    /// <summary>計算列フラグが保存→読込で往復することを検証する</summary>
    [Fact(DisplayName = "Save → Load で計算列フラグが往復する")]
    public void SaveAndLoad_RoundTripsIsComputed()
    {
        var entity = new Entity
        {
            TableName = "Items",
            Columns =
            {
                new Column
                {
                    Name = "qty",
                    DataType = "int",
                    IsComputed = false,
                },
                new Column
                {
                    Name = "total",
                    DataType = "decimal(21,2)",
                    IsComputed = true,
                },
            },
        };

        var document = new DiagramDocument { Schema = new ErDiagram { Entities = { entity } } };
        var path = TempPath();

        try
        {
            JsonStorageService.Save(path, document);
            var loaded = JsonStorageService.Load(path);

            var loadedEntity = loaded.Schema.Entities.Should().ContainSingle().Which;
            loadedEntity.Columns.Select(c => c.IsComputed).Should().Equal(false, true);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>IsComputed を持たない旧形式 JSON が false として読めることを検証する（前方互換）</summary>
    [Fact(DisplayName = "旧形式 JSON（IsComputed なし）は false として読める")]
    public void Load_LegacyJsonWithoutProperty_YieldsFalse()
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
                    "TableName": "Items",
                    "Columns": [
                      { "Id": "22222222-2222-2222-2222-222222222222", "Name": "total", "DataType": "decimal(21,2)" }
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

            var column = loaded
                .Schema.Entities.Should()
                .ContainSingle()
                .Which.Columns.Should()
                .ContainSingle()
                .Which;
            // 列そのものは残り、落ちるのは「書き込みから外す」指定だけ
            column.Name.Should().Be("total");
            column.IsComputed.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// IsComputed に明示的な null が書かれた JSON は読込を拒否されることを検証する
    /// （bool は修復口を持たないため、リストと違って空へ倒さない）。
    /// </summary>
    [Fact(DisplayName = "明示的な null の IsComputed は読込を拒否する")]
    public void TryLoad_ExplicitNull_IsRejectedAsInvalidJson()
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
                    "TableName": "Items",
                    "Columns": [
                      { "Id": "22222222-2222-2222-2222-222222222222", "Name": "total", "DataType": "int", "IsComputed": null }
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

            var ok = JsonStorageService.TryLoad(path, out _, out var error, out _);

            ok.Should().BeFalse();
            error.Should().Be(DocumentLoadError.InvalidJson);
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
