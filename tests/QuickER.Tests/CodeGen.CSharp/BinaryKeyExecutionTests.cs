using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.Sqlite;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 主キーが <c>binary(n)</c>（<c>byte[]</c>）の表を、インメモリと SQLite の生成リポジトリで実際に動かし、
/// キーをバイト列の中身で照合することを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 配列は参照で比べられるので、キーを既定の等値で扱う箇所（インメモリのストアの辞書・ステージング・バッチのキー集合・
/// 自分自身の除外、Include の突き合わせ、<c>byte[]</c> のリストに対する <c>Contains</c>）は、同じバイト列のキーを
/// 別物として扱う。グラフ保存（ステージング）と一括挿入（バッチのキー集合）の経路も、同じキーの別の配列を混ぜて通す。
/// name に UNIQUE を付けてあるのは、グラフ保存の一意制約の検査（保存後の姿を組み立てる複製）を通すため。読み戻した実体は別の配列を持つので、GetById が見つからず、同じキーの 2 回目の挿入が通り、
/// 更新が行を見つけられない形で表に出る。
/// </para>
/// <para>
/// 生成物の型を静的に使う検証コード（<see cref="Probe"/>）を生成物と一緒にコンパイルし、結果を 1 本の文字列で受け取る。
/// SQLite は実ファイルの DB に生成した DDL を当てて動かす。
/// </para>
/// </remarks>
public class BinaryKeyExecutionTests
{
    /// <summary>生成物と一緒にコンパイルする検証コード（同じ手順をインメモリと SQLite の両方へ流す）</summary>
    private const string Probe = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        namespace Sample.Domain;

        public static class BinaryKeyProbe
        {
            public static Task<string> InMemoryAsync()
            {
                var store = new InMemoryDataStore();
                return RunAsync(new InMemoryBlobRepository(store), new InMemoryBlobTagRepository(store));
            }

            public static async Task<string> SqliteAsync(string connectionString)
            {
                await using var provider = new ServiceCollection()
                    .AddGeneratedSqliteRepositories(connectionString)
                    .BuildServiceProvider();
                return await RunAsync(
                    provider.GetRequiredService<IBlobRepository>(),
                    provider.GetRequiredService<IBlobTagRepository>()
                );
            }

            private static byte[] Key() => new byte[] { 1, 2, 3, 4 };

            private static byte[] Key(byte value) => new byte[] { value, value, value, value };

            private static async Task<string> RejectedAsync(Func<Task> action)
            {
                try
                {
                    await action();
                    return "accepted";
                }
                catch (Exception)
                {
                    return "rejected";
                }
            }

