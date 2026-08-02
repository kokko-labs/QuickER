using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="DiagramMergeReconciler"/> の Guid 引継マージ（名前照合・Id 書換え・クエリ生存判定）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 再取込は毎回新規 Guid を持つ取込結果を返すため、素朴に置換すると名前付きクエリ・レイアウト・Memo が
/// 全滅する。本クラスは「テーブル名・列名の一意一致で Id を現在図へ寄せる」照合と、それに伴うリレーション
/// 参照の追従書き換え・Memo 温存・クエリの生存/壊れ判定・曖昧名の除外・空図 no-op を検証する。
/// </remarks>
public class DiagramMergeReconcilerTests
{
    /// <summary>指定 Id・名前・型の列を作る</summary>
    private static Column Col(Guid id, string name, string type = "int") =>
        new()
        {
            Id = id,
            Name = name,
            DataType = type,
        };

    /// <summary>指定 Id・名前・列群のエンティティを作る</summary>
    private static Entity Ent(Guid id, string name, params Column[] columns) =>
        new()
        {
            Id = id,
            TableName = name,
            Columns = columns.ToList(),
        };

    /// <summary>一致エンティティ・列の Id が現在図の Guid へ書き換わることを検証する</summary>
    [Fact(DisplayName = "名前一致でエンティティ・列の Id が現在図の Guid へ引き継がれる")]
    public void Reconcile_NameMatch_AdoptsCurrentGuids()
    {
        var currentEntityId = Guid.NewGuid();
        var currentColumnId = Guid.NewGuid();
        var current = new ErDiagram
        {
            Entities = { Ent(currentEntityId, "Customer", Col(currentColumnId, "Id")) },
        };

        // 取込結果は同名・同名列だが Id は新規
        var importedEntity = Ent(Guid.NewGuid(), "Customer", Col(Guid.NewGuid(), "Id"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        merged.Entities.Should().ContainSingle();
        merged.Entities[0].Id.Should().Be(currentEntityId);
        merged.Entities[0].Columns.Should().ContainSingle();
        merged.Entities[0].Columns[0].Id.Should().Be(currentColumnId);
    }

    /// <summary>取込結果のリレーションの両端エンティティ・両端列 Id が対応表で書き換わることを検証する</summary>
    [Fact(DisplayName = "リレーションの参照 Id（両端エンティティ・両端列）が追従書き換えされる")]
    public void Reconcile_RewritesRelationshipReferences()
    {
        var parentId = Guid.NewGuid();
        var parentPkId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var childFkId = Guid.NewGuid();

        var current = new ErDiagram
        {
            Entities =
            {
                Ent(parentId, "Parent", Col(parentPkId, "Id")),
                Ent(childId, "Child", Col(childFkId, "ParentId")),
            },
        };

        var importedParentPk = Guid.NewGuid();
        var importedChildFk = Guid.NewGuid();
        var importedParent = Ent(Guid.NewGuid(), "Parent", Col(importedParentPk, "Id"));
        var importedChild = Ent(Guid.NewGuid(), "Child", Col(importedChildFk, "ParentId"));
        var relationship = new Relationship
        {
            SourceEntityId = importedParent.Id,
            TargetEntityId = importedChild.Id,
            SourceColumnId = importedParentPk,
            TargetColumnId = importedChildFk,
        };

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedParent, importedChild },
            new[] { relationship },
            preserveExistingMemo: true
        );

        var mergedRelationship = merged.Relationships.Should().ContainSingle().Subject;
        mergedRelationship.SourceEntityId.Should().Be(parentId);
        mergedRelationship.TargetEntityId.Should().Be(childId);
        mergedRelationship.SourceColumnId.Should().Be(parentPkId);
        mergedRelationship.TargetColumnId.Should().Be(childFkId);
    }

