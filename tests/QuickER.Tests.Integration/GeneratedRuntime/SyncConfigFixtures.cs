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

    /// <summary>ローカルの SQLite のリポジトリを登録する（<paramref name="key"/> が null ならキーなし）</summary>
    void AddLocalRepositories(IServiceCollection services, object? key, string connectionString);

    /// <summary>サーバーの SQL Server のリポジトリを登録する（<paramref name="key"/> が null ならキーなし）</summary>
    void AddServerRepositories(IServiceCollection services, object? key, string connectionString);

    /// <summary>キーの無い新しい注文に、Before フックが <paramref name="key"/> を決める Save フックを登録する</summary>
    void AddKeyAssigningHook(IServiceCollection services, int key);

    /// <summary>AddGeneratedSyncSupport を呼ぶ</summary>
    void AddSyncSupport(IServiceCollection services, object? serverKey, object? localKey);

    /// <summary>AddGeneratedDirectSyncSources を呼ぶ</summary>
    void AddDirectSources(IServiceCollection services, object? serverKey);

    /// <summary>AddGeneratedHttpSyncSources をベースアドレスで呼ぶ</summary>
    void AddHttpSources(IServiceCollection services, string baseAddress);

    /// <summary>AddGeneratedHttpSyncSources を HttpClient のファクトリで呼ぶ</summary>
    void AddHttpSources(IServiceCollection services, Func<IServiceProvider, HttpClient> factory);

    /// <summary>AddGeneratedSyncEngine を呼ぶ</summary>
    void AddSyncEngine(IServiceCollection services, object? localKey);

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

    /// <summary>同期を 1 回流し、競合・打ち切りがあればその説明（無ければ null）と、降ろした行の数を返す</summary>
    Task<(string? Problem, int Downloaded)> SyncAsync(
        IServiceProvider provider,
        CancellationToken ct
    );

    /// <summary>
    /// 登録の形の食い違い（<see cref="SyncConfigFixtures.RegistrationProblems"/>）。ローカルのリポジトリ・差分ソース・
    /// サーバーのリポジトリ（HTTP の行では null）を解決して渡す
    /// </summary>
    IReadOnlyList<string> RegistrationProblems(
        IServiceProvider provider,
        object? localKey,
        object? serverKey,
        bool overHttp
    );
}

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
    /// 登録の形が意図どおりか：ローカルのリポジトリはジャーナル記録デコレータ（Journaling…）・差分ソースは転送経路の型
    /// （HTTP は HttpSyncServerSource・直結は DirectSyncSource）・サーバーのリポジトリは素のまま（記録するのはローカルの編集だけ）
    /// </summary>
    /// <remarks>
    /// 生成物の型はフィクスチャごとの名前空間にあるので、型の名前で見る（ジェネリックの型は `1 などの接尾辞を除いた名前）。
    /// </remarks>
    public static IReadOnlyList<string> RegistrationProblems(
        object localRepository,
        object source,
        object? serverRepository,
        bool overHttp
    )
    {
        var problems = new List<string>();

        if (!localRepository.GetType().Name.StartsWith("Journaling", StringComparison.Ordinal))
        {
            problems.Add(
                $"local repository is {localRepository.GetType().Name}, not the journaling decorator"
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
