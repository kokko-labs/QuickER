using System.IO;
using System.Reflection;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// GUI のコード生成の設定（<see cref="CSharpGenerationSettings"/>）・生成オプション（<see cref="CodeGenerationOptions"/>）・
/// 生成ダイアログ（<see cref="CSharpGenerationDialogViewModel"/>）の 3 つの間で、キーが 1:1 に対応し、値が落ちずに渡ることを固定する
/// </summary>
/// <remarks>
/// <para>
/// オプションを 1 つ足したとき、設定のプロパティ・<c>ToCodeGenerationOptions</c> の初期化子・ダイアログの
/// <c>ApplySettings</c> / <c>ToSettings</c> のどれか 1 か所を書き忘れると、GUI だけでそのオプションが黙って効かない・
/// 保存されない（CLI のキーとの 1:1 はほかのガードが見るが、GUI 側の写しを見るテストは無かった）。
/// </para>
/// <para>
/// 値は型から自動で既定と違うものを作る（bool は反転・文字列は名前から作る・列挙は既定でない最後の値）ので、
/// 後から足したプロパティも書き足しなしに網に入る。対応から外すのは、設定の <c>OutputPath</c>（出力先のパス）と
/// オプションの <c>OutputFileName</c>（ファイル名だけ）の 1 組だけ（意味が違うので名前で対応しない）。
/// </para>
/// </remarks>
public sealed class GenerationSettingsParityTests
{
    /// <summary>設定だけにあるキー（出力先のパス。オプションのファイル名へは呼び出し側が分けて渡す）</summary>
    private const string SettingsOnly = nameof(CSharpGenerationSettings.OutputPath);

    /// <summary>オプションだけにあるキー（ファイル名だけ）</summary>
    private const string OptionsOnly = nameof(CodeGenerationOptions.OutputFileName);

    [Fact(DisplayName = "設定のキーと生成オプションのキーは 1:1 に対応する（出力先の 1 組を除く）")]
    public void SettingsAndOptions_HaveTheSameKeys()
    {
        var settings = WritableProperties(typeof(CSharpGenerationSettings))
            .Select(property => property.Name)
            .Where(name => name != SettingsOnly)
            .Order(StringComparer.Ordinal);
        var options = WritableProperties(typeof(CodeGenerationOptions))
            .Select(property => property.Name)
            .Where(name => name != OptionsOnly)
            .Order(StringComparer.Ordinal);

        settings.Should().Equal(options);
    }

    [Fact(DisplayName = "設定の全キーを既定と違う値にすると、生成オプションへ同じ値が渡る")]
    public void ToCodeGenerationOptions_CarriesEveryValue()
    {
        var settings = NonDefaultSettings();
        var options = settings.ToCodeGenerationOptions("Out.g.cs");
        var failures = new List<string>();

        foreach (var property in SharedProperties())
        {
            var expected = property.GetValue(settings);
            var actual = typeof(CodeGenerationOptions)
                .GetProperty(property.Name)!
                .GetValue(options);

            if (!SameValue(expected, actual))
            {
                failures.Add(
                    $"{property.Name}: 設定 {Describe(expected)} / オプション {Describe(actual)}"
                );
            }
        }

        options.OutputFileName.Should().Be("Out.g.cs");
        failures.Should().BeEmpty();
    }

    /// <summary>ダイアログの往復で確かめる構成（DB アクセスはラジオで排他なので、QuickER 版と EF Core の 2 つ）</summary>
    private static readonly string[] Variants = ["Repository", "EfCore"];

    /// <summary><see cref="Variants"/> の Theory 用の表</summary>
    public static TheoryData<string> DialogVariants => [.. Variants];

