using QuickER.Extensibility;
#if QUICKER_DBTOOL_MODULES
using QuickER.Db.UI;
#endif
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
/// ビルドプロパティ（いずれも既定 true・csproj 側の条件付き ProjectReference と定数が連動）:
/// <c>IncludeDbToolModules</c>（DB 取込・DB 同期＝<c>QUICKER_DBTOOL_MODULES</c>）・
/// <c>IncludeAiModules</c>（AI チャット・モック生成＝<c>QUICKER_AI_MODULES</c>）・
/// <c>IncludeCodeGenModules</c>（コード生成・クエリ定義＝<c>QUICKER_CODEGEN_MODULES</c>）を
/// それぞれ独立に false へ指定すると、該当モジュールへの参照ごと外れた構成をビルドできる。
/// </remarks>
internal static class FeatureModuleCatalog
{
    /// <summary>同梱モジュールの一覧を生成する（ホストはこれを起動時に 1 回列挙する）</summary>
    public static IReadOnlyList<IFeatureModule> CreateModules()
    {
        var modules = new List<IFeatureModule>();

#if QUICKER_DBTOOL_MODULES
        // DB 取込・DB 同期モジュール（ツールバー順の先頭＝対象 DB 選択の直後に並ぶ）
        modules.Add(new DbToolsFeatureModule());
#endif

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
