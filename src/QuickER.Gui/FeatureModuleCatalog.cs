using QuickER.Extensibility;
#if QUICKER_AI_MODULES
using QuickER.AI.Chat;
using QuickER.AI.Mock;
#endif
#if QUICKER_CODEGEN_MODULES
using QuickER.CodeGen.UI;
#endif

namespace QuickER;

/// <summary>アプリに同梱するフィーチャーモジュールの静的カタログ</summary>
/// <remarks>
/// モジュールの着脱はこの一覧と csproj の ProjectReference の変更で行う。
/// ビルドプロパティ <c>IncludeAiModules=false</c>（既定 true）を指定すると、
/// AI 系モジュールへの参照ごと外れた「AI なし構成」をビルドできる
/// （csproj 側の条件付き ProjectReference と <c>QUICKER_AI_MODULES</c> 定数が連動する）。
/// コード生成・クエリ定義モジュールは AI とは独立の <c>IncludeCodeGenModules</c>（既定 true・
/// <c>QUICKER_CODEGEN_MODULES</c> 定数）で着脱する。
/// </remarks>
internal static class FeatureModuleCatalog
{
    /// <summary>同梱モジュールの一覧を生成する（ホストはこれを起動時に 1 回列挙する）</summary>
    public static IReadOnlyList<IFeatureModule> CreateModules()
    {
        var modules = new List<IFeatureModule>();

#if QUICKER_AI_MODULES
        // AI 系モジュール（チャット・モック生成）は IncludeAiModules=true のときだけ同梱する
        modules.Add(new AiChatFeatureModule());
        modules.Add(new MockGenerationFeatureModule());
#endif

#if QUICKER_CODEGEN_MODULES
        // コード生成・名前付きクエリ定義モジュールは IncludeCodeGenModules=true のときだけ同梱する
        modules.Add(new CodeGenerationFeatureModule());
#endif

        return modules;
    }
}
