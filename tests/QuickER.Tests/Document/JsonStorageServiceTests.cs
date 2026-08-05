using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Document;

/// <summary><see cref="JsonStorageService"/> の JSON 保存・読込往復を検証するテストクラス</summary>
public class JsonStorageServiceTests
{
    /// <summary>保存後に読み込み、エンティティ座標・色・リレーションの各属性が往復で保持されることを検証する</summary>
    [Fact(DisplayName = "Save → Load でエンティティとリレーションが復元される")]
    public void SaveAndLoad_RoundTrip()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);

        var a = vm.Entities[0];
        var b = vm.Entities[1];
        a.TableName = "Customer";
        a.X = 100;
        a.Y = 50;
        a.TitleBackgroundColor = "#FFF0BF";

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);
        b.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsNullable = false,
                }
            )
        );
        vm.Relationships[0].SourceColumnId = a.Columns[0].Id;
        vm.Relationships[0].TargetColumnId = b.Columns[1].Id;
        vm.Relationships[0].ConstraintName = "FK_Order_Customer";

        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, vm.ToDocument());
            File.Exists(path).Should().BeTrue();

            var loaded = JsonStorageService.Load(path);
            loaded.Schema.Entities.Should().HaveCount(2);
            loaded.Schema.Relationships.Should().HaveCount(1);

            // 意味情報は schema、視覚情報は layout サイドカーへ分離して往復する
            var ea = loaded.Schema.Entities.First(e => e.Id == a.Id);
            ea.TableName.Should().Be("Customer");

            var la = loaded.Layout!.Should().ContainKey(a.Id).WhoseValue;
            la.X.Should().Be(100);
            la.Y.Should().Be(50);
            la.TitleBackgroundColor.Should().Be("#FFF0BF");

            loaded.Schema.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
            loaded.Schema.Relationships[0].SourceColumnId.Should().Be(a.Columns[0].Id);
            loaded.Schema.Relationships[0].TargetColumnId.Should().Be(b.Columns[1].Id);
            loaded.Schema.Relationships[0].ConstraintName.Should().Be("FK_Order_Customer");
            loaded
                .Schema.Entities.First(e => e.Id == b.Id)
                .Columns[1]
                .IsNullable.Should()
                .BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>名前付きクエリ定義の全フィールドが保存・読込で往復することを検証する</summary>
    [Fact(DisplayName = "Save → Load で名前付きクエリ定義が全フィールド復元される")]
    public void SaveAndLoad_QueryDefinition_RoundTrip()
    {
        var entity = new Entity { TableName = "Order" };
        var amountColumn = new Column { Name = "Amount", DataType = "decimal(12,2)" };
        entity.Columns.Add(amountColumn);

        var query = new QueryDefinition
        {
            EntityId = entity.Id,
            Name = "GetByCustomer",
            Description = "顧客IDで注文を検索する",
            Returns = QueryReturnShape.Projection,
            ScalarType = null,
            Parameters =
            {
                new QueryParameter
                {
                    Name = "customerId",
                    Type = "int32",
                    SourceColumnId = amountColumn.Id,
                },
                new QueryParameter
                {
                    Name = "statuses",
                    Type = "int32",
                    IsList = true,
                },
            },
            Condition = "CustomerId = @customerId AND Status IN @statuses",
            OrderBy =
            {
                new QueryOrdering { ColumnId = amountColumn.Id, Descending = true },
            },
            HasPaging = true,
            Implementation = QueryImplementationKind.Sql,
            Sql = { ["sqlserver"] = "SELECT ...", ["sqlite"] = "SELECT ... LIMIT ..." },
            ResultTypeName = "OrderSummaryRow",
            Fields =
            {
                new ProjectionField
                {
                    Name = "TotalAmount",
                    Type = "decimal(12,2)",
                    SourceColumnId = amountColumn.Id,
                },
            },
        };

        var document = new DiagramDocument();
        document.Schema.Entities.Add(entity);
        document.Schema.Queries.Add(query);

        var path = Path.Combine(Path.GetTempPath(), $"er-query-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, document);
            var loaded = JsonStorageService
                .Load(path)
                .Schema.Queries.Should()
                .ContainSingle()
                .Which;

            loaded.Id.Should().Be(query.Id);
            loaded.EntityId.Should().Be(entity.Id);
            loaded.Name.Should().Be("GetByCustomer");
            loaded.Description.Should().Be("顧客IDで注文を検索する");
            loaded.Returns.Should().Be(QueryReturnShape.Projection);
            loaded.ScalarType.Should().BeNull();
            loaded.Parameters.Should().HaveCount(2);
            loaded.Parameters[0].SourceColumnId.Should().Be(amountColumn.Id);
            loaded.Parameters[1].Name.Should().Be("statuses");
            loaded.Parameters[1].IsList.Should().BeTrue();
            loaded.Parameters[1].SourceColumnId.Should().BeNull();
            loaded.Condition.Should().Be("CustomerId = @customerId AND Status IN @statuses");
            loaded.OrderBy.Should().ContainSingle();
            loaded.OrderBy[0].ColumnId.Should().Be(amountColumn.Id);
            loaded.OrderBy[0].Descending.Should().BeTrue();
            loaded.HasPaging.Should().BeTrue();
            loaded.Implementation.Should().Be(QueryImplementationKind.Sql);
            loaded.Sql.Should().HaveCount(2).And.ContainKey("sqlite");
            loaded.ResultTypeName.Should().Be("OrderSummaryRow");
            loaded.Fields.Should().ContainSingle();
            loaded.Fields[0].SourceColumnId.Should().Be(amountColumn.Id);
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
    /// null のプロパティ（列参照パラメータの Type・未指定の ScalarType / ConstraintName 等）が
    /// 保存 JSON からキーごと省略されることを検証する（WhenWritingNull＝図ファイルの正準形）。
    /// </summary>
    [Fact(DisplayName = "Save: null プロパティはキーごと省略される")]
    public void Save_NullProperties_AreOmittedFromJson()
    {
        var entity = new Entity { TableName = "Order" };
        var column = new Column { Name = "CustomerId", DataType = "int" };
        entity.Columns.Add(column);

        var document = new DiagramDocument();
        document.Schema.Entities.Add(entity);
        document.Schema.Relationships.Add(
            new Relationship
            {
                SourceEntityId = entity.Id,
                TargetEntityId = entity.Id,
                // SourceColumnId / TargetColumnId / ConstraintName は未指定（null）
            }
        );
        document.Schema.Queries.Add(
            new QueryDefinition
            {
                EntityId = entity.Id,
                Name = "GetByCustomer",
                Parameters =
                {
                    // 列参照型付け＝Type は保存されない（null）
                    new QueryParameter { Name = "customerId", SourceColumnId = column.Id },
                },
                Fields =
                {
                    new ProjectionField { Name = "CustomerId", SourceColumnId = column.Id },
                },
            }
        );

        var path = Path.Combine(Path.GetTempPath(), $"er-nulls-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, document);
            var json = File.ReadAllText(path);

            json.Should().NotContain("\"ScalarType\"", "null プロパティはキーごと省略される");
            json.Should().NotContain("\"Type\": null");
            json.Should().NotContain("\"ConstraintName\"");
            json.Should().NotContain("\"IsNullable\": null");

            // 読み戻しでは省略されたキーが既定値（null）へ戻る
            var loaded = JsonStorageService.Load(path);
            var query = loaded.Schema.Queries.Should().ContainSingle().Which;
            query.Parameters[0].Type.Should().BeNull();
            query.Parameters[0].SourceColumnId.Should().Be(column.Id);
            query.Fields[0].Type.Should().BeNull();
            query.Fields[0].IsNullable.Should().BeNull();
            loaded.Schema.Relationships[0].ConstraintName.Should().BeNull();
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
    /// null 省略形式（キー欠落）の JSON が、クエリ以外の図要素（Entity / Relationship / Layout）も含めて
    /// プロパティ既定値で読み込めることを検証する（省略と既定値の相互可換性）。
    /// </summary>
    [Fact(DisplayName = "Load: キー欠落（null 省略形式）は既定値で吸収される")]
    public void Load_OmittedKeys_FallBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-omitted-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "Version": 1,
                  "Schema": {
                    "Entities": [
                      {
                        "Id": "11111111-0000-0000-0000-000000000001",
                        "TableName": "Customer",
                        "Columns": [
                          { "Id": "11111111-0000-0000-0000-000000000002", "Name": "CustomerId", "DataType": "int" }
                        ]
                      }
                    ],
                    "Relationships": [
                      {
                        "SourceEntityId": "11111111-0000-0000-0000-000000000001",
                        "TargetEntityId": "11111111-0000-0000-0000-000000000001"
                      }
                    ],
                    "Queries": [
                      {
                        "EntityId": "11111111-0000-0000-0000-000000000001",
                        "Name": "GetById",
                        "Parameters": [
                          { "Name": "id", "SourceColumnId": "11111111-0000-0000-0000-000000000002" }
                        ]
                      }
                    ]
                  },
                  "Layout": {
                    "11111111-0000-0000-0000-000000000001": { "X": 10 }
                  }
                }
                """
            );

            var loaded = JsonStorageService.Load(path);

            // Entity / Relationship: 省略キーは既定値（Memo / Description は空文字・列参照 / 制約名は null）
            var entity = loaded.Schema.Entities.Should().ContainSingle().Which;
            entity.Memo.Should().BeEmpty();
            entity.Description.Should().BeEmpty();
            var relationship = loaded.Schema.Relationships.Should().ContainSingle().Which;
            relationship.Type.Should().Be(RelationshipType.OneToMany);
            relationship.SourceColumnId.Should().BeNull();
            relationship.ConstraintName.Should().BeNull();

            // Query: 省略キーは既定値（Type / Condition / ScalarType は null・コレクションは空）
            var query = loaded.Schema.Queries.Should().ContainSingle().Which;
            query.Returns.Should().Be(QueryReturnShape.List);
            query.Parameters[0].Type.Should().BeNull();
            query.Condition.Should().BeNull();
            query.ScalarType.Should().BeNull();
            query.OrderBy.Should().BeEmpty();
            query.Fields.Should().BeEmpty();

            // Layout: 省略キーは既定値（Width 200・既定タイトル色）
            var layout = loaded.Layout!.Should().ContainKey(entity.Id).WhoseValue;
            layout.X.Should().Be(10);
            layout.Width.Should().Be(200);
            layout.TitleBackgroundColor.Should().Be(EntityLayout.DefaultTitleBackgroundColor);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>queries を持たない既存フォーマットの JSON が空のクエリ一覧で読み込めることを検証する（後方互換）</summary>
    [Fact(DisplayName = "Load: queries が無い既存 JSON は空のクエリ一覧になる")]
    public void Load_LegacyJsonWithoutQueries_YieldsEmptyQueries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-legacy-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "Version": 1,
                  "Schema": {
                    "Entities": [ { "TableName": "Customer" } ],
                    "Relationships": [],
                    "TargetDbms": "sqlserver"
                  },
                  "Layout": {}
                }
                """
            );

            var loaded = JsonStorageService.Load(path);

            loaded.Schema.Entities.Should().ContainSingle();
            loaded.Schema.Queries.Should().BeEmpty();
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
    /// コレクション・必須値へ明示的に <c>null</c> を書いた図ファイルが、既定値へ修復されて読めることを
    /// 検証する（キー欠落と違い、明示 null はデシリアライザが初期化子を上書きするため正規化が要る）。
    /// </summary>
    [Fact(DisplayName = "Load: 明示 null のコレクション・TargetDbms は既定値へ正規化される")]
    public void Load_ExplicitNulls_AreNormalizedToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-null-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "Version": 1,
                  "Schema": {
                    "TargetDbms": null,
                    "Entities": null,
                    "Relationships": null,
                    "Queries": null
                  },
                  "Layout": null
                }
                """
            );

            var loaded = JsonStorageService.Load(path);

            // 図を消費する側（GUI の Count 参照・生成器の列挙）が NRE で落ちないこと
            loaded.Schema.Entities.Should().NotBeNull().And.BeEmpty();
            loaded.Schema.Relationships.Should().NotBeNull().And.BeEmpty();
            loaded.Schema.Queries.Should().NotBeNull().And.BeEmpty();
            loaded.Schema.TargetDbms.Should().Be("sqlserver", "方言解決の起点は null を許さない");

            // layout の null は「スキーマのみ文書」の正当な表現なのでそのまま残す
            loaded.Layout.Should().BeNull();
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    /// <summary>リスト・辞書の中に書かれた null 要素が取り除かれることを検証する</summary>
    [Fact(DisplayName = "Load: リスト・辞書内の null 要素は取り除かれる")]
    public void Load_NullElements_AreRemoved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-null-elements-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "Version": 1,
                  "Schema": {
                    "TargetDbms": "sqlite",
                    "Entities": [
                      null,
                      {
                        "Id": "33333333-0000-0000-0000-000000000001",
                        "TableName": "Customer",
                        "Columns": [ null, { "Name": "CustomerId", "DataType": "int" } ]
                      },
                      {
                        "Id": "33333333-0000-0000-0000-000000000002",
                        "TableName": "Order",
                        "Columns": null
                      }
                    ],
                    "Relationships": [ null ],
                    "Queries": [
                      null,
                      {
                        "EntityId": "33333333-0000-0000-0000-000000000001",
                        "Name": "GetAll",
                        "Parameters": [ null ],
                        "OrderBy": null,
                        "Fields": [ null ],
                        "Sql": { "sqlite": null }
                      }
                    ]
                  },
                  "Layout": {
                    "33333333-0000-0000-0000-000000000001": null,
                    "33333333-0000-0000-0000-000000000002": { "X": 10 }
                  }
                }
                """
            );

            var loaded = JsonStorageService.Load(path);

            loaded.Schema.Entities.Should().HaveCount(2);
            loaded.Schema.Entities[0].Columns.Should().ContainSingle();
            loaded.Schema.Entities[1].Columns.Should().NotBeNull().And.BeEmpty();
            loaded.Schema.Relationships.Should().BeEmpty();

            var query = loaded.Schema.Queries.Should().ContainSingle().Which;
            query.Parameters.Should().BeEmpty();
            query.OrderBy.Should().NotBeNull().And.BeEmpty();
            query.Fields.Should().BeEmpty();
            query.Sql.Should().NotBeNull().And.BeEmpty("値が null の方言 SQL は保持しない");

            // layout は値が null のエントリだけを落とし、正当な配置は残す
            loaded.Layout.Should().HaveCount(1);
            loaded.Layout![new Guid("33333333-0000-0000-0000-000000000002")].X.Should().Be(10);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    /// <summary>schema 自体が null の文書も空スキーマへ修復されることを検証する</summary>
    /// <remarks>
    /// GUI・MCP の読込は「Version・Schema オブジェクトを持つか」の事前検証で先に拒否するため、
    /// この修復が効くのは事前検証を持たない経路（CLI の直接読み込み等）。どちらの経路でも
    /// <see cref="NullReferenceException"/> で落ちないことが要点。
    /// </remarks>
    [Fact(DisplayName = "Load: schema が null の文書は空スキーマへ正規化される")]
    public void Load_NullSchema_YieldsEmptySchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-null-schema-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(path, """{ "Version": 1, "Schema": null }""");

            var loaded = JsonStorageService.Load(path);

            loaded.Schema.Should().NotBeNull();
            loaded.Schema.Entities.Should().BeEmpty();
            loaded.Schema.TargetDbms.Should().Be("sqlserver");
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    /// <summary>
    /// スキーマのみ文書（<c>Layout = null</c>）を保存すると layout キーがキーごと省略され、
    /// version / schema / queries は出力されることを検証する（スキーマのみ JSON の正準形）。
    /// </summary>
    [Fact(DisplayName = "Save: スキーマのみ文書（Layout = null）は layout キーを省略する")]
    public void Save_SchemaOnlyDocument_OmitsLayoutKey()
    {
        var entity = new Entity { TableName = "Order" };
        var document = new DiagramDocument
        {
            Schema = new ErDiagram { Entities = { entity } },
            Layout = null,
        };
        document.Schema.Queries.Add(new QueryDefinition { EntityId = entity.Id, Name = "GetAll" });

        var path = Path.Combine(Path.GetTempPath(), $"er-schema-only-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, document);
            var json = File.ReadAllText(path);

            json.Should().NotContain("\"Layout\"", "スキーマのみ文書は layout キーを出力しない");
            json.Should().Contain("\"Version\"");
            json.Should().Contain("\"Schema\"");
            json.Should().Contain("\"Queries\"");
            json.Should().Contain("GetAll", "クエリ定義は schema 内に出力される");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>layout キーを持たない JSON が例外なく読め、Layout が null または空になることを検証する</summary>
    [Fact(DisplayName = "Load: layout キーの無い JSON は null または空 layout として読める")]
    public void Load_MissingLayoutKey_YieldsNullOrEmptyLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-no-layout-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "Version": 1,
                  "Schema": {
                    "Entities": [ { "TableName": "Customer" } ],
                    "Relationships": [],
                    "TargetDbms": "sqlserver"
                  }
                }
                """
            );

            var loaded = JsonStorageService.Load(path);

            loaded.Schema.Entities.Should().ContainSingle();
            (loaded.Layout is null or { Count: 0 }).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>フォーマットバージョンが CurrentVersion より大きい文書は IsNewerFormat が立つことを検証する</summary>
    [Fact(DisplayName = "Load: CurrentVersion より新しいフォーマットは IsNewerFormat が true")]
    public void Load_NewerVersion_SetsIsNewerFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-newer-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(
                path,
                new DiagramDocument { Version = DiagramDocument.CurrentVersion + 1 }
            );

            JsonStorageService.Load(path).IsNewerFormat.Should().BeTrue();

            JsonStorageService.Save(path, new DiagramDocument());

            JsonStorageService.Load(path).IsNewerFormat.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>新しいフォーマットの文書を開くとき、警告確認でキャンセルすると現在の図が保持されることを検証する</summary>
    [Fact(DisplayName = "Open: 新しいフォーマットの警告をキャンセルすると読み込まない")]
    public void Open_NewerFormat_CancelKeepsCurrentDiagram()
    {
        var path = SaveNewerFormatDocument();
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new MainViewModel(
            dialogs,
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        try
        {
            vm.OpenCommand.Execute(null);

            vm.Entities.Should().BeEmpty();
            dialogs.WarningConfirmMessages.Should().ContainSingle();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>新しいフォーマットの文書を開くとき、警告確認で続行すると読み込まれることを検証する</summary>
    [Fact(DisplayName = "Open: 新しいフォーマットの警告に続行すると読み込む")]
    public void Open_NewerFormat_ConfirmLoads()
    {
        var path = SaveNewerFormatDocument();
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new MainViewModel(
            dialogs,
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        try
        {
            vm.OpenCommand.Execute(null);

            vm.Entities.Should().ContainSingle();
            dialogs.WarningConfirmMessages.Should().ContainSingle();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>保存先が存在しない場合に SaveAtomic が新規作成し、一時ファイルを残さないことを検証する</summary>
    [Fact(DisplayName = "SaveAtomic: 保存先が無ければ新規作成し .tmp を残さない")]
    public void SaveAtomic_NewFile_CreatesFileWithoutTemporaryLeftover()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-atomic-new-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.SaveAtomic(
                path,
                BuildDocument("Customer", DiagramDocument.CurrentVersion)
            );

            File.Exists(path).Should().BeTrue();
            FindTemporaryLeftovers(path).Should().BeEmpty("一時ファイルは差し替え後に残らない");
            JsonStorageService
                .Load(path)
                .Schema.Entities.Should()
                .ContainSingle()
                .Which.TableName.Should()
                .Be("Customer");
        }
        finally
        {
            DeleteIfExists(path);
            DeleteTemporaryLeftovers(path);
        }
    }

    /// <summary>既存ファイルを SaveAtomic が置換し、一時ファイルを残さないことを検証する</summary>
    [Fact(DisplayName = "SaveAtomic: 既存ファイルを置換し .tmp を残さない")]
    public void SaveAtomic_ExistingFile_ReplacesContentWithoutTemporaryLeftover()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-atomic-replace-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, BuildDocument("Old", DiagramDocument.CurrentVersion));
            JsonStorageService.SaveAtomic(
                path,
                BuildDocument("New", DiagramDocument.CurrentVersion)
            );

            FindTemporaryLeftovers(path).Should().BeEmpty("一時ファイルは差し替え後に残らない");
            JsonStorageService
                .Load(path)
                .Schema.Entities.Should()
                .ContainSingle()
                .Which.TableName.Should()
                .Be("New");
        }
        finally
        {
            DeleteIfExists(path);
            DeleteTemporaryLeftovers(path);
        }
    }

    /// <summary>差し替えに失敗しても一時ファイルを残さず、失敗理由を呼び出し側へ伝えることを検証する</summary>
    /// <remarks>
    /// 保存先と同名のディレクトリを置いて本体への差し替え（<see cref="File.Move(string, string)"/>）を
    /// 確実に失敗させる。失敗のたびに <c>.tmp</c> が残ると、次回保存の差し替え対象と紛らわしくなる。
    /// </remarks>
    [Fact(DisplayName = "SaveAtomic: 差し替え失敗時は .tmp を残さず例外を投げる")]
    public void SaveAtomic_ReplaceFailure_RemovesTemporaryAndThrows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-atomic-fail-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);

        try
        {
            var act = () =>
                JsonStorageService.SaveAtomic(
                    path,
                    BuildDocument("Customer", DiagramDocument.CurrentVersion)
                );

            // 保存先がディレクトリの場合、File.Move(overwrite:true) は UnauthorizedAccessException を
            // 投げる（OS/ランタイム依存）ため、どちらの型でも「伝播していること」を検証する
            var thrown = act.Should().Throw<Exception>("差し替えの失敗を握り潰さない").Which;
            (thrown is IOException or UnauthorizedAccessException)
                .Should()
                .BeTrue($"IO 系の例外であること（実際: {thrown.GetType().Name}）");
            FindTemporaryLeftovers(path).Should().BeEmpty("失敗時に一時ファイルを残さない");
        }
        finally
        {
            DeleteTemporaryLeftovers(path);

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>SaveAtomic と Save の出力 JSON が完全に一致することを検証する（直列化設定の共有）</summary>
    [Fact(DisplayName = "SaveAtomic: 出力 JSON は Save と同一")]
    public void SaveAtomic_ProducesIdenticalJsonToSave()
    {
        var plainPath = Path.Combine(Path.GetTempPath(), $"er-atomic-plain-{Guid.NewGuid()}.json");
        var atomicPath = Path.Combine(Path.GetTempPath(), $"er-atomic-same-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(
                plainPath,
                BuildDocument("Customer", DiagramDocument.CurrentVersion)
            );
            JsonStorageService.SaveAtomic(
                atomicPath,
                BuildDocument("Customer", DiagramDocument.CurrentVersion)
            );

            File.ReadAllText(atomicPath).Should().Be(File.ReadAllText(plainPath));
        }
        finally
        {
            DeleteIfExists(plainPath);
            DeleteIfExists(atomicPath);
        }
    }

    /// <summary>指定のテーブル名・フォーマットバージョンでエンティティ 1 件の保存文書を組み立てる</summary>
    /// <remarks>エンティティ Id は固定にして、Save と SaveAtomic の出力を比較できるようにする</remarks>
    private static DiagramDocument BuildDocument(string tableName, int version) =>
        new()
        {
            Version = version,
            Schema = new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities =
                {
                    new Entity
                    {
                        Id = new Guid("22222222-0000-0000-0000-000000000001"),
                        TableName = tableName,
                    },
                },
            },
        };

    /// <summary>存在すればファイルを削除する（後始末用）</summary>
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>保存先に残った一時ファイル（<c>{path}.{GUID}.tmp</c>）を列挙する</summary>
    /// <remarks>
    /// 一時ファイル名には同時保存の衝突回避のため GUID が挟まるため、固定名ではなくワイルドカードで探す
    /// （固定名 <c>{path}.tmp</c> のままだと、どんな残骸があっても常に緑になる空振りの検証になる）。
    /// </remarks>
    private static string[] FindTemporaryLeftovers(string path) =>
        Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp");

    /// <summary>保存先に残った一時ファイルをすべて削除する（後始末用）</summary>
    private static void DeleteTemporaryLeftovers(string path)
    {
        foreach (var leftover in FindTemporaryLeftovers(path))
        {
            DeleteIfExists(leftover);
        }
    }

    /// <summary>CurrentVersion より新しいフォーマットバージョンでエンティティ 1 件の文書を一時ファイルへ保存する</summary>
    private static string SaveNewerFormatDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-newer-{Guid.NewGuid()}.json");
        var document = new DiagramDocument
        {
            Version = DiagramDocument.CurrentVersion + 1,
            Schema = new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { new Entity { TableName = "T1" } },
            },
        };
        JsonStorageService.Save(path, document);
        return path;
    }
}