    /// <summary>リネーム（別名）は一致せず新規 Guid のまま＝そのエンティティを参照するクエリは壊れる</summary>
    [Fact(DisplayName = "リネームは新規扱い（Id 引継されずクエリが壊れる）")]
    public void Reconcile_Rename_TreatedAsNew_BreaksQuery()
    {
        var currentEntityId = Guid.NewGuid();
        var current = new ErDiagram
        {
            Entities = { Ent(currentEntityId, "Customer", Col(Guid.NewGuid(), "Id")) },
            Queries =
            {
                new QueryDefinition { Name = "GetAll", EntityId = currentEntityId },
            },
        };

        // 取込結果はテーブルがリネームされている（別名）
        var importedEntity = Ent(Guid.NewGuid(), "Client", Col(Guid.NewGuid(), "Id"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        // 別名のため Id は引き継がれない（新規のまま）
        merged.Entities[0].Id.Should().Be(importedEntity.Id);
        merged.SurvivingQueries.Should().BeEmpty();
        merged.BrokenQueries.Should().ContainSingle().Which.Name.Should().Be("GetAll");
    }

    /// <summary>同名テーブルが複数ある場合、その名前はマッチ対象外（曖昧さ回避＝新規扱い）</summary>
    [Fact(DisplayName = "同名テーブルが複数あるとその名前はマッチ対象外になる")]
    public void Reconcile_AmbiguousTableName_NotMatched()
    {
        var current = new ErDiagram
        {
            Entities =
            {
                Ent(Guid.NewGuid(), "Log", Col(Guid.NewGuid(), "Id")),
                Ent(Guid.NewGuid(), "Log", Col(Guid.NewGuid(), "Message")),
            },
        };

        var importedId = Guid.NewGuid();
        var importedEntity = Ent(importedId, "Log", Col(Guid.NewGuid(), "Id"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        // 曖昧な名前なので Id は引き継がれない
        merged.Entities[0].Id.Should().Be(importedId);
    }

    /// <summary>同一エンティティ内で同名列が複数あると、その列名はマッチ対象外になる</summary>
    [Fact(DisplayName = "同名列が複数あるとその列名はマッチ対象外になる")]
    public void Reconcile_AmbiguousColumnName_NotMatched()
    {
        var currentEntityId = Guid.NewGuid();
        var current = new ErDiagram
        {
            Entities =
            {
                Ent(
                    currentEntityId,
                    "Customer",
                    Col(Guid.NewGuid(), "Value"),
                    Col(Guid.NewGuid(), "Value")
                ),
            },
        };

        var importedColumnId = Guid.NewGuid();
        var importedEntity = Ent(Guid.NewGuid(), "Customer", Col(importedColumnId, "Value"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        // エンティティ自体は一致するが、曖昧な列名は引き継がれない
        merged.Entities[0].Id.Should().Be(currentEntityId);
        merged.Entities[0].Columns[0].Id.Should().Be(importedColumnId);
    }

    /// <summary>preserveExistingMemo=true では一致エンティティの Memo を現在図の値で温存する</summary>
    [Fact(DisplayName = "Memo 温存: preserveExistingMemo=true で現在図の Memo を維持する")]
    public void Reconcile_PreserveMemoTrue_KeepsCurrentMemo()
    {
        var currentEntityId = Guid.NewGuid();
        var current = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    Id = currentEntityId,
                    TableName = "Customer",
                    Memo = "重要顧客テーブル",
                    Columns = { Col(Guid.NewGuid(), "Id") },
                },
            },
        };

        // 取込結果（DB 取込）は Memo を持たない
        var importedEntity = Ent(Guid.NewGuid(), "Customer", Col(Guid.NewGuid(), "Id"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        merged.Entities[0].Memo.Should().Be("重要顧客テーブル");
    }

    /// <summary>preserveExistingMemo=false（Excel）では取込値の Memo を正とする</summary>
    [Fact(DisplayName = "Memo: preserveExistingMemo=false で取込値の Memo を正とする")]
    public void Reconcile_PreserveMemoFalse_UsesImportedMemo()
    {
        var currentEntityId = Guid.NewGuid();
        var current = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    Id = currentEntityId,
                    TableName = "Customer",
                    Memo = "旧メモ",
                    Columns = { Col(Guid.NewGuid(), "Id") },
                },
            },
        };

        // Excel 定義書は Memo を保持する
        var importedEntity = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "Customer",
            Memo = "Excel メモ",
            Columns = { Col(Guid.NewGuid(), "Id") },
        };

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: false
        );

        merged.Entities[0].Memo.Should().Be("Excel メモ");
    }

