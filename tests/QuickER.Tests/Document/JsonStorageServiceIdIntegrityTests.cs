using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Tests.Document;

/// <summary>
/// 読込時の Id 重複検査（<see cref="JsonStorageService.TryLoad"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 重複 Id を持つ図を読み込むと、上書き保存（<c>ToDocument</c> の <c>ToDictionary</c>）が毎回失敗し、
/// 自動保存は例外を握り潰すため一度も成功しない＝作業内容を保存する手段が両方とも消える。
/// 図ファイルは手編集でしか重複 Id を持ち得ないため、修復せず読込を拒否する
/// （拒否メッセージにはテーブル名と重複した Id を含める＝手で直すための情報）。
/// </remarks>
public sealed class JsonStorageServiceIdIntegrityTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-idintegrity-" + Guid.NewGuid().ToString("N")
    );

    public JsonStorageServiceIdIntegrityTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末失敗はテスト結果に影響させない
        }
    }

    /// <summary>指定スキーマの図（レイアウトなし）をファイルへ書き出してパスを返す</summary>
    private string WriteSchema(ErDiagram schema)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.json");
        JsonStorageService.Save(path, new DiagramDocument { Schema = schema, Layout = null });
        return path;
    }

    /// <summary>指定 Id・名前の列を 1 本だけ持つエンティティを作る</summary>
    private static Entity CreateEntity(
        Guid entityId,
        string tableName,
        Guid columnId,
        string columnName
    ) =>
        new()
        {
            Id = entityId,
            TableName = tableName,
            Columns =
            {
                new Column { Id = columnId, Name = columnName },
            },
        };

    /// <summary>同じ Id のエンティティが 2 件ある図は、テーブル名と Id を名指しして拒否されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: エンティティ Id の重複を拒否する")]
    public void TryLoad_DuplicateEntityId_IsRejected()
    {
        var duplicated = Guid.NewGuid();
        var path = WriteSchema(
            new ErDiagram
            {
                Entities =
                {
                    CreateEntity(duplicated, "Orders", Guid.NewGuid(), "OrderId"),
                    CreateEntity(duplicated, "OrderLines", Guid.NewGuid(), "LineId"),
                },
            }
        );

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.DuplicateId);
        exception!.Message.Should().Contain(duplicated.ToString());
        exception.Message.Should().Contain("Orders").And.Contain("OrderLines");
    }

    /// <summary>同一テーブル内で列 Id が重複する図は、テーブル名・列名・Id を名指しして拒否されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: 同一テーブル内の列 Id 重複を拒否する")]
    public void TryLoad_DuplicateColumnIdWithinEntity_IsRejected()
    {
        var duplicated = Guid.NewGuid();
        var entity = CreateEntity(Guid.NewGuid(), "Orders", duplicated, "OrderId");
        entity.Columns.Add(new Column { Id = duplicated, Name = "CustomerId" });
        var path = WriteSchema(new ErDiagram { Entities = { entity } });

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.DuplicateId);
        exception!.Message.Should().Contain(duplicated.ToString());
        // テーブル間の重複とは別の検査（別の文言）で報告する＝どちらが欠けても残りでは代替できない
        exception.Message.Should().Contain("in table 'Orders'");
        exception.Message.Should().Contain("OrderId").And.Contain("CustomerId");
    }

    /// <summary>テーブルをまたいで列 Id が重複する図は、両テーブル名と Id を名指しして拒否されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: エンティティをまたぐ列 Id 重複を拒否する")]
    public void TryLoad_DuplicateColumnIdAcrossEntities_IsRejected()
    {
        var duplicated = Guid.NewGuid();
        var path = WriteSchema(
            new ErDiagram
            {
                Entities =
                {
                    CreateEntity(Guid.NewGuid(), "Orders", duplicated, "OrderId"),
                    CreateEntity(Guid.NewGuid(), "OrderLines", duplicated, "LineId"),
                },
            }
        );

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.DuplicateId);
        exception!.Message.Should().Contain(duplicated.ToString());
        exception.Message.Should().Contain("Orders").And.Contain("OrderLines");
    }

    /// <summary>Id が重複しない通常の図は従来どおり読み込めることを検証する（誤検知しない）</summary>
    [Fact(DisplayName = "TryLoad: Id が重複しない図は読み込める")]
    public void TryLoad_WithoutDuplicates_Loads()
    {
        var path = WriteSchema(
            new ErDiagram
            {
                Entities =
                {
                    CreateEntity(Guid.NewGuid(), "Orders", Guid.NewGuid(), "OrderId"),
                    CreateEntity(Guid.NewGuid(), "OrderLines", Guid.NewGuid(), "LineId"),
                },
            }
        );

        JsonStorageService.TryLoad(path, out var document, out var error, out _).Should().BeTrue();

        error.Should().Be(DocumentLoadError.None);
        document!.Schema.Entities.Should().HaveCount(2);
    }

    /// <summary>
    /// リレーション・一意制約・クエリの Id 重複は拒否しないことを検証する
    /// （参照も辞書化も無く実害が無いため、検査対象を広げない判断の固定）
    /// </summary>
    [Fact(DisplayName = "TryLoad: リレーション・一意制約・クエリの Id 重複は拒否しない")]
    public void TryLoad_DuplicateNonKeyIds_AreAccepted()
    {
        var source = CreateEntity(Guid.NewGuid(), "Orders", Guid.NewGuid(), "OrderId");
        var target = CreateEntity(Guid.NewGuid(), "OrderLines", Guid.NewGuid(), "LineId");
        var duplicated = Guid.NewGuid();

        source.UniqueConstraints.Add(
            new UniqueConstraint { Id = duplicated, ColumnIds = { source.Columns[0].Id } }
        );
        source.UniqueConstraints.Add(
            new UniqueConstraint { Id = duplicated, ColumnIds = { source.Columns[0].Id } }
        );

        var path = WriteSchema(
            new ErDiagram
            {
                Entities = { source, target },
                Relationships =
                {
                    new Relationship
                    {
                        Id = duplicated,
                        SourceEntityId = source.Id,
                        TargetEntityId = target.Id,
                    },
                    new Relationship
                    {
                        Id = duplicated,
                        SourceEntityId = source.Id,
                        TargetEntityId = target.Id,
                    },
                },
                Queries =
                {
                    new QueryDefinition { Id = duplicated, Name = "ByOrderId" },
                    new QueryDefinition { Id = duplicated, Name = "ByLineId" },
                },
            }
        );

        JsonStorageService.TryLoad(path, out var document, out var error, out _).Should().BeTrue();

        error.Should().Be(DocumentLoadError.None);
        document!.Schema.Relationships.Should().HaveCount(2);
    }
}
