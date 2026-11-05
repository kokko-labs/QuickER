using System.Reflection;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>MainViewModel の最終既知ハッシュと「続行」で無視したハッシュを読む</summary>
/// <remarks>
/// 製品は 2 つを公開しないので、文書の状態機械のエンジンだけが反射で読む（不変条件「最終既知ハッシュは書いた／読んだ内容
/// そのもの」を毎手で見るため）。フィールドが見つからなければ型の初期化で例外にする（黙って null を返さない）。
/// 読み方は <c>DocumentFileTests.Probe_ReadsTheRecordedHashes</c> が固定する。
/// </remarks>
internal static class DocumentFileProbe
{
    private static readonly FieldInfo LastKnownField = Find("_lastKnownFileHash");

    private static readonly FieldInfo IgnoredField = Find("_ignoredExternalHash");

    /// <summary>最終既知ハッシュ（未保存の無題は null）</summary>
    public static string? LastKnownHash(MainViewModel vm) => (string?)LastKnownField.GetValue(vm);

    /// <summary>「続行」で無視した外部の版のハッシュ（無ければ null）</summary>
    public static string? IgnoredHash(MainViewModel vm) => (string?)IgnoredField.GetValue(vm);

    private static FieldInfo Find(string name)
    {
        var field =
            typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"MainViewModel の {name} が見つからない。DocumentFileProbe を製品の形に合わせて直すこと"
            );

        return field.FieldType == typeof(string)
            ? field
            : throw new InvalidOperationException(
                $"MainViewModel の {name} が string でない（{field.FieldType}）。DocumentFileProbe を直すこと"
            );
    }
}
