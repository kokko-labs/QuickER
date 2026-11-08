namespace QuickER.Tests.Gui.Converters;

/// <summary>XAML の表示の値のコンバーターの表（反転のパラメーター・件数のしきい値・null の扱い）</summary>
public sealed class ConverterTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => ConverterRows.Table.Ids();

    [Theory(DisplayName = "コンバーターの表：反転のパラメーター・件数のしきい値・null の扱い")]
    [MemberData(nameof(Ids))]
    public void Row_Holds(string id) => ConverterRows.Check(ConverterRows.Table[id].Input);
}
