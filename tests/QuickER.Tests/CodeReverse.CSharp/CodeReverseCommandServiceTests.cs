using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using CodeGenStrings = QuickER.CodeGen.UI.Resources.Strings;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// <see cref="CodeReverseCommandService"/> の取込フロー（キャンセル中止・クエリ生存・0 件エラー提示）を検証する。
/// </summary>
/// <remarks>
/// <c>DbImportCommandServiceTests</c> の流儀を踏襲し、ホストは <see cref="StubErDiagramHost"/>、
/// ファイル選択・ダイアログはテスト用スタブへ差し替える。取込元 C# は実生成物を一時ファイルへ書き出す。
/// </remarks>
public class CodeReverseCommandServiceTests
{
    /// <summary>SQL Server 生成の本体 .g.cs を一時ファイルへ書き出し、選択結果を返す</summary>
    private static (FileDialogResult picked, string root) WriteGeneratedSource(ErDiagram diagram)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERReverseGuiTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);

        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Reverse.Gui.Sample",
                GenerateValueObjects = true,
                SplitFilesByCategory = false,
            }
        );

        var sourcePath = Path.Combine(root, "Model.g.cs");
        File.WriteAllText(sourcePath, result.Files.Single().Content);

        return (new FileDialogResult(sourcePath, 1), root);
    }

    /// <summary>SQL Server プロバイダのみを登録したレジストリ（現在図の方言解決に使う）</summary>
    private static DatabaseProviderRegistry SqlServerRegistry() =>
        new(new IDatabaseProvider[] { new SqlServerProvider() });

    /// <summary>ファイル選択のキャンセル（null）では、図の差し替えもダイアログ提示も行わない</summary>
    [Fact(DisplayName = "ファイル選択のキャンセルでは何もしない")]
    public void Run_Cancelled_DoesNothing()
    {
        var host = new StubErDiagramHost
        {
            DiagramToReturn = new ErDiagram(),
            ProvidersToReturn = SqlServerRegistry(),
        };
        var dialogs = new StubDialogService();
        var files = new StubFileDialogService { OpenResult = null };
        var service = new CodeReverseCommandService(host, dialogs, files);

        service.Run();

        host.LastReplacedDiagram.Should().BeNull();
        dialogs.ErrorMessages.Should().BeEmpty();
        dialogs.ConfirmMessages.Should().BeEmpty();
    }

    /// <summary>取込成功時は現在図の方言を維持し、参照が保たれるクエリが生存する</summary>
    [Fact(DisplayName = "取込でクエリが生存し TargetDbms は現在図の方言を維持する")]
    public void Run_Success_QuerySurvives_KeepsTargetDbms()
    {
        var entityId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var current = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    Id = entityId,
                    TableName = "customers",
                    Columns =
                    {
                        new Column
                        {
                            Id = columnId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
            Queries =
            {
                new QueryDefinition
                {
                    Name = "GetById",
                    EntityId = entityId,
                    Parameters =
                    {
                        new QueryParameter { Name = "id", SourceColumnId = columnId },
                    },
                },
            },
        };

        // 取込元コードは同じ customers（customer_id ＋ name 追加）＝構造差分ありだが参照は保たれる
        var codeDiagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "customers",
                    Columns =
                    {
                        new Column
                        {
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    },
                },
            },
        };
        var (picked, root) = WriteGeneratedSource(codeDiagram);

        try
        {
            var host = new StubErDiagramHost
            {
                DiagramToReturn = current,
                ProvidersToReturn = SqlServerRegistry(),
                TargetDbmsToReturn = "sqlserver",
            };
            var dialogs = new StubDialogService { ConfirmResult = true };
            var files = new StubFileDialogService { OpenResult = picked };
            var service = new CodeReverseCommandService(host, dialogs, files);

            service.Run();

            host.LastReplacedDiagram.Should().NotBeNull();
            host.LastReplacedDiagram!.TargetDbms.Should().Be("sqlserver");
            host.LastReplacedDiagram.Queries.Should()
                .ContainSingle()
                .Which.Name.Should()
                .Be("GetById");
            host.LastReplacedDiagram.Entities.Should().Contain(entity => entity.Id == entityId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>構造同一でも説明がコード由来の値で上書きされる場合は確認を出し、件数を文言へ載せる</summary>
    /// <remarks>
    /// 構造署名は説明を含まないため、実差分（<c>DescriptionOverwriteCount</c>）を見ないと
    /// 図に手書きした説明が無確認で消える。ここではその確認が出ることと件数の表示を固定する。
    /// </remarks>
    [Fact(DisplayName = "構造同一でも説明が上書きされる場合は確認を出し件数を載せる")]
    public void Run_SameStructureWithDescriptionOverwrite_Confirms()
    {
        // 取込元コードは説明を持たない（＝取り込むと現在図の手書き説明が消える）
        var codeDiagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "customers",
                    Columns =
                    {
                        new Column
                        {
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

        // 現在図は構造こそ同一だが、テーブル・列に手書きの説明を持つ
        var current = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    TableName = "customers",
                    Description = "手書きしたテーブル説明",
                    Columns =
                    {
                        new Column
                        {
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            Description = "手書きした列説明",
                        },
                    },
                },
            },
        };
        var (picked, root) = WriteGeneratedSource(codeDiagram);

        try
        {
            var host = new StubErDiagramHost
            {
                DiagramToReturn = current,
                ProvidersToReturn = SqlServerRegistry(),
                TargetDbmsToReturn = "sqlserver",
            };
            var dialogs = new StubDialogService { ConfirmResult = false };
            var files = new StubFileDialogService { OpenResult = picked };
            var service = new CodeReverseCommandService(host, dialogs, files);

            service.Run();

            // テーブル 1 件＋列 1 件＝2 件が上書き対象として文言へ載る
            var shown = dialogs.ConfirmMessages.Should().ContainSingle().Which;
            shown.Should().StartWith(CodeGenStrings.Reverse_ReplaceConfirm);
            shown
                .Should()
                .Contain(string.Format(CodeGenStrings.Reverse_DescriptionOverwriteWarning, 2));
            host.LastReplacedDiagram.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>構造同一かつ説明も一致する取込は、従来どおり無確認で続行する</summary>
    [Fact(DisplayName = "構造同一かつ説明も一致する取込は無確認のまま")]
    public void Run_SameStructureAndDescriptions_NoConfirm()
    {
        // 取込元コードと現在図で説明まで一致させる＝上書きで失われるものが無い
        var schema = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    TableName = "customers",
                    Description = "Customers",
                    Columns =
                    {
                        new Column
                        {
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            Description = "Customer identifier",
                        },
                    },
                },
            },
        };
        var (picked, root) = WriteGeneratedSource(schema);

        try
        {
            var host = new StubErDiagramHost
            {
                DiagramToReturn = schema,
                ProvidersToReturn = SqlServerRegistry(),
                TargetDbmsToReturn = "sqlserver",
            };
            var dialogs = new StubDialogService();
            var files = new StubFileDialogService { OpenResult = picked };
            var service = new CodeReverseCommandService(host, dialogs, files);

            service.Run();

            dialogs.ConfirmMessages.Should().BeEmpty();
            dialogs.WarningConfirmMessages.Should().BeEmpty();
            host.LastReplacedDiagram.Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>置換確認を拒否（キャンセル）すると図を差し替えない</summary>
    [Fact(DisplayName = "置換確認の拒否では図を差し替えない")]
    public void Run_ReplacementDeclined_DoesNotReplace()
    {
        var current = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    TableName = "existing",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

        // 取込元コードは別テーブル＝構造差分ありで確認ダイアログを誘発する
        var codeDiagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "imported",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };
        var (picked, root) = WriteGeneratedSource(codeDiagram);

        try
        {
            var host = new StubErDiagramHost
            {
                DiagramToReturn = current,
                ProvidersToReturn = SqlServerRegistry(),
                TargetDbmsToReturn = "sqlserver",
            };
            var dialogs = new StubDialogService { ConfirmResult = false };
            var files = new StubFileDialogService { OpenResult = picked };
            var service = new CodeReverseCommandService(host, dialogs, files);

            service.Run();

            dialogs
                .ConfirmMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(CodeGenStrings.Reverse_ReplaceConfirm);
            host.LastReplacedDiagram.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>ホストに未保存変更があるときの置換確認は、警告水準（ConfirmWarning）で表示される</summary>
    [Fact(DisplayName = "ダーティ時の置換確認は警告水準（Warning）になる")]
    public void Run_DirtyHost_UsesWarningConfirmation()
    {
        var current = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    TableName = "existing",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

        // 取込元コードは別テーブル＝構造差分ありで確認ダイアログを誘発する
        var codeDiagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "imported",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };
        var (picked, root) = WriteGeneratedSource(codeDiagram);

        try
        {
            // ホストは未保存変更あり（IsDirty=true）＝置換で編集内容が失われる状態
            var host = new StubErDiagramHost
            {
                DiagramToReturn = current,
                ProvidersToReturn = SqlServerRegistry(),
                TargetDbmsToReturn = "sqlserver",
                IsDirtyToReturn = true,
            };
            var dialogs = new StubDialogService { ConfirmResult = false };
            var files = new StubFileDialogService { OpenResult = picked };
            var service = new CodeReverseCommandService(host, dialogs, files);

            service.Run();

            // 未保存変更が失われる置換のため、警告水準の確認になる（通常確認は使わない）
            dialogs
                .WarningConfirmMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(CodeGenStrings.Reverse_ReplaceConfirm);
            dialogs.ConfirmMessages.Should().BeEmpty();
            host.LastReplacedDiagram.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>解析対象クラスが無いファイルは、案内メッセージのエラーダイアログで提示され差し替えない</summary>
    [Fact(DisplayName = "対象クラス 0 件は案内メッセージのエラーダイアログ")]
    public void Run_NoTargetClasses_ShowsError()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERReverseGuiTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Plain.cs");
        File.WriteAllText(
            sourcePath,
            "namespace Sample; public class Plain { public int Id { get; set; } }"
        );

        try
        {
            var host = new StubErDiagramHost
            {
                DiagramToReturn = new ErDiagram(),
                ProvidersToReturn = SqlServerRegistry(),
                TargetDbmsToReturn = "sqlserver",
            };
            var dialogs = new StubDialogService();
            var files = new StubFileDialogService
            {
                OpenResult = new FileDialogResult(sourcePath, 1),
            };
            var service = new CodeReverseCommandService(host, dialogs, files);

            service.Run();

            host.LastReplacedDiagram.Should().BeNull();
            dialogs
                .ErrorMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(ReverseStrings.Reverse_NoTargetClasses);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
