using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.Cli;

/// <summary>
/// CLI フラグ→設定キー表（<see cref="GenerationOptionSet"/>）が、設定 JSON の正となるキー集合
/// （<see cref="CodeGenerationOptions"/> の設定可能プロパティ／<see cref="GenerationConfigSchema"/>）と
/// 1:1 で対応していることを守るパリティガード。
/// </summary>
/// <remarks>
/// <para>
/// 設定キーを綴り間違えても何も落ちない: <c>ApplyOverrides</c> が誤った名前で JSON へ書き、
/// <c>Deserialize&lt;CodeGenerationOptions&gt;</c> が未知メンバーとして捨て、未知キー警告は
/// 「ユーザーが書いたキーだけ」を見るため出ない。結果としてそのフラグだけが静かに無効化される
/// （<c>--generate-repositories</c> を渡したのに Repository が出ない、が診断ゼロで起きる）。
/// ここが唯一の検出網になる。
/// </para>
/// <para>
/// 逆向き（カタログにあるのに CLI フラグが無い）も同時に固定する。新しい生成オプションを足したとき、
/// <see cref="GenerationConfigSchema"/> だけ更新して CLI フラグを忘れると MCP からは設定できるのに
/// CLI からは設定できないという非対称が黙って残るため。
/// </para>
/// </remarks>
public sealed class GenerationOptionSetParityTests
{
    /// <summary>
    /// CLI フラグ表の設定キー <c>OutputPath</c> が橋渡しする <see cref="CodeGenerationOptions"/> のプロパティ名。
    /// </summary>
    /// <remarks>
    /// <c>--output-path</c> は「出力先パス」を受け、ローダー（<c>DeriveOutputFileName</c>）がそのファイル名部分を
    /// <c>OutputFileName</c> へ導出する。CLI／GUI が書き出す正当な別名で、コア側に <c>OutputPath</c> という
    /// プロパティは存在しない（この 1 件だけが 1:1 対応の例外）。
    /// </remarks>
    private const string OutputPathKey = "OutputPath";

    /// <summary>別名 <c>OutputPath</c> の解決先プロパティ名</summary>
    private const string OutputFileNameKey = "OutputFileName";

    /// <summary>設定 JSON の正となるキー集合＝init 設定可能なインスタンスプロパティ（計算 get-only・static は除外）</summary>
    private static HashSet<string> RecognizedPropertyNames =>
        typeof(CodeGenerationOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Select(property => property.Name)
            .ToHashSet();

    /// <summary>CLI フラグ表の設定キーを、別名 <c>OutputPath</c> を解決したうえで返す</summary>
    private static HashSet<string> CliConfigKeys() =>
        new GenerationOptionSet()
            .ConfigKeyFlags.Select(entry =>
                entry.Key == OutputPathKey ? OutputFileNameKey : entry.Key
            )
            .ToHashSet();

    /// <summary>CLI フラグが書き込む設定キーが、コア側の設定可能プロパティと完全一致する</summary>
    [Fact(DisplayName = "CLI フラグの設定キーは CodeGenerationOptions のプロパティと完全一致する")]
    public void ConfigKeys_MatchCodeGenerationOptionsExactly()
    {
        CliConfigKeys()
            .Should()
            .BeEquivalentTo(
                RecognizedPropertyNames,
                "CLI フラグ表（src/QuickER.Cli/GenerationOptionSet.cs）の設定キーは CodeGenerationOptions の"
                    + "設定可能プロパティと 1:1 でなければなりません。綴り違いはフラグを無警告で無効化し、"
                    + "欠落は「GUI/MCP では設定できるのに CLI からは設定できない」非対称になります"
                    + $"（例外は別名 {OutputPathKey} → {OutputFileNameKey} の 1 件のみ）"
            );
    }

    /// <summary>CLI フラグが書き込む設定キーが、MCP 向けカタログのキー集合と完全一致する</summary>
    /// <remarks>
    /// カタログ側は <c>GenerationConfigSchemaTests</c> がコアと 1:1 であることを別途固定しているため、
    /// ここは「CLI・カタログ・コアの三者が同じ集合を指す」ことの残り 1 辺にあたる。
    /// </remarks>
    [Fact(DisplayName = "CLI フラグの設定キーは設定カタログのキー集合と完全一致する")]
    public void ConfigKeys_MatchGenerationConfigSchemaExactly()
    {
        var catalogNames = GenerationConfigSchema.Keys.Select(key => key.Name).ToHashSet();

        CliConfigKeys()
            .Should()
            .BeEquivalentTo(
                catalogNames,
                "CLI フラグ表と get_generation_config_schema のカタログは同じ設定キー集合を指す必要があります"
            );
    }

    /// <summary>フラグ名は kebab-case のロングオプションで、重複しない</summary>
    /// <remarks>
    /// 同じフラグ名を 2 つの設定キーへ割り当てると、後勝ちで一方のキーが到達不能になる
    /// （System.CommandLine は同名 Option の登録を弾かない）。
    /// </remarks>
    [Fact(DisplayName = "CLI フラグ名は重複せず kebab-case のロングオプションである")]
    public void Flags_AreUniqueKebabCaseLongOptions()
    {
        var flags = new GenerationOptionSet().ConfigKeyFlags.Select(entry => entry.Flag).ToList();

        flags.Should().OnlyHaveUniqueItems("同名フラグは一方の設定キーを到達不能にする");

        foreach (var flag in flags)
        {
            flag.Should().MatchRegex("^--[a-z0-9]+(-[a-z0-9]+)*$", $"フラグ '{flag}' の綴り");
        }
    }
}