    /// <summary>全 Guid 参照（エンティティ・列参照パラメータ・並び順）が解決できるクエリは生存する</summary>
    [Fact(DisplayName = "生存クエリ: 全 Guid 参照が解決できると温存される")]
    public void Reconcile_AllReferencesResolved_QuerySurvives()
    {
        var entityId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var current = new ErDiagram
        {
            Entities = { Ent(entityId, "Order", Col(columnId, "CustomerId")) },
            Queries =
            {
                new QueryDefinition
                {
                    Name = "GetByCustomer",
                    EntityId = entityId,
                    Parameters =
                    {
                        new QueryParameter { Name = "customerId", SourceColumnId = columnId },
                    },
                    OrderBy = { new QueryOrdering { ColumnId = columnId } },
                },
            },
        };

        var importedEntity = Ent(Guid.NewGuid(), "Order", Col(Guid.NewGuid(), "CustomerId"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        merged.SurvivingQueries.Should().ContainSingle().Which.Name.Should().Be("GetByCustomer");
        merged.BrokenQueries.Should().BeEmpty();
    }

    /// <summary>列参照（並び順の列）が失われるとクエリは壊れる</summary>
    [Fact(DisplayName = "壊れクエリ: 参照列（並び順）が失われると壊れる")]
    public void Reconcile_LostColumnReference_QueryBreaks()
    {
        var entityId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var current = new ErDiagram
        {
            Entities = { Ent(entityId, "Order", Col(columnId, "CreatedAt")) },
            Queries =
            {
                new QueryDefinition
                {
                    Name = "SortByCreatedAt",
                    EntityId = entityId,
                    OrderBy = { new QueryOrdering { ColumnId = columnId } },
                },
            },
        };

        // 取込結果ではエンティティは一致するが、並び順が参照する列がリネームされている
        var importedEntity = Ent(Guid.NewGuid(), "Order", Col(Guid.NewGuid(), "CreationDate"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        merged.SurvivingQueries.Should().BeEmpty();
        merged.BrokenQueries.Should().ContainSingle().Which.Name.Should().Be("SortByCreatedAt");
    }

    /// <summary>現在図が空のときは実質 no-op（一致なし・全新規・クエリ全滅でも安全に動く）</summary>
    [Fact(DisplayName = "空図 no-op: 一致なしで全新規、クエリだけ残った図でも壊れとして安全に扱う")]
    public void Reconcile_EmptyCurrent_NoMatches()
    {
        var orphanQuery = new QueryDefinition { Name = "Orphan", EntityId = Guid.NewGuid() };
        var current = new ErDiagram { Queries = { orphanQuery } };

        var importedId = Guid.NewGuid();
        var importedEntity = Ent(importedId, "Customer", Col(Guid.NewGuid(), "Id"));

        var merged = DiagramMergeReconciler.Reconcile(
            current,
            new[] { importedEntity },
            Array.Empty<Relationship>(),
            preserveExistingMemo: true
        );

        // 取込結果は Id 書換えされず（一致なし）、クエリだけ残った図でも壊れとして分類され例外にならない
        merged.Entities[0].Id.Should().Be(importedId);
        merged.SurvivingQueries.Should().BeEmpty();
        merged.BrokenQueries.Should().ContainSingle().Which.Name.Should().Be("Orphan");
    }
}
