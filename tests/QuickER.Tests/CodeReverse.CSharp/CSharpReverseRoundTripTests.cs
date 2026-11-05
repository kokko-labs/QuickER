using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// コードの取込（GUI のマージ）が主キーの採番の印を現在図から引き継ぐ手順を、後処理の段ごとに検証する。
/// </summary>
/// <remarks>
/// 図 → コード生成 → リバースの往復（テーブル/列・型・PK・NULL・説明・UNIQUE 制約・リレーションと外部キーのメタデータ・
/// 計算列・採番列）は形式の往復のエンジン（<c>FormatRoundTrip/</c> の形の行 <c>csharp-*</c>）が見る。
/// ここに残すのは、後処理の途中（採番の印を引き継ぐ前）の状態を表明するテストで、全体の経路（マージの行）では途中を観測できない。
/// </remarks>
public class CSharpReverseRoundTripTests
{
    /// <summary>
    /// コードの取込（GUI のマージ）は、主キーの採番の印を現在図から引き継ぎ、取り込み直しても置換の確認を出さないことを検証する
    /// </summary>
    /// <remarks>
    /// 引き継がないと印が消え、署名（<c>*IDENTITY</c>）の差として置換の確認が出る。GUI と同じ手順
    /// （Guid 引継マージ → 後処理）を通し、引き継ぐ前は確認が要ることも表明する（テストが空振りしないため）。
    /// </remarks>
    [Fact(
        DisplayName = "コードの取込は主キーの採番の印を現在図から引き継ぎ、取り込み直しても置換の確認を出さない"
    )]
    public void Merge_IdentityPrimaryKey_KeepsMarkAndNeedsNoReplacementConfirmation()
    {
        var (current, reversed) = ReverseIdentityPrimaryKeyDiagram();
        var merged = DiagramMergeReconciler.Reconcile(
            current,
            reversed.Entities.ToList(),
            reversed.Relationships.ToList(),
            preserveExistingMemo: true
        );
        var relationships = ReverseMergePostProcessor.Apply(
            current,
            merged.Entities,
            merged.Relationships,
            reversed.RelationshipMetadata
        );

        DiagramReplacementCheck
            .CanSkipConfirmation(false, current, merged, relationships)
            .Should()
            .BeFalse("引き継ぐ前は印が消えて署名が食い違う");

        ReverseMergePostProcessor.ApplyPrimaryKeyIdentityMarks(current, merged.Entities);

        merged.Entities.Single().Columns.Single().IsIdentity.Should().BeTrue();
        DiagramReplacementCheck
            .CanSkipConfirmation(false, current, merged, relationships)
            .Should()
            .BeTrue();
    }

    /// <summary>主キー item_id が採番列の 1 テーブルの図を生成し、生成コードをリバースする</summary>
    private static (
        ErDiagram Diagram,
        CodeReverseResult Reversed
    ) ReverseIdentityPrimaryKeyDiagram()
    {
        var diagram = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            IsIdentity = true,
                        },
                    },
                },
            },
        };
        var provider = new SqlServerProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.IdentityKey",
                OutputFileName = "IdentityKey.g.cs",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );
        generation.HasErrors.Should().BeFalse("主キーの採番列は警告であって生成は止めない");

        var reversed = new CSharpReverseParser().Parse(
            generation.Files.Single().Content,
            provider.TypeCatalog,
            IdentifierLengthLimit.Unlimited
        );

        return (diagram, reversed);
    }
}
