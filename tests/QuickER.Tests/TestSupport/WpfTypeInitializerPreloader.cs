using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// PresentationFramework の型初期化子（静的コンストラクタ）を、テストの並列実行が始まる前に
/// 一括実行しておくプリローダー。
/// </summary>
/// <remarks>
/// WPF 内部の BAML スキーマコンテキスト（WpfSharedBamlSchemaContext）のロックと、
/// コントロール型の型初期化ロックは取得順が交差しうる：
/// <list type="bullet">
/// <item>BAML 読込・ResourceDictionary の遅延実体化中のスレッドは、スキーマロックを保持したまま
/// 既知型の静的フィールド参照で未初期化型の cctor 完了を待つ</item>
/// <item>一方、コントロールを直接 new したスレッドは cctor 実行中（型初期化ロック保持）に
/// 既定テンプレート構築（FrameworkElementFactory.Type 設定）で同じスキーマロックを待つ
/// （例：PasswordBox → TextBox → TextBoxBase → ScrollViewer の cctor 連鎖）</item>
/// </list>
/// この 2 つが別スレッドで重なると相互待ちの永久停止になる（2026-07-27 に全テスト並列実行で
/// 実観測。ScrollViewer..cctor と DialogThemeTests の遅延 BAML 実体化が相互待ちし、
/// XamlLoadGate 保持スレッドも巻き込まれてランナー全体が無応答になった）。
///
/// cctor はプロセスにつき一度しか実行されないため、並列実行が始まる前に
/// PresentationFramework の DependencyObject 派生型の cctor をすべて済ませておけば、
/// 「テストスレッド上で WPF の cctor が走る」というデッドロックの片翼が全型について消える。
/// ScrollViewer など個別型の限定リストにせずアセンブリ全体を掃くのは、スキーマロックを
/// cctor 内で取得する型を漏れなく列挙できる保証がないため（対象 268 型・約 0.25 秒の一括実行で、
/// 個別列挙の網羅性リスクを買い取る）。
/// </remarks>
internal static class WpfTypeInitializerPreloader
{
    /// <summary>
    /// エントリポイント実行前（＝xunit の並列テスト開始前・単一スレッド）に
    /// PresentationFramework の全 DependencyObject 派生型の型初期化子を実行する。
    /// </summary>
    /// <remarks>
    /// ModuleInitializer 内で別スレッドを起こして待つと、そのスレッドが本モジュールの
    /// コード（クロージャ）実行時にモジュール初期化の完了待ちへ入り自己デッドロックする
    /// 恐れがあるため、呼び出し元スレッドで直接実行する。WPF の cctor は
    /// DependencyProperty 登録・クラスハンドラ登録・既定テンプレート構築のみで
    /// STA や Dispatcher を要求しないため、MTA のメインスレッドで安全に実行できる。
    /// </remarks>
    [ModuleInitializer]
    internal static void PreloadPresentationFrameworkTypeInitializers()
    {
        foreach (var type in GetLoadableTypes(typeof(FrameworkElement).Assembly))
        {
            if (type.IsGenericTypeDefinition || !typeof(DependencyObject).IsAssignableFrom(type))
            {
                continue;
            }

            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (Exception)
            {
                // 型初期化に失敗する型があっても、その型を実際に使うテストで同じ例外が再発して
                // 顕在化する。プリロードの目的はデッドロック除去だけなので握りつぶして続行する。
            }
        }
    }

    /// <summary>アセンブリからロード可能な型だけを列挙する（一部の型のロード失敗は無視）</summary>
    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.OfType<Type>()];
        }
    }
}
