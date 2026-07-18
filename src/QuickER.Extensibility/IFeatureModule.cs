using Microsoft.Extensions.DependencyInjection;

namespace QuickER.Extensibility;

/// <summary>
/// 着脱可能な機能モジュール（AI チャット・モック生成など）の入口となる契約。
/// </summary>
/// <remarks>
/// ホスト（QuickER.Gui）はモジュールを列挙し、DI コンテナへの登録（<see cref="ConfigureServices"/>）→
/// コンテナ構築 → ツールバー寄与の生成（<see cref="CreateToolbarItems"/>）の順に扱う。
/// アプリ終了時には <see cref="OnMainWindowClosing"/> で後始末を行う。
/// </remarks>
public interface IFeatureModule
{
    /// <summary>モジュール識別子（例: <c>"ai-chat"</c>）</summary>
    string Id { get; }

    /// <summary>DI コンテナ構築時に、自分が必要とするサービスを登録する</summary>
    /// <param name="services">登録先のサービスコレクション</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>コンテナ構築直後（ツールバー寄与の生成前）に呼ばれる。ホストイベントの購読などの初期化に使う</summary>
    /// <param name="services">構築済みのサービスプロバイダ</param>
    void Initialize(IServiceProvider services) { }

    /// <summary>コンテナ構築後、ホストのツールバーへ寄与するボタン群を生成する</summary>
    /// <param name="services">構築済みのサービスプロバイダ</param>
    /// <returns>ツールバーへ並べるボタン記述子の一覧</returns>
    IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services);

    /// <summary>
    /// メインウィンドウが閉じるとき（アプリ終了時）に呼ばれる。モードレスウィンドウの後始末などを行う。
    /// </summary>
    /// <param name="services">構築済みのサービスプロバイダ</param>
    void OnMainWindowClosing(IServiceProvider services);
}
