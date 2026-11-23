using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期の構成の行列のフィクスチャ（キーの型）ごとのアダプタ。生成物の型はフィクスチャごとに別の名前空間にあるので、
/// 登録・書き込み・同期をここで包み、行列の本体（<see cref="SyncConfigMatrixRuntimeTests"/>）は生成物の型を名指ししない。
/// </summary>
/// <remarks>
/// どの表も主キーを int で受け渡す（値オブジェクトのキーのフィクスチャは int から値オブジェクトを作る）。同期する行は各フィクスチャの
/// 親の表（注文）の 1 行。
/// </remarks>
internal interface ISyncConfigFixture : IMatrixAdapter
{
    /// <summary>リモートサービス（HTTP の差分ソースと同期のエンドポイント）を生成したフィクスチャか</summary>
    bool HasRemoteServices { get; }

    /// <summary>リモートサービスのルートの接頭辞（RemotePaths.DefaultPrefix）</summary>
    string RemotePrefix { get; }

    /// <summary>サーバー（SQL Server）の図</summary>
    ErDiagram ServerDiagram { get; }

    /// <summary>ローカル（SQLite）の図</summary>
    ErDiagram LocalDiagram { get; }

    /// <summary>
    /// ローカルの SQLite のリポジトリを登録する（<paramref name="key"/> が null ならキーなし。<paramref name="factoryAsInstance"/>
    /// なら、生成の登録の後でローカルの接続ファクトリを、同じ接続文字列の接続ファクトリのインスタンスで登録し直す＝シングルトン）
    /// </summary>
    void AddLocalRepositories(
        IServiceCollection services,
        object? key,
        string connectionString,
        bool factoryAsInstance = false
    );

    /// <summary>サーバーの SQL Server のリポジトリを登録する（<paramref name="key"/> が null ならキーなし）</summary>
    void AddServerRepositories(IServiceCollection services, object? key, string connectionString);

    /// <summary>キーの無い新しい注文に、Before フックが <paramref name="key"/> を決める Save フックを登録する</summary>
    void AddKeyAssigningHook(IServiceCollection services, int key);

    /// <summary>親の表（注文）の名前（未送信の一覧の表の名前）</summary>
    string ParentTableName { get; }

    /// <summary>同期する子の表（注文の明細）の名前（未送信の一覧の表の名前）</summary>
    string ChildTableName { get; }

    /// <summary>フィクスチャの同期する表の名前の全部（構築時の除外が無いときにトリガーを張る表）</summary>
    IReadOnlyList<string> SyncTableNames { get; }

    /// <summary>親を参照する版なしの子の表を持つフィクスチャか</summary>
    bool HasVersionlessChild { get; }

    /// <summary>AddGeneratedSyncSupport を呼ぶ（<paramref name="excludeParent"/> なら親の表を excludeFromSync へ渡す）</summary>
    void AddSyncSupport(
        IServiceCollection services,
        object? serverKey,
        object? localKey,
        bool excludeParent
    );

    /// <summary>AddGeneratedDirectSyncSources を呼ぶ</summary>
    void AddDirectSources(IServiceCollection services, object? serverKey);

    /// <summary>AddGeneratedHttpSyncSources をベースアドレスで呼ぶ</summary>
    void AddHttpSources(IServiceCollection services, string baseAddress);

    /// <summary>AddGeneratedHttpSyncSources を HttpClient のファクトリで呼ぶ</summary>
    void AddHttpSources(IServiceCollection services, Func<IServiceProvider, HttpClient> factory);

    /// <summary>AddGeneratedSyncEngine を呼ぶ（<paramref name="excludeParent"/> なら親の表を excludeFromSync へ渡す）</summary>
    void AddSyncEngine(IServiceCollection services, object? localKey, bool excludeParent);

    /// <summary>サーバー（SQL Server のリポジトリ・直結の差分ソース・同期のエンドポイント）を Kestrel で起動する</summary>
    Task<InProcessRemoteServer> StartRemoteServerAsync(
        string connectionString,
        CancellationToken ct
    );

    /// <summary>ローカルに新しい注文を書く</summary>
    /// <param name="provider">サービスプロバイダ</param>
    /// <param name="localKey">ローカルのリポジトリのキー</param>
    /// <param name="key">注文のキー</param>
    /// <param name="keyFromHook">キーを入れずに書く（フックが決める）か</param>
    /// <param name="graph">グラフ保存で書くか</param>
    /// <param name="ct">取り消し</param>
    Task WriteLocalAsync(
        IServiceProvider provider,
        object? localKey,
        int key,
        bool keyFromHook,
        bool graph,
        CancellationToken ct
    );

