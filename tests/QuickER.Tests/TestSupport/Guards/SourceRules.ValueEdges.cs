using System.IO;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：値のプールの正本（値の端の値を、値を受ける部分のテストのソースへ直に書かない）</summary>
internal static partial class SourceRules
{
    /// <summary>値のプールの置き場（正本そのもの。規則の範囲から外し、空振りの検知の範囲にする）</summary>
    private const string ValuePoolsFolder = "tests/QuickER.Tests/TestSupport/ValuePools";

    // 見つける形は SQL の型に固有の値の写し（日時の範囲の端・1 tick・datetime の刻み・TimeSpan の端・浮動小数点の端・2^24 と 2^53・
    // ASCII の外の短い文字列）の 7 つ。Kind（DateTimeKind.Utc / Local）と型の端（数値の型・日時の型の MaxValue / MinValue）は数えない：
    // (1) BCL の定数そのものが正本で、プールの名前つきの定数へ包んでも値は変わらず写しの危険が無い（間接の層が増えるだけ）。
    // (2) その水準がエンジンに届いているかは、値のプールの受ける水準の行列（ValuePoolUseTests・ValuePoolUseIntegrationTests）が
    //     エンジンの入力を観測して見ている（水準 dt-kind-utc・dt-kind-local・int-type-min・int-type-max・dec-type-end・fp-type-end・dt-calendar-end）。
    // この 2 つの形を規則へ足すときは、上の 2 点が崩れたかを先に確かめる
    private static IEnumerable<SourceRule> ValueEdges() =>
        [
            SourceRule.Forbid(
                "value-edge-literal-copies",
                "値を受けるエンジン・表のテストのソースは、値の端の値をプール（TestSupport/ValuePools）から引き、直に書かない（書いた値はプールの水準と同じ値でも、水準の追加・直しに追従しない）",
                SourceScope.FromDeclarations(
                    "tests",
                    "値を受ける部分（ValuePoolUseDeclarations）のテストのソース",
                    ValueReceivingSourceFiles
                ),
                GuardFinder.Nodes(ValueEdgeLiterals.Describe),
                [.. ValueEdgeKnown()]
            ) with
            {
                // 値を受ける部分（宣言の受ける系統を持つもの）のテストのソースが範囲に入っていること
                MinimumFiles = 60,
                // 正本の代表の値（ValueEdgeSamples）には、この見つける形が拾う 9 つの形がある（拾えなくなると件数が合わない）。
                // プールの全体でなく代表の値の 1 ファイルに絞り、水準の判定式や宣言の文言の増減で件数を直さずに済むようにする
                Witness = new(
                    SourceScope.Files(ValuePoolsFolder, ValuePoolsFolder + "/ValueEdgeSamples.cs"),
                    ValueEdgeWitness()
                ),
            },
        ];

    /// <summary>
    /// 値を受ける部分（<see cref="ValuePoolUseDeclarations"/> の受ける系統を持つ部分）のテストのソース（エンジンの台帳・単体の表の台帳・
    /// Integration の台帳の写しから集め、フォルダ・部分ファイルを広げる。値のプールの置き場の下は除く）
    /// </summary>
    private static IReadOnlyList<string> ValueReceivingSourceFiles()
    {
        var root = EngineRepository.Root();
        var receiving = ValuePoolUseDeclarations
            .All.Where(subject => subject.Families.Count > 0)
            .Select(subject => subject.Name)
            .ToHashSet(StringComparer.Ordinal);
        var snapshot = ProtectionSnapshot.Parse(
            File.ReadAllText(Path.Combine(root, ProtectionSnapshot.IntegrationPath))
        );
        var sources = EngineRegistry
            .All.Select(engine =>
                (ProtectionNames.EnginePart(engine.Id, engine.Part), engine.SourceFiles)
            )
            .Concat(
                snapshot.Engines.Select(engine =>
                    (ProtectionNames.EnginePart(engine.Id, engine.Part), engine.SourceFiles)
                )
            )
            .Concat(
                TableRegistry.All.Select(table =>
                    (ProtectionNames.Of(ProtectionKind.Table, table.Name), table.SourceFiles)
                )
            )
            .Concat(
                snapshot.Tables.Select(table =>
                    (ProtectionNames.Of(ProtectionKind.Table, table.Name), table.SourceFiles)
                )
            )
            .Where(source => receiving.Contains(source.Item1))
            .ToList();
        var missing = receiving.Except(sources.Select(source => source.Item1)).Order().ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"値を受ける部分が両アセンブリの台帳に無い: {string.Join(", ", missing)}"
            );
        }

        return
        [
            .. ImpactMapSources
                .ExpandPartials(root, [.. sources.SelectMany(source => source.SourceFiles)])
                .Where(path =>
                    path.StartsWith("tests/", StringComparison.Ordinal)
                    && !path.StartsWith(ValuePoolsFolder + "/", StringComparison.Ordinal)
                )
                .Distinct(StringComparer.Ordinal),
        ];
    }
}
