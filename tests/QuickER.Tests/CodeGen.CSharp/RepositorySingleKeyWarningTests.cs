using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// QuickER 版 Repository は単一主キーの表だけを対象にし、複合主キー・主キーなしの表を警告で名指しして飛ばすことを検証する
/// </summary>
/// <remarks>扱いの表の B7a（C# 生成＝生成時の診断）を固定するテスト。</remarks>
public sealed class RepositorySingleKeyWarningTests
{
    private static Entity Table(string name, params (string Name, bool Key)[] columns)
    {
        var entity = new Entity { TableName = name };

        foreach (var (column, key) in columns)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = column,
                    DataType = "int",
                    IsPrimaryKey = key,
                    IsNullable = !key,
                }
            );
        }

        return entity;
    }

    [Theory(DisplayName = "複合主キー・主キーなしの表は、Repository を作らずに警告で名指しする")]
    [InlineData(true)]
    [InlineData(false)]
    public void Generate_NonSingleKeyTable_SkipsRepositoryWithWarning(bool composite)
    {
        var table = composite
            ? Table("order_lines", ("order_id", true), ("line_no", true))
            : Table("audit_logs", ("message", false));
        var diagram = new ErDiagram { Entities = { Table("customers", ("id", true)), table } };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                && diagnostic.Message
                    == string.Format(
                        Strings.CodeGen_Warning_RepositorySingleKeyOnly,
                        table.TableName
                    )
            );
    }
}
