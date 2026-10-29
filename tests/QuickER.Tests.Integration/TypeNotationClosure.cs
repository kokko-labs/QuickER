using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Integration;

/// <summary>
/// 「図に書いた型表記で同期 → 取り込み直す → 差分 0」を方言ごとに確かめる共有ヘルパー（表記の閉包テスト）。
/// </summary>
/// <remarks>
/// <para>
/// DB は宣言の綴りをそのまま保存するとは限らない（SQL Server の <c>numeric(18,0)</c> は <c>numeric(18)</c>、
/// PostgreSQL の <c>timestamp</c> は <c>timestamp(6)</c> として返る）。図と取込を素のまま比べると、
/// 同期しても取り込み直した表記が変わらないので、消えない <c>ALTER COLUMN</c> が差分に出続ける。差分は
/// 両側を方言の正規化（<see cref="SyncDialectCapabilities.NormalizeTypeNotation"/>）に通してから比べるので、
/// ここでは型の選択肢と <see cref="ITypeCatalog.TryFormat"/> の出力のすべてについて
/// (a) 正規化した図の表記が取込の表記と一致すること (b) 取り込み直した後の差分に AlterColumn が無いこと、
/// の 2 つを確かめる。失敗は型ごとに名指しする。
/// </para>
/// <para>
/// 型ごとに 1 テーブル（主キー列＋その型の列 1 本）を作る。1 テーブルに並べると、行サイズの上限
/// （MySQL）や 1 テーブル 1 本の制約（SQL Server の rowversion）に当たるため。
/// </para>
/// </remarks>
internal static class TypeNotationClosure
{
    /// <summary>方言を問わず TryFormat に通す代表的な正規型（精度・長さの有無を両方含める）</summary>
    public static IReadOnlyList<CanonicalType> DefaultSamples { get; } =
    [
        new(CanonicalTypeKind.Boolean),
        new(CanonicalTypeKind.TinyInt),
        new(CanonicalTypeKind.SmallInt),
        new(CanonicalTypeKind.Int32),
        new(CanonicalTypeKind.Int64),
        new(CanonicalTypeKind.Decimal, Precision: 10, Scale: 2),
        new(CanonicalTypeKind.Decimal, Precision: 10, Scale: 0),
        new(CanonicalTypeKind.Decimal, Precision: 18),
        new(CanonicalTypeKind.Decimal),
        new(CanonicalTypeKind.Float32),
        new(CanonicalTypeKind.Float64),
        new(CanonicalTypeKind.Money),
        new(CanonicalTypeKind.String, Length: 50),
        new(CanonicalTypeKind.String, Length: -1),
        new(CanonicalTypeKind.AnsiString, Length: 100),
        new(CanonicalTypeKind.AnsiString, Length: -1),
        new(CanonicalTypeKind.FixedString, Length: 10),
        new(CanonicalTypeKind.AnsiFixedString, Length: 10),
        new(CanonicalTypeKind.Binary, Length: 16),
        new(CanonicalTypeKind.Binary, Length: -1),
        new(CanonicalTypeKind.FixedBinary, Length: 16),
        // 方言の上限ちょうどの長さ（TryFormat が上限を超える長さを断る方言では、超えない側の表記として閉じているかを見る）。
        // SQL Server：nvarchar 4000・varchar / varbinary 8000。Oracle：NVARCHAR2 2000・VARCHAR2 4000・RAW 2000
        new(CanonicalTypeKind.String, Length: 4000),
        new(CanonicalTypeKind.String, Length: 2000),
        new(CanonicalTypeKind.AnsiString, Length: 8000),
        new(CanonicalTypeKind.AnsiString, Length: 4000),
        new(CanonicalTypeKind.Binary, Length: 8000),
        new(CanonicalTypeKind.Binary, Length: 2000),
        new(CanonicalTypeKind.Date),
        new(CanonicalTypeKind.Time),
        new(CanonicalTypeKind.Time, Precision: 3),
        new(CanonicalTypeKind.DateTime),
        new(CanonicalTypeKind.DateTime, Precision: 3),
        new(CanonicalTypeKind.DateTimeOffset),
        new(CanonicalTypeKind.DateTimeOffset, Precision: 3),
        new(CanonicalTypeKind.Guid),
        new(CanonicalTypeKind.Xml),
        new(CanonicalTypeKind.Json),
        new(CanonicalTypeKind.RowVersion),
    ];

    /// <summary>
    /// Oracle の精度の上限なしの整数（<c>INTEGER</c>）を、方言切替（<see cref="DiagramTypeConverter.CreatePlan"/>）で
    /// <paramref name="target"/> へ変換した型を返す（桁の上限を付けて変換したことも表明する）
    /// </summary>
    /// <remarks>
    /// 方言切替が書く型（<c>decimal(38,0)</c> / <c>numeric(38,0)</c>）も、DDL として通り取り込み直して差分が出ないことを、
    /// 変換の出力そのものを閉包の候補へ足して確かめるために使う（表記を写さない）
    /// </remarks>
    public static string ConvertedOracleInteger(IDatabaseProvider target)
    {
        var entity = new Entity { TableName = "t" };

        entity.Columns.Add(new Column { Name = "c", DataType = "INTEGER" });

        var diagram = new ErDiagram();

        diagram.Entities.Add(entity);

        var conversion = DiagramTypeConverter
            .CreatePlan(diagram, new OracleTypeCatalog(), target.TypeCatalog)
            .Converted.Should()
            .ContainSingle()
            .Subject;

        conversion.PrecisionCapped.Should().BeTrue(target.Name);
        return conversion.NewType!;
    }

