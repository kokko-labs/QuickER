using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 生成 SQL の <c>--</c> 行コメントへ埋め込む値が、必ず <c>SqlComment.Sanitize</c> を通ることを構造的に固定する。
/// </summary>
/// <remarks>
/// <para>
/// <c>--</c> は行末までの行コメントなので、埋め込んだ値に改行が含まれると 2 行目以降が実行される SQL になる。
/// 埋め込み箇所は DDL 生成・同期スクリプト生成の 5 方言に散在し、1 箇所でも通し忘れるとそこが即座に武器になる
/// （実際、SQLite の再構築見出しと SQLite DDL の多対多コメントの 2 箇所が漏れていた。いずれも「兄弟の行は
/// サニタイズ済み」という見た目だったため、レビューでも気付きにくい）。
/// </para>
/// <para>
/// 名前については入口の <c>SqlNameText</c> が改行・制御文字を拒否するため二重の防壁になるが、<b>説明</b>
/// （<c>Entity.Description</c> / <c>Column.Description</c>）は入口で止めない＝コメントへ載るユーザーデータとして
/// サニタイズだけが防壁になる。そのためこの構造ガードは入口検証が入った後も価値を失わない。
/// </para>
/// <para>
/// 検査は「<c>--</c> で始まる補間文字列を含む文（<c>;</c> 区切り・文字列リテラル内は無視）の、すべての補間穴」が
/// <c>SqlComment.Sanitize(</c> を含むか、<see cref="SafeInterpolations"/> に明示されているか、のいずれかであること。
/// </para>
/// </remarks>
public class SqlCommentSanitizeGuardTests
{
    /// <summary>検査対象のプロジェクト（DDL 生成・同期スクリプト生成を持つもの）</summary>
    private static readonly string[] TargetProjects =
    [
        "QuickER.Provider",
        "QuickER.Provider.SqlServer",
        "QuickER.Provider.PostgreSql",
        "QuickER.Provider.MySql",
        "QuickER.Provider.Oracle",
        "QuickER.Provider.Sqlite",
    ];

    /// <summary>
    /// サニタイズ不要と判断した補間式（ユーザーデータではないもの）。新しい式を足すときは理由を添えること。
    /// </summary>
    private static readonly HashSet<string> SafeInterpolations = new(StringComparer.Ordinal)
    {
        // 固定書式の生成日時（改行を含み得ない）
        "DateTime.Now:yyyy-MM-dd HH:mm:ss",
        // 差分の種別・フェーズ＝enum。図の自由入力ではない
        "item.Kind",
        "kind",
        "phase",
        "SchemaDiffKind.AlterPrimaryKey",
        "SectionLabel(section)",
        // 件数＝int
        "section.Items.Count",
    };