    /// <summary>リポジトリへ注文を直接挿入する（サーバーの他者の書き込み）</summary>
    Task InsertAsync(IServiceProvider provider, object? key, int id, CancellationToken ct);

    /// <summary>リポジトリにその注文があるか</summary>
    Task<bool> ExistsAsync(IServiceProvider provider, object? key, int id, CancellationToken ct);

    /// <summary>リポジトリの注文を直接の UpdateAsync で書き換える</summary>
    Task UpdateAsync(IServiceProvider provider, object? key, int id, CancellationToken ct);

    /// <summary>リポジトリへ注文の明細を直接挿入する</summary>
    Task InsertChildAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        int childId,
        CancellationToken ct
    );

    /// <summary>リポジトリにその明細があるか</summary>
    Task<bool> ChildExistsAsync(
        IServiceProvider provider,
        object? key,
        int childId,
        CancellationToken ct
    );

    /// <summary>リポジトリにある、その注文の明細の数</summary>
    Task<int> ChildCountAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        CancellationToken ct
    );

    /// <summary>
    /// 保存済みの注文を読んで書き換え（Updated）、キー <paramref name="childId"/> の新しい明細（Added）を足して注文のリポジトリで
    /// グラフ保存する（注文の行も同じ保存が書く）
    /// </summary>
    Task SaveNewChildAsync(
        IServiceProvider provider,
        object? localKey,
        int parentId,
        int childId,
        CancellationToken ct
    );

    /// <summary>
    /// 保存済みの注文を読み、キーの無い新しい明細（Added）を足してから明細だけを MarkRemoved し、注文（Unchanged）をグラフ保存する
    /// </summary>
    Task SaveNewChildRemovedAsync(
        IServiceProvider provider,
        object? localKey,
        int parentId,
        CancellationToken ct
    );

    /// <summary>
    /// 保存済みの注文を読み、キーの無い新しい明細（Added）と、<paramref name="versionlessChildId"/> が null でなければそのキーの
    /// 新しい版なしの子（Added）を足してから注文を MarkRemoved し、グラフ保存する（<paramref name="asList"/> なら複数の SaveAsync）
    /// </summary>
    Task SaveParentRemovedWithNewChildAsync(
        IServiceProvider provider,
        object? localKey,
        int parentId,
        int? versionlessChildId,
        bool asList,
        CancellationToken ct
    );

    /// <summary>リポジトリへ版なしの子を直接挿入する（<see cref="HasVersionlessChild"/> のフィクスチャだけ）</summary>
    Task InsertVersionlessChildAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        int childId,
        CancellationToken ct
    );

    /// <summary>リポジトリにその版なしの子があるか（<see cref="HasVersionlessChild"/> のフィクスチャだけ）</summary>
    Task<bool> VersionlessChildExistsAsync(
        IServiceProvider provider,
        object? key,
        int childId,
        CancellationToken ct
    );

    /// <summary>
    /// サーバーのリポジトリから、行が足した明細・版なしの子・注文を消す（無い行は飛ばす。サーバーの DB は全部の行で共有するので、
    /// 後の行の最初の同期・洗い替えがこの行の残りを降ろさないようにする）
    /// </summary>
    Task DeleteServerRowsAsync(
        IServiceProvider serverProvider,
        IReadOnlyList<int> childIds,
        IReadOnlyList<int> versionlessChildIds,
        IReadOnlyList<int> parentIds,
        CancellationToken ct
    );

    /// <summary>ローカルの未送信の行（DI から引いた新しいエンジンの <c>GetPendingChangesAsync</c> の行）</summary>
    Task<IReadOnlyList<SyncConfigPendingRow>> PendingAsync(
        IServiceProvider provider,
        CancellationToken ct
    );

    /// <summary>同期を 1 回流し、競合・打ち切りがあればその説明（無ければ null）と、降ろした行の数を返す</summary>
    /// <param name="provider">サービスプロバイダ</param>
    /// <param name="ct">取り消し</param>
    /// <param name="lastWriteWins">後勝ちモード（版なしの表も同期する）で流すか（既定は版ありのモード）</param>
    Task<(string? Problem, int Downloaded)> SyncAsync(
        IServiceProvider provider,
        CancellationToken ct,
        bool lastWriteWins = false
    );

    /// <summary>
    /// 洗い替えを 1 回流す（既定のモード＝後勝ち・ローカルの除外 blob は捨てる）。ローカルの全消しとサーバーの全行の流し込みが
    /// 例外なく終わることを確かめる
    /// </summary>
    Task RefreshAsync(IServiceProvider provider, CancellationToken ct);

    /// <summary>
    /// 登録の形の食い違い（<see cref="SyncConfigFixtures.RegistrationProblems"/>）。ローカルの接続ファクトリ・注文と明細の
    /// リポジトリ・差分ソース・サーバーのリポジトリと接続ファクトリ（HTTP の行では null）を解決し、ローカルの接続ファクトリから
    /// 接続を 1 回開いた後のトリガーの付いた表を読んで渡す
    /// </summary>
    IReadOnlyList<string> RegistrationProblems(
        IServiceProvider provider,
        object? localKey,
        object? serverKey,
        bool overHttp,
        bool parentExcluded
    );
}

