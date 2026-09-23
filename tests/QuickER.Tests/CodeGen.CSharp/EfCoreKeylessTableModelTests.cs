using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 主キーの無いテーブル（取込で入るヒープ・ログ表が典型）を含む図で、生成 <c>QuickErDbContext</c> が
/// <b>そのテーブルを除外して例外なく使える</b>ことを実際にモデルを組み立てて確認するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// EF Core はマップされる全エンティティ型にキーを要求し、キーの無い型が 1 つでもモデルへ入ると
/// DbContext 全体が初回使用時に <c>requires a primary key to be defined</c> で例外になる。
/// 除外は QuickER 版 Repository のスキップ（単一主キーのみ）と同じ線＝DbSet・Fluent 構成・
/// 当該テーブルが絡むリレーション構成を出さず、<c>modelBuilder.Ignore&lt;T&gt;()</c> を明示して
/// 親側ナビゲーション経由の自動発見も塞ぎ、Warning でテーブルを名指しする。
/// </para>
/// <para>
/// FK の子として参加する形（親エンティティが keyless へのコレクションナビゲーションを持つ）が
/// 自動発見の混入経路なので、図は「PK あり親 → PK なし子」のリレーション入りで組む。
/// </para>
/// </remarks>
public class EfCoreKeylessTableModelTests
{
    /// <summary>customers（PK あり）→ logs（PK なし・FK の子）の図</summary>
    private static ErDiagram BuildDiagram()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var logCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var logMessage = new Column
        {
            Name = "message",
            DataType = "nvarchar(200)",
            IsNullable = true,
        };
        var logs = new Entity { TableName = "logs", Columns = { logCustomerId, logMessage } };

        return new ErDiagram
        {
            Entities = { customers, logs },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = logs.Id,
                    ColumnPairs = { new(customerId.Id, logCustomerId.Id) },
                },
            },
        };
    }

    [Fact(
        DisplayName = "主キーなしテーブルは DbContext から除外され、残りのモデルは例外なく使える"
    )]
    public void 主キーなしテーブルは除外されモデルは使える()
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            BuildDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.EfCoreKeyless",
                GenerateEfCoreRepositories = true,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        // Warning が除外したテーブルを名指しする
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning && d.Message.Contains("logs")
            );

        var code = string.Join("\n", result.Files.Select(f => f.Content));

        // DbSet・Fluent 構成は出ず、Ignore の明示だけが出る
        code.Should().Contain("modelBuilder.Ignore<LogEntity>();");
        code.Should().NotContain("DbSet<LogEntity>");
        code.Should().NotContain("modelBuilder.Entity<LogEntity>");

        // Repository 側と同じスキップ線＝keyless の EF Core Repository・DI 登録も出ない
        code.Should().NotContain("EfCoreLogRepository");

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.EfCoreKeyless.Tests.{Guid.NewGuid():N}"
        );

        using var context = CreateContext(assembly);

        // Model へのアクセスでモデル構築＋検証が走る（keyless が混入していればここで例外）
        var model = context.Model;

        model.FindEntityType("Test.EfCoreKeyless.LogEntity").Should().BeNull();

        // 親側のコレクションナビゲーション（自動発見の混入経路）もモデルへ紛れ込まない
        var customer = model.FindEntityType("Test.EfCoreKeyless.CustomerEntity")!;
        customer.GetNavigations().Should().BeEmpty();
    }

    /// <summary>生成アセンブリから QuickErDbContext を Sqlite 構成で生成する（実接続はしない）</summary>
    private static DbContext CreateContext(Assembly assembly)
    {
        var contextType =
            assembly.GetTypes().SingleOrDefault(type => type.Name == "QuickErDbContext")
            ?? throw new InvalidOperationException("QuickErDbContext が生成されていない");

        var builder = (DbContextOptionsBuilder)
            Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType)
            )!;
        builder.UseSqlite("Data Source=:memory:");

        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }
}
