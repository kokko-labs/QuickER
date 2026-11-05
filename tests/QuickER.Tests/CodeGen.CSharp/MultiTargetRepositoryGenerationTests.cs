using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// QuickER 版 Repository のマルチターゲット生成（複数方言）の Generator 側基盤を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 本フェーズ（M1）で提供するのは「オプションの実効方言解決・後方互換」「マルチ辞書での型解決 API」
/// 「方言間 C# 型不一致の診断」。sqlserver がターゲットに含まれるときの <c>[SqlColumnType]</c> 補完・契約の 1 回出力と方言別 namespace の実装・Repository 名前空間がルートの外のときの using は、決まった行（<c>CodegenCaseTests</c> の <c>code-multitarget-*</c>）が持つ。
/// </para>
/// <para>
/// 方言別 namespace への実装分割（別ファイル／連結レイアウト）はランタイム <c>SqlQuery&lt;T&gt;</c> が
/// 方言別 SQL を型内へ埋め込む現行設計のため未着手（報告参照）。ここではその手前までの Generator 基盤を守る。
/// </para>
/// </remarks>
public sealed class MultiTargetRepositoryGenerationTests
{
    /// <summary>2 エンティティ・1対多・int/string/decimal のみ（方言可搬）の小さな ER 図</summary>
    private static ErDiagram BuildDiagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "varchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = orderPk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = "decimal(10,2)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    ColumnPairs = [new(customerPk, orderFk)],
                },
            ],
        };
    }

    // ---- オプション後方互換・実効方言解決 ----

    [Fact(DisplayName = "RepositoryDialects（単一要素）指定は実効方言 1 つへ解決される")]
    public void EffectiveDialects_SingleElement_ResolvesToOne()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = ["sqlite"] };
        options.ResolveRepositoryDialects().Should().Equal("sqlite");
    }

    [Fact(DisplayName = "空リストは既定 sqlserver へフォールバックする")]
    public void EffectiveDialects_EmptyListFallsBackToSqlServer()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = [] };
        options.ResolveRepositoryDialects().Should().Equal("sqlserver");
    }

    [Fact(DisplayName = "未指定（null）は既定 sqlserver へフォールバックする")]
    public void EffectiveDialects_NullFallsBackToSqlServer()
    {
        var options = new CodeGenerationOptions();
        options.ResolveRepositoryDialects().Should().Equal("sqlserver");
    }

    [Fact(DisplayName = "実効方言は重複を大小無視で除去し順序を保つ")]
    public void EffectiveDialects_DeduplicatesCaseInsensitive()
    {
        var options = new CodeGenerationOptions
        {
            RepositoryDialects = ["SqlServer", "sqlite", "sqlserver"],
        };
        options.ResolveRepositoryDialects().Should().Equal("sqlserver", "sqlite");
    }

    /// <summary>
    /// 大小違いの綴りが正規綴りへ置換されて返ることを検証する。原綴りのまま返すと、下流の方言分岐は
    /// Ordinal 比較のため <c>"SQLite"</c> 指定で SQL Server 側の基底＋SQLite の using が混ざった
    /// コンパイル不能な出力が診断なしで出る
    /// </summary>
    [Fact(DisplayName = "実効方言は正規綴り（SupportedRepositoryDialects 側）へ置換して返す")]
    public void EffectiveDialects_NormalizesSpellingToCanonical()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = ["SQLite", "SQLSERVER"] };
        options.ResolveRepositoryDialects().Should().Equal("sqlite", "sqlserver");
    }

    [Fact(DisplayName = "空要素・空白は除去される")]
    public void EffectiveDialects_TrimsAndDropsBlanks()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = [" sqlserver ", "", "  "] };
        options.ResolveRepositoryDialects().Should().Equal("sqlserver");
    }

    [Fact(DisplayName = "未対応方言＋GenerateRepositories=true は ArgumentException になる")]
    public void EffectiveDialects_UnsupportedThrows()
    {
        var options = new CodeGenerationOptions
        {
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "postgres"],
        };
        var act = () => _ = options.ResolveRepositoryDialects();
        act.Should().Throw<ArgumentException>().WithMessage("*postgres*");
    }

    [Fact(
        DisplayName = "未対応方言を含むオプションも文字列化できる（record の ToString が例外を投げない）"
    )]
    public void ToString_WithUnsupportedDialect_DoesNotThrow()
    {
        // record の ToString は公開プロパティをすべて読む。方言の解決（未対応で投げる）がプロパティだと、
        // デバッガ・ログ・テストの表示名でオプションを文字列化するだけで例外になる
        var options = new CodeGenerationOptions
        {
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "postgres"],
        };

        options.ToString().Should().Contain("GenerateRepositories = True");
    }

    /// <summary>
    /// QuickER 版 Repository を生成しない構成では方言指定が生成物に影響しないため、未対応方言でも
    /// 例外にせず既定 sqlserver へ倒すことを検証する（CLI が図の方言名を焼き込む経路の受け皿）
    /// </summary>
    [Fact(DisplayName = "未対応方言＋Repository 非生成は sqlserver へフォールバックする")]
    public void EffectiveDialects_UnsupportedWithoutRepositories_FallsBackToSqlServer()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = ["postgresql"] };
        options.ResolveRepositoryDialects().Should().Equal("sqlserver");
    }

    /// <summary>
    /// 対応方言と未対応方言が混在する場合も、対応分だけを残す部分フィルタではなくリスト丸ごと
    /// 既定 sqlserver へ倒すことを検証する（GUI の空リスト時と同一の既定へ揃えるため）
    /// </summary>
    [Fact(DisplayName = "対応方言と未対応方言の混在もリスト丸ごと sqlserver へ倒れる")]
    public void EffectiveDialects_MixedUnsupportedWithoutRepositories_FallsBackWholeList()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = ["sqlite", "postgresql"] };
        options.ResolveRepositoryDialects().Should().Equal("sqlserver");
    }

    [Fact(
        DisplayName = "未対応方言＋GenerateRepositories=true は生成時に診断エラーへ変換される（例外を投げない）"
    )]
    public void Generate_UnsupportedDialect_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>
        {
            ["sqlserver"] = primary,
        };
        var options = new CodeGenerationOptions
        {
            GenerateRepositories = true,
            RepositoryDialects = ["bogus"],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains("bogus")
            );
    }

    /// <summary>
    /// 層別出力でも未対応方言が診断エラーへ変換される（例外を投げない）ことを検証する。
    /// 層フォルダ検証がファイル計画経由で実効方言に触れるため、方言解決が検証より後だと
    /// ArgumentException が診断でなく例外のまま呼び出し元へ抜けていた
    /// </summary>
    [Fact(DisplayName = "層別出力×未対応方言も診断エラーへ変換される（例外を投げない）")]
    public void Generate_UnsupportedDialect_WithLayeredOutput_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var options = new CodeGenerationOptions
        {
            GenerateRepositories = true,
            RepositoryDialects = ["bogus"],
            LayeredOutput = true,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, primary, options);

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains("bogus")
            );
    }

    // ---- マルチ辞書 API: 単一方言と同等の出力（後方互換） ----

    [Fact(DisplayName = "単一方言のマルチ辞書呼び出しは 3 引数版と同じ出力になる（後方互換）")]
    public void Generate_SingleDialectViaMultiApi_MatchesLegacyOverload()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var options = new CodeGenerationOptions { RootNamespace = "Sample.Domain" };

        var legacy = new CSharpCodeGenerationService().Generate(diagram, primary, options);
        var viaMulti = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>
            {
                ["sqlserver"] = primary,
            },
            options
        );

        viaMulti.Files.Select(f => f.Content).Should().Equal(legacy.Files.Select(f => f.Content));
    }

    // ---- 型不一致診断 ----

    [Fact(DisplayName = "方言間で共有 Entity の C# 型が食い違うと診断エラーになる")]
    public void Generate_TypeMismatchBetweenDialects_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);

        // sqlite 辞書を人工的に食い違わせる（customer_id を string にする）
        var customerId = diagram.Entities[0].Columns[0].Id;
        var tampered = new Dictionary<Guid, CSharpTypeInfo>(
            SqliteCSharpTypeMapper.ResolveColumnTypes(diagram)
        )
        {
            [customerId] = new CSharpTypeInfo { TypeName = "string", IsReferenceType = true },
        };

        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = tampered,
        };

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.Contains("customer_id")
                // 型不一致診断であることを表示言語に依存しないトークン（方言名・食い違う型名）で確認する
                && d.Message.Contains("sqlite")
                && d.Message.Contains("string")
            );
    }

    // 方言間で C# 型が一致する（可搬型）なら診断エラーは出ない：決まった行 diag-multitarget-matching-types-no-mismatch-error
    // （型辞書を食い違わせる Generate_TypeMismatchBetweenDialects_ReturnsErrorDiagnostic は、改ざんした辞書を直接渡すので入口を通せず残す）

    // ---- M2b: マルチターゲットレイアウト（契約 1 回＋方言別 namespace 実装＋keyed DI） ----

    // 非分割の連結ファイルが自ファイル内の名前空間を using しないこと・方言別 DI 拡張と keyed 版・分割の契約／固定 infra／方言別ファイルへの ADO 依存の置き場は、
    // 決まった行 code-multitarget-nonsplit-omits-own-namespace-using / code-multitarget-nonsplit-dialect-named-di-and-keyed-overloads /
    // code-multitarget-split-ado-usings-placed-per-dialect-file（CodegenCases.ColumnText.cs）

    // マルチ方言＋EF Core の排他エラー・Repository 非生成のときの非発動・keyed 利用を同梱したコンパイルは、決まった行（CodegenCaseTests）
    // diag-multitarget-efcore-is-exclusive / diag-multitarget-efcore-without-repositories-is-not-exclusive /
    // compile-multitarget-nonsplit-keyed-resolution・compile-multitarget-split-keyed-resolution
}