/// <summary>ローカルの未送信の行の写し（新しいエンジンの未送信の一覧の 1 行。生成物の型はフィクスチャごとの名前空間にあるので写す）</summary>
/// <param name="Table">行の表の名前</param>
/// <param name="KeyText">行のキーのテキスト</param>
/// <param name="IsDelete">行の最後の変更が削除か</param>
internal sealed record SyncConfigPendingRow(string Table, string KeyText, bool IsDelete);

/// <summary>同期の構成の行列のフィクスチャの軸</summary>
/// <remarks>
/// 正本の値はここに置き、次元表（<see cref="SyncConfigDimensions.Table"/>）の Fixture の水準はこれから作る（次元表の行は軸の
/// アダプタを引くので、軸が次元表の静的な初期化に依存すると、どちらを先に触るかで初期化が循環する）。
/// </remarks>
internal static class SyncConfigFixtures
{
    /// <summary>軸の値の正本（キーの型）</summary>
    public static IReadOnlyList<string> Values { get; } =
    [SyncConfigDimensions.IntKey, SyncConfigDimensions.VoKey];

    /// <summary>軸</summary>
    public static MatrixAxis<ISyncConfigFixture> Axis { get; } =
        new(
            SyncConfigDimensions.Fixture,
            Values,
            [new SyncConfigIntKeyFixture(), new SyncConfigVoKeyFixture()]
        );

    /// <summary>開くたびの確認がローカルの同期する表に張るトリガーの名前の前置き</summary>
    public const string TriggerPrefix = "quicker_sync_trg_";

    /// <summary>開くたびの確認つきの接続ファクトリの型の名前（生成物の SQLite の側の型）</summary>
    private const string ObservedFactoryName = "ObservedSqlConnectionFactory";

