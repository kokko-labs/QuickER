using System.Linq;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.GeneratedBackendParitySqlServerFixture;

/// <summary>
/// 実装先どうしの一致の値の往復（QuickER 版 SQL Server・EF Core 版 SQL Server）の固定フィクスチャを生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 図は <see cref="BackendParityFixtureDefinition.Build"/>（customers → orders → order_lines・contacts）に、SQL Server の
/// 精度を持つ列の表 <c>value_probes</c> を 1 つ足した派生図。<c>value_probes</c> は値の往復で書く列をすべて持つ：
/// 小数秒の桁を持つ日時（<c>datetime2(2)</c>・<c>time(2)</c>・<c>datetimeoffset(2)</c>）と 7 桁の日時（<c>datetime2</c>・
/// <c>datetimeoffset</c>）・<c>date</c>・<c>decimal(10,2)</c>・Unicode でない文字列・固定長の文字列・Unicode の文字列・有界の
/// バイナリ・除外する無制限バイナリ。customers への NULL 許容の外部キーで、Include のある取得（QuickER 版は FOR JSON）でも読める。
/// </para>
/// <para>
/// オプションは SQL Server 方言の QuickER 版 Repository と EF Core の併存・値オブジェクトなし（製品の既定。範囲の外の値も
/// パラメータの束縛と SqlBulkCopy まで届く）・無制限バイナリ列の除外。インメモリ・リモート・EditModel は使わないので出さない。
/// </para>
/// </remarks>
public static class BackendParitySqlServerFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedBackendParitySqlServerFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "BackendParitySqlServerFixture.g.cs";

    /// <summary>フィクスチャ生成に用いる決定的なオプション</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = true,
            GenerateValueObjects = false,
            GenerateEfCoreRepositories = true,
            RepositoryDialects = ["sqlserver"],
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid CustomerId = new("b9000000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerPkColId = new("b9000000-0000-0000-0000-000000000002");

    private static readonly Guid ProbeId = new("b9500000-0000-0000-0000-000000000001");
    private static readonly Guid ProbePkColId = new("b9500000-0000-0000-0000-000000000002");
    private static readonly Guid ProbeCustomerColId = new("b9500000-0000-0000-0000-000000000003");
    private static readonly Guid ProbeAt2ColId = new("b9500000-0000-0000-0000-000000000004");
    private static readonly Guid ProbeClockColId = new("b9500000-0000-0000-0000-000000000005");
    private static readonly Guid ProbeAtOffsetColId = new("b9500000-0000-0000-0000-000000000006");
    private static readonly Guid ProbeAtOffset2ColId = new("b9500000-0000-0000-0000-000000000007");
    private static readonly Guid ProbeDayColId = new("b9500000-0000-0000-0000-000000000008");
    private static readonly Guid ProbeAnsiColId = new("b9500000-0000-0000-0000-000000000009");
    private static readonly Guid ProbeCodeColId = new("b9500000-0000-0000-0000-00000000000a");
    private static readonly Guid ProbeLabelColId = new("b9500000-0000-0000-0000-00000000000b");
    private static readonly Guid ProbeTagColId = new("b9500000-0000-0000-0000-00000000000c");
    private static readonly Guid ProbeAtColId = new("b9500000-0000-0000-0000-00000000000d");
    private static readonly Guid ProbeAmountColId = new("b9500000-0000-0000-0000-00000000000e");
    private static readonly Guid ProbePayloadColId = new("b9500000-0000-0000-0000-00000000000f");
    private static readonly Guid RelCustomerProbes = new("b9500000-0000-0000-0000-000000000101");

    /// <summary>値の往復の ER 図を決定的に構築する（<see cref="BackendParityFixtureDefinition.Build"/>＋<c>value_probes</c>）</summary>
    public static ErDiagram Build()
    {
        var diagram = BackendParityFixtureDefinition.Build();
        diagram.Entities.Add(
            new Entity
            {
                Id = ProbeId,
                TableName = "value_probes",
                Columns =
                {
                    new Column
                    {
                        Id = ProbePkColId,
                        Name = "probe_id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Id = ProbeCustomerColId,
                        Name = "customer_id",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = true,
                    },
                    Nullable(ProbeAt2ColId, "at2", "datetime2(2)"),
                    Nullable(ProbeClockColId, "clock", "time(2)"),
                    Nullable(ProbeAtOffsetColId, "at_offset", "datetimeoffset"),
                    Nullable(ProbeAtOffset2ColId, "at_offset2", "datetimeoffset(2)"),
                    Nullable(ProbeDayColId, "day", "date"),
                    Nullable(ProbeAnsiColId, "ansi", "varchar(20)"),
                    Nullable(ProbeCodeColId, "code", "nchar(10)"),
                    Nullable(ProbeLabelColId, "label", "nvarchar(20)"),
                    Nullable(ProbeTagColId, "tag", "varbinary(16)"),
                    Nullable(ProbeAtColId, "at", "datetime2"),
                    Nullable(ProbeAmountColId, "amount", "decimal(10,2)"),
                    Nullable(ProbePayloadColId, "payload", "varbinary(max)"),
                },
            }
        );
        diagram.Relationships.Add(
            new Relationship
            {
                Id = RelCustomerProbes,
                Type = RelationshipType.OneToMany,
                SourceEntityId = CustomerId,
                TargetEntityId = ProbeId,
                ColumnPairs = [new(CustomerPkColId, ProbeCustomerColId)],
                ConstraintName = "FK_value_probes_customers",
                OnDelete = ForeignKeyReferentialAction.NoAction,
                OnUpdate = ForeignKeyReferentialAction.NoAction,
            }
        );

        return diagram;
    }

    /// <summary>
    /// SQL Server の DDL を作る図（<see cref="Build"/> の <c>FK_contacts_fallback</c> の ON DELETE SET DEFAULT を NO ACTION にしたもの）
    /// </summary>
    /// <remarks>
    /// SQL Server は、同じ親から同じ子への参照動作つきの外部キーを 2 本以上作れない（<c>FK_contacts_backup</c> の SET NULL と
    /// 重なり、「複数のカスケードの道」として断る）。生成コードは <see cref="Build"/> の図から作ったものをそのまま使う
    /// （値の往復は顧客を削除しないので、この外部キーの参照動作に触れない）。
    /// </remarks>
    public static ErDiagram BuildForSqlServerDdl()
    {
        var diagram = Build();

        foreach (
            var relationship in diagram.Relationships.Where(r =>
                r.OnDelete == ForeignKeyReferentialAction.SetDefault
            )
        )
        {
            relationship.OnDelete = ForeignKeyReferentialAction.NoAction;
        }

        return diagram;
    }

    /// <summary>NULL 許容の普通の列</summary>
    private static Column Nullable(Guid id, string name, string dataType) =>
        new()
        {
            Id = id,
            Name = name,
            DataType = dataType,
            IsNullable = true,
        };
}
