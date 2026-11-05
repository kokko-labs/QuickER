using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 空白だけの説明（テーブル・列）を、DDL・差分・同期スクリプトが同じく「説明なし」として扱うことを検証する
/// </summary>
/// <remarks>
/// 判定は <see cref="DescriptionText"/> の 1 つだけ（写しを置かない＝<see cref="DescriptionPredicate_IsOnlyInDescriptionText"/>
/// が src を走査して固定する）。DDL・同期スクリプトは方言の DDL の表の行（<c>ddl-description-blank-is-written-as-none</c>・
/// <c>sync-other-description-blank-new-value-is-removal</c>）、差分は同期計画の期待の行 <c>diff-blank-description-*</c>、
/// 実 DB の往復は同期計画の期待の行 <c>realdb-blank-description-*</c>。
/// </remarks>
public class BlankDescriptionTests
{
    /// <summary>
    /// 説明の空欄の判定は <see cref="DescriptionText"/> の 1 つだけで、src の Provider 系に写しが無いことを検証する
    /// </summary>
    /// <remarks>
    /// 拾う形は、説明（名前に <c>Description</c> / <c>Desc</c> を含む式）に対する <c>string.IsNullOrEmpty</c> /
    /// <c>string.IsNullOrWhiteSpace</c> と、<c>?? string.Empty</c> での空文字への置き換え。説明の比較・書き出しの前に
    /// 経路ごとの判定を書くと、DDL・差分・同期スクリプトの扱いが食い違う。
    /// </remarks>
    [Fact(DisplayName = "空白だけの説明: 説明の空欄の判定は DescriptionText の 1 つだけ")]
    public void DescriptionPredicate_IsOnlyInDescriptionText()
    {
        var src = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src");
        var patterns = new[]
        {
            new Regex(
                @"string\.IsNullOr(?:Empty|WhiteSpace)\([^()]*Desc",
                RegexOptions.CultureInvariant
            ),
            new Regex(@"Desc\w*\s*\?\?\s*string\.Empty", RegexOptions.CultureInvariant),
        };
        var found = Directory
            .GetDirectories(src, "QuickER.Provider*")
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f =>
                !TestSupport.SourceFiles.IsBuildOutput(Path.GetRelativePath(src, f))
                && Path.GetFileName(f) != "DescriptionText.cs"
            )
            .SelectMany(f =>
                File.ReadAllLines(f)
                    .Select((line, index) => (f, line, index))
                    .Where(x => patterns.Any(p => p.IsMatch(x.line)))
                    .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.index + 1}: {x.line.Trim()}")
            )
            .ToList();

        found.Should().BeEmpty();
    }
}
