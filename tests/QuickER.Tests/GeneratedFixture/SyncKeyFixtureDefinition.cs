using System;
using System.Collections.Generic;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.GeneratedSyncKeyFixture;

/// <summary>
/// 双方向同期支援（<see cref="CodeGenerationOptions.GenerateSyncSupport"/>）の版なしの表の主キーの型をそろえて並べる
/// フィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 版なし（<c>rowversion</c> 列の無い）同期の表の全量走査は、サーバー（SQL Server）でキー順のページング
/// （<c>SELECT TOP (@batchSize) … WHERE [key] &gt; @afterKey ORDER BY [key]</c>）をする。前のページの最後のキーは
/// 生 SQL のバインダが値の型から推定した型で束縛されるので、主キーの列の型と束縛の型の組ごとにページの境目の
/// 振る舞いが変わる。<see cref="SyncFixtureDefinition"/>（int の <c>sync_notes</c>）と <see cref="SyncModelFixtureDefinition"/>
/// （varchar の <c>model_labels</c>）は 1 つの型ずつしか持たないので、ここで型の表をそろえる。
/// </para>
/// <para>
/// 図は版なしの表だけ。各表は主キー 1 列とペイロードの列 <c>memo</c>（nvarchar(40)・NULL 許容）を持ち、外部キーは無い:
/// </para>
/// <list type="bullet">
///   <item><c>synckey_datetime2s</c>: <c>key_at</c>（<b>datetime2(7)</b>・PK）</item>
///   <item><c>synckey_datetimes</c>: <c>key_at</c>（<b>datetime</b>・PK）</item>
///   <item><c>synckey_dates</c>: <c>key_on</c>（<b>date</b>・PK）</item>
///   <item><c>synckey_offsets</c>: <c>key_at</c>（<b>datetimeoffset(7)</b>・PK）</item>
///   <item><c>synckey_guids</c>: <c>key_id</c>（<b>uniqueidentifier</b>・PK）</item>
///   <item><c>synckey_bigints</c>: <c>key_id</c>（<b>bigint</b>・PK）</item>
///   <item><c>synckey_decimals</c>: <c>key_value</c>（<b>decimal(18,4)</b>・PK）</item>
///   <item><c>synckey_varchars</c>: <c>key_code</c>（<b>varchar(20)</b>・PK）</item>
///   <item><c>synckey_nvarchars</c>: <c>key_code</c>（<b>nvarchar(20)</b>・PK）</item>
/// </list>
/// <para>
/// オプションは sqlserver / sqlite のマルチターゲットの QuickER 版 Repository ＋ 同期支援だけ。値オブジェクト・リモートサービス・
/// EditModel・Mapper・EF Core は生成しない（値オブジェクトのキーと HTTP の差分ソースを作らない理由は、この図を使う
/// ランタイムテスト <c>SyncSupportAdoSqlServerKeyPagingRuntimeTests</c> の XmlDoc に書く）。
/// </para>
/// </remarks>
public static class SyncKeyFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedSyncKeyFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "SyncKeyFixture.g.cs";

    /// <summary>期待する出力ファイル構成（ファイル構成そのものもドリフト検知の対象）</summary>
    public static IReadOnlyList<string> OutputFileNames { get; } = [OutputFileName];

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server（サーバー）／SQLite（ローカル）の QuickER 版 Repository ＋ 同期支援。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = true,
            GenerateValueObjects = false,
            GenerateEfCoreRepositories = false,
            GenerateSyncSupport = true,
            GenerateRemoteServices = false,
            RepositoryDialects = ["sqlserver", "sqlite"],
            SplitFilesByCategory = false,
        };

    /// <summary>表の名前（datetime2(7) の主キー）</summary>
    public const string DateTime2Table = "synckey_datetime2s";

    /// <summary>表の名前（datetime の主キー）</summary>
    public const string DateTimeTable = "synckey_datetimes";

    /// <summary>表の名前（date の主キー）</summary>
    public const string DateTable = "synckey_dates";

    /// <summary>表の名前（datetimeoffset(7) の主キー）</summary>
    public const string OffsetTable = "synckey_offsets";

    /// <summary>表の名前（uniqueidentifier の主キー）</summary>
    public const string GuidTable = "synckey_guids";

    /// <summary>表の名前（bigint の主キー）</summary>
    public const string BigIntTable = "synckey_bigints";

    /// <summary>表の名前（decimal(18,4) の主キー）</summary>
    public const string DecimalTable = "synckey_decimals";

    /// <summary>表の名前（varchar(20) の主キー）</summary>
    public const string VarCharTable = "synckey_varchars";

    /// <summary>表の名前（nvarchar(20) の主キー）</summary>
    public const string NVarCharTable = "synckey_nvarchars";

    /// <summary>同期支援の検証用 ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    /// <remarks>図の要素 ID は決定的でなければ再生成時に差分が出るため、表の番号から固定 GUID を組む。</remarks>
    public static ErDiagram Build()
    {
        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                KeyTable(1, DateTime2Table, "key_at", "datetime2(7)"),
                KeyTable(2, DateTimeTable, "key_at", "datetime"),
                KeyTable(3, DateTable, "key_on", "date"),
                KeyTable(4, OffsetTable, "key_at", "datetimeoffset(7)"),
                KeyTable(5, GuidTable, "key_id", "uniqueidentifier"),
                KeyTable(6, BigIntTable, "key_id", "bigint"),
                KeyTable(7, DecimalTable, "key_value", "decimal(18,4)"),
                KeyTable(8, VarCharTable, "key_code", "varchar(20)"),
                KeyTable(9, NVarCharTable, "key_code", "nvarchar(20)"),
            },
        };
    }

    /// <summary>図を SQLite 方言へ変換した複製を返す（ローカル DB 側のスキーマ）</summary>
    /// <remarks>
    /// 実運用の「SQL Server の図をローカル用に方言切替する」手順と同じ <see cref="DiagramTypeConverter"/> を通す。
    /// </remarks>
    public static ErDiagram BuildSqliteMirror()
    {
        var diagram = Build();
        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerTypeCatalog(),
            new SqliteTypeCatalog()
        );
        DiagramTypeConverter.Apply(diagram, plan);
        diagram.TargetDbms = "sqlite";

        return diagram;
    }

    /// <summary>
    /// 主辞書（図の方言＝SQL Server）と、実効方言（sqlserver / sqlite）ごとに解決した方言辞書を返す。
    /// </summary>
    public static (
        IReadOnlyDictionary<Guid, CSharpTypeInfo> Primary,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> ByDialect
    ) ResolveColumnTypes(ErDiagram diagram)
    {
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };

        return (primary, byDialect);
    }

    /// <summary>主キー 1 列とペイロードの列 <c>memo</c> を持つ版なしの表を組む</summary>
    /// <param name="number">表の番号（固定 GUID の材料・1 から 9）</param>
    /// <param name="tableName">表の名前</param>
    /// <param name="keyName">主キーの列の名前</param>
    /// <param name="keyType">主キーの列の型（SQL Server 表記）</param>
    private static Entity KeyTable(int number, string tableName, string keyName, string keyType) =>
        new()
        {
            Id = FixedId(number, 0),
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Id = FixedId(number, 1),
                    Name = keyName,
                    DataType = keyType,
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = FixedId(number, 2),
                    Name = "memo",
                    DataType = "nvarchar(40)",
                    IsNullable = true,
                },
            },
        };

    /// <summary>表の番号と要素の番号から固定 GUID を組む</summary>
    private static Guid FixedId(int table, int element) =>
        new(0xc5000000, 0, 0, 0, 0, 0, 0, 0, 0, (byte)table, (byte)element);
}
