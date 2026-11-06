using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.CodeGen.CSharp;
using QuickER.Tests.TestSupport;

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
/// <para>
/// キー集合の照合は照合の表（<c>ParityTests</c> の行 <c>cli-config-keys-match-options</c>・<c>cli-config-keys-match-schema</c>）へ移した。
/// ここに残すのはフラグ名の書き方の規則と、行が呼ぶ CLI フラグ表の読み取り。
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
    internal const string OutputPathKey = "OutputPath";

    /// <summary>別名 <c>OutputPath</c> の解決先プロパティ名</summary>
    internal const string OutputFileNameKey = "OutputFileName";

    /// <summary>CLI フラグ表の設定キーを、別名 <c>OutputPath</c> を解決したうえで返す</summary>
    internal static HashSet<string> CliConfigKeys() =>
        new GenerationOptionSet()
            .ConfigKeyFlags.Select(entry =>
                entry.Key == OutputPathKey ? OutputFileNameKey : entry.Key
            )
            .ToHashSet();

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
