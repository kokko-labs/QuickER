using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.Sqlite;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成 EditModel の DB 照合（<c>ValidateUniqueAsync</c>）を、インメモリの生成リポジトリで実際に動かして検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 固定するのは 2 つ。(1) 照合用の Entity へ EditModel の全列を写す＝カスタムチェック（<c>CollectCustomUniquenessChecks</c>）が
/// 制約の構成列でも主キーでもない列を読める（構成列と主キーだけを写していた頃は、その列が既定値で判定されていた）。
/// (2) 値型の構成列が未入力でも、その列を含む制約の結果だけを捨て、ほかの制約は照合する（未入力の列が 1 つでもあると、
/// 無関係な制約まで含めてメソッド全体が true を返し、DB 照合の所見もすべて消していた）。
/// </para>
/// <para>
/// 生成物の型を静的に使う検証コード（<see cref="Probe"/>）を生成物と一緒にコンパイルし、結果を 1 本の文字列で受け取る。
/// 図は値オブジェクトなし（値型の未入力が Entity の既定値 0 として現れる形）。
/// </para>
/// </remarks>
public class EditModelValidateUniqueExecutionTests
{
    /// <summary>生成物と一緒にコンパイルする検証コード</summary>
    private const string Probe = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;

        namespace Sample.Domain;

        public sealed partial class InMemoryItemRepository
        {
            partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<ItemEntity>>? checks) =>
                (checks ??= new List<UniquenessCheck<ItemEntity>>()).Add(
                    static (entity, cancellationToken) =>
                        Task.FromResult<UniquenessViolation?>(
                            entity.Note == "blocked"
                                ? new UniquenessViolation("CUSTOM_blocked_note", new[] { nameof(ItemEntity.Note) })
                                : null
                        )
                );
        }

        public static class ValidateUniqueProbe
        {
            private static string Errors(ItemEditModel model, string propertyName) =>
                ((IEnumerable)model.GetErrors(propertyName)).Cast<string>().Count().ToString();

            private static string Describe(bool result, ItemEditModel model) =>
                $"{result}/slot={Errors(model, nameof(ItemEditModel.BindingSlot))}"
                + $"/code={Errors(model, nameof(ItemEditModel.BindingCode))}"
                + $"/name={Errors(model, nameof(ItemEditModel.BindingName))}"
                + $"/note={Errors(model, nameof(ItemEditModel.BindingNote))}";

            public static async Task<string> RunAsync()
            {
                var items = new InMemoryItemRepository(new InMemoryDataStore());
                await items.InsertAsync(new ItemEntity { Id = 1, Slot = 0, Code = "A", Name = "taken" });

                // slot を未入力のまま、code は既存行と同じ "A"（未入力の slot が既定値 0 で照合されると (0, "A") の偽の重複）、
                // name は既存行と本当に重複する
                var unsetSlot = new ItemEditModel
                {
                    BindingId = "10",
                    BindingSlot = string.Empty,
                    BindingCode = "A",
                    BindingName = "taken",
                };
                var first = Describe(await unsetSlot.ValidateUniqueAsync(items), unsetSlot);

                // どの制約にも重複しないが、カスタムチェックが構成列でない note を読んで弾く
                var blockedNote = new ItemEditModel
                {
                    BindingId = "11",
                    BindingSlot = "3",
                    BindingCode = "C",
                    BindingName = "free",
                    BindingNote = "blocked",
                };
                var second = Describe(await blockedNote.ValidateUniqueAsync(items), blockedNote);

                return first + " " + second;
            }
        }
        """;

    [Fact(
        DisplayName = "DB 照合は全列を写してカスタムチェックへ渡し、未入力の値型の列を含む制約の結果だけを捨てる"
    )]
    public async Task ValidateUnique_CopiesEveryColumn_AndSkipsOnlyConstraintsWithUnsetValueTypes()
    {
        var probe = LoadProbe();

        var result = await (Task<string>)probe.GetMethod("RunAsync")!.Invoke(null, null)!;

        result.Should().Be("False/slot=0/code=0/name=1/note=0 False/slot=0/code=0/name=0/note=1");
    }

    /// <summary>図を生成し、検証コードと一緒にコンパイルして検証コードの型を返す</summary>
    private static Type LoadProbe()
    {
        var diagram = BuildDiagram();
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateMappers = false,
                GenerateRepositories = false,
                GenerateInMemoryRepositories = true,
                GenerateValueObjects = false,
            }
        );

        result.HasErrors.Should().BeFalse();

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.ValidateUnique.Tests.{Guid.NewGuid():N}",
            Probe
        );
        return assembly.GetType("Sample.Domain.ValidateUniqueProbe")!;
    }

    /// <summary>UQ(slot, code) と UQ(name) を持ち、どの制約にも属さない note を持つ items の図（SQLite）</summary>
    private static ErDiagram BuildDiagram()
    {
        var slot = new Column
        {
            Name = "slot",
            DataType = "INTEGER",
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "code",
            DataType = "TEXT",
            IsNullable = false,
        };
        var name = new Column
        {
            Name = "name",
            DataType = "TEXT",
            IsNullable = false,
        };
        var items = new Entity
        {
            TableName = "items",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                },
                slot,
                code,
                name,
                new Column { Name = "note", DataType = "TEXT" },
            ],
        };
        items.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { slot.Id, code.Id } });
        items.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { name.Id } });

        return new ErDiagram { TargetDbms = "sqlite", Entities = [items] };
    }
}
