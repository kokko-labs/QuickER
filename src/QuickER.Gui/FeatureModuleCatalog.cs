using QuickER.Extensibility;
#if QUICKER_AI_MODULES
using QuickER.AI.Chat;
using QuickER.AI.Mock;
#endif


namespace QuickER;

/// <summary>アプリに同梱するフィーチャーモジュールの静的カタログ</summary>
/// <remarks>
/// モジュールの着脱はこの一覧と csproj の ProjectReference の変更で行う。
/// ビルドプロパティ <c>IncludeAiModules=false</c>（既定 true）を指定すると、
/// AI 系モジュールへの参照ごと外れた「AI なし構成」をビルドできる
/// （csproj 側の条件付き ProjectReference と <c>QUICKER_AI_MODULES</c> 定数が連動する）。
/// </remarks>
internal static class FeatureModuleCatalog
{
    /// <summary>同梱モジュールの一覧を生成する（ホストはこれを起動時に 1 回列挙する）</summary>
    public static IReadOnlyList<IFeatureModule> CreateModules() =>
#if QUICKER_AI_MODULES
        [new AiChatFeatureModule(), new MockGenerationFeatureModule()];
#else
        [];
#endif
}
