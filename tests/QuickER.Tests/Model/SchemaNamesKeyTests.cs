using System.Text;
using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// <see cref="SchemaNames.Key"/> が、序数で比べたときに <see cref="SchemaNames.AreSame"/> と同じ答えになることを検証するテストクラス
/// </summary>
/// <remarks>
/// キーは名前を連結したシグネチャ・複合キーに使うので、1 字でも答えが食い違うと、列の組み合わせ（AreSame）と
/// 一意制約・外部キーの照合（キー）で同じ名前の扱いが割れる。小文字化・素の大文字化はどちらも食い違う字を持つ。
/// </remarks>
public sealed class SchemaNamesKeyTests
{
    [Fact(DisplayName = "Unicode のスカラー値の全部で、キーの序数一致は AreSame と同じ答えになる")]
    public void Key_AgreesWithAreSame_ForEveryScalarValue()
    {
        // 名前の前後の空白で答えが変わらないよう、両端を同じ字で挟んで比べる
        var keyOfClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameOfKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var checkedValues = 0;

        for (var value = 0; value <= 0x10FFFF; value++)
        {
            if (value is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var name = "x" + char.ConvertFromUtf32(value) + "x";
            var key = SchemaNames.Key(name);
            checkedValues++;

            // OrdinalIgnoreCase が同じとみなす名前は、同じキーになる
            if (keyOfClass.TryGetValue(name, out var classKey))
            {
                if (!string.Equals(classKey, key, StringComparison.Ordinal))
                {
                    problems.Add($"U+{value:X4}: AreSame なのにキーが違う");
                }
            }
            else
            {
                keyOfClass[name] = key;
            }

            // 同じキーになる名前は、AreSame で同じ名前
            if (nameOfKey.TryGetValue(key, out var other))
            {
                if (!SchemaNames.AreSame(other, name))
                {
                    problems.Add($"U+{value:X4}: キーが同じなのに AreSame でない");
                }
            }
            else
            {
                nameOfKey[key] = name;
            }
        }

        checkedValues.Should().Be(0x110000 - 0x800);
        problems.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "前後の空白になる全字で、キーの空白の除き方は AreSame と同じ（内側の空白は残す）"
    )]
    public void Key_TrimsLikeAreSame_ForEveryWhiteSpace()
    {
        var problems = new List<string>();

        for (var value = 0; value <= 0xFFFF; value++)
        {
            var c = (char)value;

            if (!char.IsWhiteSpace(c))
            {
                continue;
            }

            var padded = c + "Ab" + c;
            var inner = "a" + c + "b";

            if (
                SchemaNames.Key(padded) != SchemaNames.Key("ab")
                || !SchemaNames.AreSame(padded, "ab")
            )
            {
                problems.Add($"U+{value:X4}: 前後の空白の除き方が食い違う");
            }

            if (SchemaNames.Key(inner) == SchemaNames.Key("ab") || SchemaNames.AreSame(inner, "ab"))
            {
                problems.Add($"U+{value:X4}: 内側の空白まで除いた");
            }
        }

        problems.Should().BeEmpty();
    }

    [Theory(DisplayName = "小文字化・素の大文字化と食い違う字も、AreSame と同じ答えになる")]
    [InlineData("k", "K", false)] // ケルビン記号（小文字化すると k）
    [InlineData("ω", "Ω", false)] // オーム記号（小文字化すると ω）
    [InlineData("å", "Å", false)] // オングストローム記号（小文字化すると å）
    [InlineData("s", "ſ", false)] // ſ（大文字化すると S）
    [InlineData("𐐀", "𐐨", true)] // Deseret の大文字と小文字（補助面）
    [InlineData("Orders", " orders ", true)]
    public void Key_AgreesWithAreSame_ForKnownTraps(string first, string second, bool same)
    {
        SchemaNames.AreSame(first, second).Should().Be(same);
        (SchemaNames.Key(first) == SchemaNames.Key(second)).Should().Be(same);
    }

    [Fact(DisplayName = "対になっていないサロゲートはキーにそのまま残る")]
    public void Key_KeepsLoneSurrogates()
    {
        var lone = new StringBuilder().Append('a').Append((char)0xD800).Append('b').ToString();

        SchemaNames
            .Key(lone)
            .Should()
            .Be(new StringBuilder().Append('A').Append((char)0xD800).Append('B').ToString());
    }
}