    /// <summary>型の選択肢と、代表的な正規型を <see cref="ITypeCatalog.TryFormat"/> した表記を重複なく集める</summary>
    /// <param name="catalog">方言の型カタログ</param>
    /// <param name="samples">TryFormat に通す代表的な正規型（書き出せないものは自動的に除かれる）</param>
    public static IReadOnlyList<string> CandidateTypes(
        ITypeCatalog catalog,
        IEnumerable<CanonicalType> samples
    )
    {
        var types = new List<string>(catalog.DataTypes);

        foreach (var sample in samples)
        {
            if (catalog.TryFormat(sample, out var native))
            {
                types.Add(native);
            }
        }

        return types.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 型ごとのテーブルを同期で作り、取り込み直して、正規化の一致と差分 0 を表明する。
    /// </summary>
    /// <param name="provider">対象方言のプロバイダ</param>
    /// <param name="settings">接続設定（他のテストのテーブルが無い DB・スキーマを指していること）</param>
    /// <param name="types">確かめる型表記</param>
    /// <param name="tablePrefix">テーブル名の接頭辞</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task AssertClosedAsync(
        IDatabaseProvider provider,
        DbConnectionSettings settings,
        IReadOnlyList<string> types,
        string tablePrefix,
        CancellationToken ct
    )
    {
        var capabilities = provider.SyncCapabilities;
        var target = new List<Entity>();
        var declaredByTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < types.Count; i++)
        {
            var tableName = tablePrefix + i.ToString("D2", CultureInfo.InvariantCulture);
            declaredByTable[tableName] = types[i];
            target.Add(
                new Entity
                {
                    TableName = tableName,
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = provider.TypeCatalog.DefaultDataType,
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "v",
                            DataType = types[i],
                            IsNullable = true,
                        },
                    },
                }
            );
        }

        // ---------- 図に書いた表記のまま同期でテーブルを作る ----------
        var connectionString = provider.BuildConnectionString(settings);
        var live = await provider.SchemaImporter.ImportAsync(connectionString, 60, ct);
        var create = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            target,
            [],
            capabilities
        );

        foreach (var item in create.Items)
        {
            item.IsSelected = item.Kind == SchemaDiffKind.AddTable;
        }

        create
            .Items.Count(i => i.Kind == SchemaDiffKind.AddTable)
            .Should()
            .Be(target.Count, "型ごとのテーブルがすべて新規作成の差分になること");

        var script = provider.SyncScriptBuilder.Build(
            new SyncPlanner().BuildPlan(
                create.Items,
                capabilities,
                new SyncPlanContext
                {
                    LiveEntities = live.Entities,
                    LiveRelationships = live.Relationships,
                    AuxiliaryObjects = live.AuxiliaryObjects,
                    TableCreateSql = live.TableCreateSql,
                }
            )
        );
        var result = await provider.SyncExecutor.ExecuteAsync(settings, script, ct);
        result
            .Committed.Should()
            .BeTrue($"型ごとのテーブルの作成に失敗: {result.Error}{Environment.NewLine}{script}");

        // ---------- 取り込み直して、正規化した図の表記と取込の表記を突き合わせる ----------
        var reimported = await provider.SchemaImporter.ImportAsync(connectionString, 60, ct);
        var mismatches = new List<string>();

        foreach (var (tableName, declared) in declaredByTable)
        {
            var entity = reimported.Entities.SingleOrDefault(e =>
                string.Equals(
                    SchemaDiffService.NormalizeTable(e),
                    tableName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (entity is null)
            {
                mismatches.Add($"{declared}: テーブル {tableName} が取り込まれなかった");
                continue;
            }

            var imported = entity.Columns.Single(c =>
                c.Name.Equals("v", StringComparison.OrdinalIgnoreCase)
            );
            var normalized = capabilities.NormalizeTypeNotation(declared);

            if (!DbTypeText.AreEquivalent(normalized, imported.DataType))
            {
                mismatches.Add(
                    $"{declared}: 正規化した図の表記 {normalized} ≠ 取込の表記 {imported.DataType}"
                );
            }
        }

        mismatches.Should().BeEmpty("図に書いた表記は、正規化すると取込の表記と一致すること");

        // ---------- 取り込み直した結果との差分に型の変更が残らない ----------
        var after = new SchemaDiffService().Compute(
            reimported.Entities,
            reimported.Relationships,
            target,
            [],
            capabilities
        );

        after
            .Items.Where(i => i.Kind == SchemaDiffKind.AlterColumn)
            .Select(i =>
                $"{declaredByTable.GetValueOrDefault(i.TableName, i.TableName)}: {i.Description}"
            )
            .Should()
            .BeEmpty("同期した直後の差分に、消えない ALTER COLUMN が残らないこと");
    }
}