    /// <summary>
    /// 接続を開き（包んだ接続ファクトリの接続なら、開くたびの確認がトリガーを張る）、名前が <see cref="TriggerPrefix"/> で
    /// 始まるトリガーの付いた表の名前を、重なりなしに名前の順で返す（接続は閉じる）
    /// </summary>
    public static IReadOnlyList<string> TriggerTables(DbConnection connection)
    {
        using (connection)
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, tbl_name FROM sqlite_master WHERE type = 'trigger'";
            using var reader = command.ExecuteReader();
            var tables = new SortedSet<string>(StringComparer.Ordinal);

            while (reader.Read())
            {
                if (reader.GetString(0).StartsWith(TriggerPrefix, StringComparison.Ordinal))
                {
                    tables.Add(reader.GetString(1));
                }
            }

            return [.. tables];
        }
    }

    /// <summary>
    /// 登録の形が意図どおりか：ローカルの接続ファクトリは開くたびの確認つきの包み（ObservedSqlConnectionFactory）・ローカルの注文と
    /// 明細のリポジトリは SQLite の側の生成のリポジトリの型そのもの（何にも包まない。構築時に除外した親の表も）・差分ソースは
    /// 転送経路の型（HTTP は HttpSyncServerSource・直結は DirectSyncSource）・サーバーのリポジトリは SQL Server の側の生成の
    /// リポジトリの型そのもの・サーバーの接続ファクトリは包まない・ローカルの接続を 1 回開いた後、トリガーは除外していない同期する表にだけ在る
    /// </summary>
    /// <remarks>
    /// リポジトリは、フィクスチャが渡す生成の型（<c>typeof</c>）と実際の型が同じかで見る。包み（デコレータ）で登録すると、解決した
    /// 型が包みの型になるので食い違う。接続ファクトリと差分ソースは、生成物の型がフィクスチャごとの名前空間にあるので、型の名前で見る
    /// （ジェネリックの型は `1 などの接尾辞を除いた名前）。
    /// サーバーの接続ファクトリは SQL Server の方言の契約で引くので、SQLite の側の包みの型にはなりえないが、登録の形の表明として
    /// 並べる。除外した親の表への書き込みが未送信に残らないことは、行列の流れが未送信の一覧で確かめる（一覧はエンジンの表の行だけを
    /// 返すので、除外した表に記録の仕組みが無いことはトリガーの表明が受け持つ）。
    /// </remarks>
    /// <param name="localConnectionFactory">ローカルの接続ファクトリ（行のキーで引いたもの）</param>
    /// <param name="localRepository">ローカルの注文のリポジトリ</param>
    /// <param name="localChildRepository">ローカルの明細のリポジトリ</param>
    /// <param name="source">注文の差分ソース</param>
    /// <param name="serverRepository">サーバーの注文のリポジトリ（HTTP の行では null）</param>
    /// <param name="serverConnectionFactory">サーバーの接続ファクトリ（HTTP の行では null）</param>
    /// <param name="triggerTables">ローカルの接続を 1 回開いた後にトリガーの付いた表（<see cref="TriggerTables"/>）</param>
    /// <param name="synchronisedTables">トリガーが付くはずの表（同期する表から構築時に除外した表を除いたもの）</param>
    /// <param name="overHttp">サーバーが HTTP の向こうにいるか</param>
    /// <param name="parentExcluded">親の表を構築時に除外したか</param>
    /// <param name="expectedLocalRepository">ローカルの注文のリポジトリの生成の型（SQLite の側）</param>
    /// <param name="expectedLocalChildRepository">ローカルの明細のリポジトリの生成の型（SQLite の側）</param>
    /// <param name="expectedServerRepository">サーバーの注文のリポジトリの生成の型（SQL Server の側）</param>
    public static IReadOnlyList<string> RegistrationProblems(
        object localConnectionFactory,
        object localRepository,
        object localChildRepository,
        object source,
        object? serverRepository,
        object? serverConnectionFactory,
        IReadOnlyList<string> triggerTables,
        IReadOnlyList<string> synchronisedTables,
        bool overHttp,
        bool parentExcluded,
        Type expectedLocalRepository,
        Type expectedLocalChildRepository,
        Type expectedServerRepository
    )
    {
        var problems = new List<string>();

        if (localConnectionFactory.GetType().Name != ObservedFactoryName)
        {
            problems.Add(
                $"local connection factory is {localConnectionFactory.GetType().Name}, not {ObservedFactoryName}"
            );
        }

        if (localRepository.GetType() != expectedLocalRepository)
        {
            problems.Add(
                parentExcluded
                    ? $"local repository of the excluded parent table is {localRepository.GetType().FullName}, not the generated {expectedLocalRepository.FullName}"
                    : $"local repository is {localRepository.GetType().FullName}, not the generated {expectedLocalRepository.FullName}"
            );
        }

        if (localChildRepository.GetType() != expectedLocalChildRepository)
        {
            problems.Add(
                $"local child repository is {localChildRepository.GetType().FullName}, not the generated {expectedLocalChildRepository.FullName}"
            );
        }

        var expectedSource = overHttp ? "HttpSyncServerSource" : "DirectSyncSource";
        var sourceName = source.GetType().Name;
        var arity = sourceName.IndexOf('`');

        if ((arity < 0 ? sourceName : sourceName[..arity]) != expectedSource)
        {
            problems.Add($"server source is {sourceName}, not {expectedSource}");
        }

        if (serverRepository is not null && serverRepository.GetType() != expectedServerRepository)
        {
            problems.Add(
                $"server repository is {serverRepository.GetType().FullName}, not the generated {expectedServerRepository.FullName}"
            );
        }

        if (serverConnectionFactory?.GetType().Name == ObservedFactoryName)
        {
            problems.Add(
                "server connection factory is wrapped (only local connections are checked)"
            );
        }

        var expectedTables = synchronisedTables.Order(StringComparer.Ordinal).ToList();

        if (!triggerTables.SequenceEqual(expectedTables, StringComparer.Ordinal))
        {
            problems.Add(
                $"triggers are on [{string.Join(", ", triggerTables)}], not on [{string.Join(", ", expectedTables)}]"
            );
        }

        return problems;
    }
}