            private static async Task<string> RunAsync(IBlobRepository blobs, IBlobTagRepository tags)
            {
                var log = new List<string>();

                await blobs.InsertAsync(new BlobEntity { Hash = Key(), Name = "a" });
                log.Add("get:" + ((await blobs.GetByIdAsync(Key()))?.Name ?? "null"));

                try
                {
                    await blobs.InsertAsync(new BlobEntity { Hash = Key(), Name = "dup" });
                    log.Add("dup:inserted");
                }
                catch (Exception)
                {
                    log.Add("dup:rejected");
                }

                var read = (await blobs.GetByIdAsync(Key()))!;
                read.Name = "b";
                read.MarkUpdated();
                log.Add("update:" + await blobs.UpdateAsync(read));

                await tags.InsertAsync(new BlobTagEntity { TagId = 1, Hash = Key() });

                var withTags = await blobs.Query().Include(b => b.BlobTags).ToListAsync();
                log.Add("children:" + withTags.Single().BlobTags.Count);

                var withParent = await tags.Query().Include(t => t.Blob).ToListAsync();
                log.Add("parent:" + (withParent.Single().Blob?.Name ?? "null"));

                var keys = new List<byte[]> { Key() };
                log.Add("in:" + (await blobs.Query().Where(b => keys.Contains(b.Hash)).ToListAsync()).Count);

                // Graph save (the in-memory staging): insert a parent with its child, then update the parent read back
                var graph = new BlobEntity { Hash = Key(5), Name = "g" };
                graph.BlobTags.Add(new BlobTagEntity { TagId = 2, Hash = Key(5) });
                graph.MarkAdded(includeChildren: true);
                log.Add("graph-insert:" + await blobs.SaveAsync(graph));

                var graphRead = (await blobs.GetByIdAsync(Key(5)))!;
                graphRead.Name = "g2";
                graphRead.MarkUpdated();
                log.Add("graph-update:" + await blobs.SaveAsync(graphRead));
                log.Add("graph-read:" + ((await blobs.GetByIdAsync(Key(5)))?.Name ?? "null"));

                // Two roots carrying the same key (separate arrays) in one save are a duplicate key
                var first = new BlobEntity { Hash = Key(9), Name = "x" };
                var second = new BlobEntity { Hash = Key(9), Name = "y" };
                first.MarkAdded();
                second.MarkAdded();
                log.Add("graph-duplicate:" + await RejectedAsync(() => blobs.SaveAsync(new[] { first, second })));
                log.Add("graph-duplicate-left:" + ((await blobs.GetByIdAsync(Key(9)))?.Name ?? "null"));

                // One save removes a row read back and inserts another row with the same UNIQUE name
                await blobs.InsertAsync(new BlobEntity { Hash = Key(3), Name = "d" });
                var removed = (await blobs.GetByIdAsync(Key(3)))!;
                removed.MarkRemoved();
                var replacement = new BlobEntity { Hash = Key(4), Name = "d" };
                replacement.MarkAdded();
                log.Add("swap:" + await blobs.SaveAsync(new[] { removed, replacement }));
                log.Add("swap-old:" + ((await blobs.GetByIdAsync(Key(3)))?.Name ?? "null"));

                // A bulk insert whose batch repeats a key (separate arrays) is a duplicate key and inserts nothing
                log.Add(
                    "bulk-duplicate:"
                        + await RejectedAsync(() =>
                            blobs.BulkInsertAsync(
                                new[]
                                {
                                    new BlobEntity { Hash = Key(7), Name = "p" },
                                    new BlobEntity { Hash = Key(7), Name = "q" },
                                }
                            )
                        )
                );
                log.Add("bulk-left:" + ((await blobs.GetByIdAsync(Key(7)))?.Name ?? "null"));

                log.Add("rows:" + (await blobs.Query().ToListAsync()).Count);

                return string.Join(",", log);
            }
        }
        """;

    /// <summary>修正後のどちらの実装先でも同じになる結果</summary>
    private const string Expected =
        "get:a,dup:rejected,update:True,children:1,parent:b,in:1,"
        + "graph-insert:2,graph-update:1,graph-read:g2,"
        + "graph-duplicate:rejected,graph-duplicate-left:null,"
        + "swap:2,swap-old:null,"
        + "bulk-duplicate:rejected,bulk-left:null,"
        + "rows:3";

    [Fact(
        DisplayName = "インメモリは binary(n) の主キーを中身で照合する（取得・重複・更新・Include・IN・グラフ保存・一括挿入）"
    )]
    public async Task InMemory_MatchesBinaryKeysByContent()
    {
        var probe = LoadProbe();

        var result = await (Task<string>)probe.GetMethod("InMemoryAsync")!.Invoke(null, null)!;

        result.Should().Be(Expected);
    }

    [Fact(DisplayName = "SQLite の Include は binary(n) の主キーを中身で突き合わせる")]
    public async Task Sqlite_IncludeMatchesBinaryKeysByContent()
    {
        var probe = LoadProbe();
        var path = Path.Combine(Path.GetTempPath(), $"quicker-binary-key-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString();

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = new SqliteDdlGenerator().Build(BuildDiagram());
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var result = await (Task<string>)
                probe.GetMethod("SqliteAsync")!.Invoke(null, [connectionString])!;

            result.Should().Be(Expected);
        }
        finally
        {
            File.Delete(path);
        }
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
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
                GenerateInMemoryRepositories = true,
                GenerateEditModels = false,
                GenerateMappers = false,
            }
        );

        result.HasErrors.Should().BeFalse();

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.BinaryKey.Tests.{Guid.NewGuid():N}",
            Probe
        );
        return assembly.GetType("Sample.Domain.BinaryKeyProbe")!;
    }

    /// <summary>binary(4) の主キーを持つ blobs と、それを参照する blob_tags の図（SQLite）</summary>
    private static ErDiagram BuildDiagram()
    {
        var hash = new Column
        {
            Name = "hash",
            DataType = "BINARY(4)",
            IsPrimaryKey = true,
        };
        var name = new Column { Name = "name", DataType = "TEXT" };
        var blobs = new Entity { TableName = "blobs", Columns = [hash, name] };

        // UNIQUE の name を持たせ、インメモリのグラフ保存の一意制約の検査（保存後の姿を組み立てる ComposeView）も通す
        blobs.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { name.Id } });

        var tagHash = new Column { Name = "hash", DataType = "BINARY(4)" };
        var tags = new Entity
        {
            TableName = "blob_tags",
            Columns =
            [
                new Column
                {
                    Name = "tag_id",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                },
                tagHash,
            ],
        };

        return new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities = [blobs, tags],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = blobs.Id,
                    TargetEntityId = tags.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(hash.Id, tagHash.Id)],
                },
            ],
        };
    }
}