    [Theory(DisplayName = "設定をダイアログへ読み込んで書き戻すと、全キーの値が保たれる")]
    [MemberData(nameof(DialogVariants))]
    public void Dialog_RoundTripsEveryValue(string variant)
    {
        var settings = DialogSettings(variant);
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var vm = new CSharpGenerationDialogViewModel(new CSharpGenerationSettingsStore(folder));
            Invoke(vm, "ApplySettings", settings);
            var written = (CSharpGenerationSettings)Invoke(vm, "ToSettings")!;
            var failures = new List<string>();

            foreach (var property in WritableProperties(typeof(CSharpGenerationSettings)))
            {
                var expected = property.GetValue(settings);
                var actual = property.GetValue(written);

                if (!SameValue(expected, actual))
                {
                    failures.Add(
                        $"{property.Name}: 読み込んだ値 {Describe(expected)} / 書き戻した値 {Describe(actual)}"
                    );
                }
            }

            failures.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "ダイアログの往復の構成は、全キーを少なくとも一度は既定と違う値で通す")]
    public void DialogVariants_CoverEveryKeyWithANonDefaultValue()
    {
        var defaults = new CSharpGenerationSettings();
        var uncovered = WritableProperties(typeof(CSharpGenerationSettings))
            .Where(property =>
                Variants.All(variant =>
                    SameValue(
                        property.GetValue(DialogSettings(variant)),
                        property.GetValue(defaults)
                    )
                )
            )
            .Select(property => property.Name)
            .ToList();

        uncovered.Should().BeEmpty("既定のままでは、書き戻しで値が落ちても気づけない");
    }

    /// <summary>公開の読み書きできるインスタンスのプロパティ（init を含む）</summary>
    private static IEnumerable<PropertyInfo> WritableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true });

    /// <summary>設定とオプションの両方にあるキー（設定のプロパティで返す）</summary>
    private static IEnumerable<PropertyInfo> SharedProperties() =>
        WritableProperties(typeof(CSharpGenerationSettings))
            .Where(property => property.Name != SettingsOnly);

    /// <summary>全キーを型から作った既定と違う値にした設定（両方の DB アクセスを ON にする＝制約は見ない）</summary>
    private static CSharpGenerationSettings NonDefaultSettings()
    {
        var defaults = new CSharpGenerationSettings();
        var settings = new CSharpGenerationSettings();

        foreach (var property in WritableProperties(typeof(CSharpGenerationSettings)))
        {
            property.SetValue(settings, NonDefaultValue(property, property.GetValue(defaults)));
        }

        return settings;
    }

    /// <summary>ダイアログで成り立つ構成の設定（隠れる欄が出る条件を満たし、DB アクセスは 1 つだけ）</summary>
    private static CSharpGenerationSettings DialogSettings(string variant)
    {
        var settings = NonDefaultSettings();
        settings.RepositoryDialects = ["sqlserver", "sqlite"];

        if (variant == "Repository")
        {
            settings.GenerateRepositories = true;
            settings.GenerateEfCoreRepositories = false;
        }
        else
        {
            // EF Core では同期支援と無制限バイナリ列の除外の欄が隠れる（隠れた値は書き戻さない＝意図どおり）
            settings.GenerateRepositories = false;
            settings.GenerateEfCoreRepositories = true;
            settings.GenerateSyncSupport = false;
            settings.ExcludeUnboundedBinaryColumns = false;
        }

        return settings;
    }

    /// <summary>型から既定と違う値を作る</summary>
    private static object? NonDefaultValue(PropertyInfo property, object? defaultValue)
    {
        var type = property.PropertyType;

        if (type == typeof(bool))
        {
            return !(bool)defaultValue!;
        }

        if (type == typeof(string))
        {
            return "Acme." + property.Name;
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).Cast<object>().Last(value => !value.Equals(defaultValue));
        }

        if (type == typeof(List<string>))
        {
            return new List<string> { "sqlite" };
        }

        throw new InvalidOperationException(
            $"{property.Name} の型 {type.Name} から既定と違う値を作れない（このテストへ型を足すこと）"
        );
    }

    /// <summary>値が同じか（文字列の並びは要素で比べ、空の文字列と null は同じとみなさない）</summary>
    private static bool SameValue(object? left, object? right) =>
        (left, right) switch
        {
            (IEnumerable<string> a, IEnumerable<string> b) => a.SequenceEqual(b),
            _ => Equals(left, right),
        };

    private static string Describe(object? value) =>
        value switch
        {
            null => "null",
            IEnumerable<string> items => "[" + string.Join(", ", items) + "]",
            _ => value.ToString() ?? "null",
        };

    private static object? Invoke(object target, string methodName, params object[] arguments) =>
        target
            .GetType()
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(target, arguments);
}
