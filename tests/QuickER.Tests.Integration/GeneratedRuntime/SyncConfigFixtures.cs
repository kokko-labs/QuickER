using System;
using System.Collections.Generic;
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
    /// ローカルの SQLite のリポジトリを登録する（<paramref name="key"/> が null ならキーなし。<paramref name="asInstances"/> なら
    /// 同期する表のリポジトリを、生成の登録の後でインスタンスの登録に置き換える＝シングルトン）
    /// </summary>
    void AddLocalRepositories(
        IServiceCollection services,
        object? key,
        string connectionString,
        bool asInstances = false
    );

    /// <summary>サーバーの SQL Server のリポジトリを登録する（<paramref name="key"/> が null ならキーなし）</summary>
    void AddServerRepositories(IServiceCollection services, object? key, string connectionString);

    /// <summary>キーの無い新しい注文に、Before フックが <paramref name="key"/> を決める Save フックを登録する</summary>
    void AddKeyAssigningHook(IServiceCollection services, int key);

    /// <summary>親の表（注文）の名前（ジャーナルの記録の表の名前）</summary>
    string ParentTableName { get; }

    /// <summary>同期する子の表（注文の明細）の名前（ジャーナルの記録の表の名前）</summary>
    string ChildTableName { get; }

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

    /// <summary>ローカルのジャーナルの記録（キーの決まった記録の表の名前と、キーの無い記録の数）</summary>
    Task<SyncConfigJournal> JournalAsync(
        IServiceProvider provider,
        object? localKey,
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
    /// 登録の形の食い違い（<see cref="SyncConfigFixtures.RegistrationProblems"/>）。ローカルの注文と明細のリポジトリ・差分ソース・
    /// サーバーのリポジトリ（HTTP の行では null）を解決して渡す
    /// </summary>
    IReadOnlyList<string> RegistrationProblems(
        IServiceProvider provider,
        object? localKey,
        object? serverKey,
        bool overHttp,
        bool parentExcluded
    );
}

/// <summary>ローカルのジャーナルの記録の写し</summary>
/// <param name="Tables">キーの決まった記録ごとの表の名前（記録の順）</param>
/// <param name="UnsetKeyEntries">キーの無い記録（エンジンの読み取りが返さない記録）の数</param>
internal sealed record SyncConfigJournal(IReadOnlyList<string> Tables, int UnsetKeyEntries);

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

    /// <summary>
    /// 契約の登録（<paramref name="key"/> が null ならキーなし）を、組み立て済みのインスタンスの登録（シングルトン）に置き換える
    /// </summary>
    public static void ReplaceWithInstance<TService>(
        IServiceCollection services,
        object? key,
        TService instance
    )
        where TService : class
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (
                services[index].ServiceType == typeof(TService)
                && Equals(services[index].ServiceKey, key)
            )
            {
                services.RemoveAt(index);
            }
        }

        services.Add(
            key is null
                ? new ServiceDescriptor(typeof(TService), instance)
                : new ServiceDescriptor(typeof(TService), key, instance)
        );
    }

    /// <summary>
    /// 登録の形が意図どおりか：ローカルの注文と明細のリポジトリはジャーナル記録デコレータ（Journaling…。構築時に除外した親の表も
    /// 包まれる）・差分ソースは転送経路の型（HTTP は HttpSyncServerSource・直結は DirectSyncSource）・サーバーのリポジトリは素のまま
    /// （記録するのはローカルの編集だけ）
    /// </summary>
    /// <remarks>
    /// 生成物の型はフィクスチャごとの名前空間にあるので、型の名前で見る（ジェネリックの型は `1 などの接尾辞を除いた名前）。
    /// 除外した親の表の直接の書き込みが記録しないことは、行列の流れがジャーナルで確かめる。
    /// </remarks>
    public static IReadOnlyList<string> RegistrationProblems(
        object localRepository,
        object localChildRepository,
        object source,
        object? serverRepository,
        bool overHttp,
        bool parentExcluded
    )
    {
        var problems = new List<string>();

        if (!localRepository.GetType().Name.StartsWith("Journaling", StringComparison.Ordinal))
        {
            problems.Add(
                parentExcluded
                    ? $"local repository of the excluded parent table is {localRepository.GetType().Name}, not the journaling decorator"
                    : $"local repository is {localRepository.GetType().Name}, not the journaling decorator"
            );
        }

        if (!localChildRepository.GetType().Name.StartsWith("Journaling", StringComparison.Ordinal))
        {
            problems.Add(
                $"local child repository is {localChildRepository.GetType().Name}, not the journaling decorator"
            );
        }

        var expectedSource = overHttp ? "HttpSyncServerSource" : "DirectSyncSource";
        var sourceName = source.GetType().Name;
        var arity = sourceName.IndexOf('`');

        if ((arity < 0 ? sourceName : sourceName[..arity]) != expectedSource)
        {
            problems.Add($"server source is {sourceName}, not {expectedSource}");
        }

        if (
            serverRepository is not null
            && serverRepository.GetType().Name.StartsWith("Journaling", StringComparison.Ordinal)
        )
        {
            problems.Add("server repository is decorated (only local edits are journaled)");
        }

        return problems;
    }
}