    /// <summary>補間文字列の穴（<c>{式}</c>）。式に波括弧を含むケースは無い</summary>
    private static readonly Regex InterpolationHole = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// <c>--</c> で始まる文字列リテラル（先頭のインデント空白は許す）。補間文字列に限らないのは、
    /// 「素の <c>"-- …"</c> ＋ <c>$"{…}"</c> の連結」で組み立てるコメントがあるため。
    /// </summary>
    private static readonly Regex CommentStringLiteral = new(@"""\s*--", RegexOptions.Compiled);

    [Fact(DisplayName = "SQL コメントへ埋め込む値は必ず SqlComment.Sanitize を通る")]
    public void CommentInterpolations_AreAlwaysSanitized()
    {
        var offenders = new List<string>();
        var checkedStatements = 0;

        foreach (var (relativePath, text) in EnumerateSourceFiles())
        {
            foreach (var statement in SplitStatements(text))
            {
                if (!CommentStringLiteral.IsMatch(statement))
                {
                    continue;
                }

                checkedStatements++;

                foreach (Match hole in InterpolationHole.Matches(StringLiteralBodies(statement)))
                {
                    var expression = hole.Groups[1].Value.Trim();

                    if (
                        expression.Contains("SqlComment.Sanitize(", StringComparison.Ordinal)
                        || SafeInterpolations.Contains(expression)
                    )
                    {
                        continue;
                    }

                    offenders.Add($"{relativePath}: {{{expression}}}");
                }
            }
        }

        // 検査対象が 0 件になる（＝空検査になる）変更を検知する
        checkedStatements
            .Should()
            .BeGreaterThan(10, "SQL コメントを組み立てる文が検査対象として見つかること");

        offenders
            .Should()
            .BeEmpty(
                "-- 行コメントへ載せる値は SqlComment.Sanitize を通すこと"
                    + "（通さないと改行入りの値が 2 行目以降を実行される SQL に変える）"
            );
    }

    /// <summary>
    /// 文に含まれる文字列リテラルの中身だけを連結して返す（補間穴の抽出対象を文字列の内側に限るため）。
    /// </summary>
    /// <remarks>
    /// 文はコードブロックの <c>{</c> <c>}</c> をまたぐので、文そのものから補間穴を探すと C# の波括弧を
    /// 穴と誤認する。リテラルの中身だけを取り出してから数える。
    /// </remarks>
    private static string StringLiteralBodies(string statement)
    {
        var bodies = new System.Text.StringBuilder();
        var inString = false;
        var inVerbatim = false;

        for (var i = 0; i < statement.Length; i++)
        {
            var ch = statement[i];

            if (!inString)
            {
                if (ch == '@' && i + 1 < statement.Length && statement[i + 1] == '"')
                {
                    inString = true;
                    inVerbatim = true;
                    i++;
                }
                else if (ch == '"')
                {
                    inString = true;
                    inVerbatim = false;
                }

                continue;
            }

            if (!inVerbatim && ch == '\\' && i + 1 < statement.Length)
            {
                i++;
                continue;
            }

            if (inVerbatim && ch == '"' && i + 1 < statement.Length && statement[i + 1] == '"')
            {
                i++;
                continue;
            }

            if (ch == '"')
            {
                inString = false;
                bodies.Append('\n');
                continue;
            }

            bodies.Append(ch);
        }

        return bodies.ToString();
    }

    /// <summary>検査対象プロジェクトの全 C# ソース（相対パスと本文）</summary>
    private static IEnumerable<(string RelativePath, string Text)> EnumerateSourceFiles()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();

        foreach (var project in TargetProjects)
        {
            var directory = Path.Combine(root, "src", project);
            Directory.Exists(directory).Should().BeTrue($"検査対象 {project} が存在すること");

            foreach (
                var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            )
            {
                yield return (
                    Path.GetRelativePath(root, path).Replace('\\', '/'),
                    File.ReadAllText(path)
                );
            }
        }
    }

    /// <summary>
    /// ソースを文（<c>;</c> 区切り）へ分割する。文字列リテラル・文字リテラル・コメントの中の <c>;</c> は区切りにしない。
    /// </summary>
    /// <remarks>
    /// 補間文字列は複数行へ折り返され、続きの行にも補間穴が現れる（<c>+ $"{Sanitize(x)} -&gt; "</c>）。
    /// 行単位で見ると続きの行を取りこぼすため、文単位で束ねてから穴を数える。
    /// </remarks>
    private static IEnumerable<string> SplitStatements(string text)
    {
        var statement = new System.Text.StringBuilder();
        var inString = false;
        var inVerbatimString = false;
        var inChar = false;
        var inLineComment = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inLineComment)
            {
                // コメント本文は検査対象に含めない（説明文の {…} を補間穴と誤認しないため）
                if (ch is '\n')
                {
                    inLineComment = false;
                    statement.Append(ch);
                }

                continue;
            }

            if (inVerbatimString)
            {
                // 逐語的文字列では \ はただの文字で、"" が " のエスケープになる
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inVerbatimString = false;
                }

                statement.Append(ch);
                continue;
            }

            if (inString)
            {
                if (ch == '\\' && i + 1 < text.Length)
                {
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                statement.Append(ch);
                continue;
            }

            if (inChar)
            {
                if (ch == '\\' && i + 1 < text.Length)
                {
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                }

                if (ch == '\'')
                {
                    inChar = false;
                }

                statement.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '@' when i + 1 < text.Length && text[i + 1] == '"':
                    inVerbatimString = true;
                    statement.Append(ch).Append(text[i + 1]);
                    i++;
                    continue;
                case '"':
                    inString = true;
                    break;
                case '\'':
                    inChar = true;
                    break;
                case '/' when i + 1 < text.Length && text[i + 1] == '/':
                    inLineComment = true;
                    break;
                case ';':
                    yield return statement.ToString();
                    statement.Clear();
                    continue;
            }

            statement.Append(ch);
        }

        if (statement.Length > 0)
        {
            yield return statement.ToString();
        }
    }
}
