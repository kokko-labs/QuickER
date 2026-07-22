using System.IO;
using FluentAssertions;
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
