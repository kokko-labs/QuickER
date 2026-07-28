using QuickER.CodeGen.CSharp.Resources;

namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>
/// 自由 SQL（<see cref="QuickER.Model.QueryImplementationKind.Sql"/>）の静的バリデーション（レベル 1・方言非依存）
/// </summary>
/// <remarks>
/// <para>
/// 完全な SQL パーサではなく、1 パスの単純な状態機械で SQL 文字列を走査し、実行時に必ず失敗する・
/// あるいは定義ミスの兆候となる問題を検出する。検出項目は次の 3 種類：
/// </para>
/// <list type="bullet">
///   <item>未宣言パラメータ（<see cref="RawSqlIssueKind.UndeclaredParameter"/>）＝SQL 内の <c>@xxx</c> が
///     宣言パラメータ一覧に無い（実行時に必ず失敗するため、生成時は該当クエリをスキップする）</item>
///   <item>未使用パラメータ（<see cref="RawSqlIssueKind.UnusedParameter"/>）＝宣言済みだが SQL 内で未使用</item>
///   <item>複文（<see cref="RawSqlIssueKind.MultipleStatements"/>）＝文字列・コメント外の <c>;</c> の後に
///     さらに非空白トークンが続く</item>
/// </list>
/// <para>
/// 走査の誤検知防止として、<c>'...'</c> 文字列リテラル（<c>''</c> エスケープ対応）・<c>--</c> 行コメント・
/// <c>/* */</c> ブロックコメント・<c>[...]</c>（SQL Server 識別子）・<c>"..."</c>（標準識別子）の内側はスキップし、
/// <c>@@</c> で始まるシステム変数（<c>@@ROWCOUNT</c> 等）はパラメータ扱いしない。パラメータ名と宣言の照合は
/// 大文字小文字を区別しない。診断メッセージは CodeGen.CSharp の resx でローカライズ済み。
/// </para>
/// </remarks>
public static class RawSqlAnalyzer
{
    /// <summary>自由 SQL 静的検証の検出種別</summary>
    public enum RawSqlIssueKind
    {
        /// <summary>SQL 内で使われているパラメータが宣言されていない（実行時に必ず失敗する）</summary>
        UndeclaredParameter,

        /// <summary>宣言済みパラメータが SQL 内で使われていない（無害だが定義ミスの兆候）</summary>
        UnusedParameter,

        /// <summary>複数のステートメント（; の後にさらにコードが続く）が含まれている</summary>
        MultipleStatements,
    }

    /// <summary>自由 SQL 静的検証の検出結果 1 件</summary>
    /// <param name="Kind">検出種別</param>
    /// <param name="ParameterName">
    /// 対象パラメータ名（<see cref="RawSqlIssueKind.UndeclaredParameter"/> /
    /// <see cref="RawSqlIssueKind.UnusedParameter"/> のときは非 null。複文のときは null）
    /// </param>
    public sealed record RawSqlFinding(RawSqlIssueKind Kind, string? ParameterName);

    /// <summary>
    /// 自由 SQL を走査し、未宣言パラメータ・未使用パラメータ・複文を検出して返す
    /// </summary>
    /// <param name="sql">対象の SQL 文字列（null / 空白のみは解析対象外＝空の結果）</param>
    /// <param name="declaredParameters">宣言済みパラメータ名の一覧（大文字小文字は区別せず照合）</param>
    /// <returns>検出結果の一覧（未宣言→未使用→複文の順。問題がなければ空）</returns>
    public static IReadOnlyList<RawSqlFinding> Analyze(
        string? sql,
        IEnumerable<string> declaredParameters
    )
    {
        ArgumentNullException.ThrowIfNull(declaredParameters);

        // 宣言一覧は順序（未使用の報告順）と照合の両方に使うため materialize する
        var declaredList = declaredParameters.ToList();
        var declaredSet = new HashSet<string>(declaredList, StringComparer.OrdinalIgnoreCase);

        // 空・空白のみの SQL は解析対象外（宣言パラメータを一律「未使用」と誤検知するのを避ける）
        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        var usedParameters = new List<string>(); // 出現順・重複排除済み
        var usedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var multipleStatements = false;
        var semicolonSeen = false;

        var i = 0;
        var length = sql.Length;

        while (i < length)
        {
            var c = sql[i];

            // 行コメント（-- から行末まで）
            if (c == '-' && i + 1 < length && sql[i + 1] == '-')
            {
                i += 2;

                while (i < length && sql[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // ブロックコメント（/* から */ まで。未終端は末尾まで）
            if (c == '/' && i + 1 < length && sql[i + 1] == '*')
            {
                i += 2;

                while (i + 1 < length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }

                i = Math.Min(i + 2, length);
                continue;
            }

            // 文字列リテラル（'' はエスケープ）
            if (c == '\'')
            {
                i++;

                while (i < length)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    i++;
                }

                // 文字列リテラルは意味のあるトークン＝; の後に現れたら複文
                if (semicolonSeen)
                {
                    multipleStatements = true;
                }

                continue;
            }

            // [ ] 識別子（SQL Server）
            if (c == '[')
            {
                i++;

                while (i < length && sql[i] != ']')
                {
                    i++;
                }

                if (i < length)
                {
                    i++;
                }

                if (semicolonSeen)
                {
                    multipleStatements = true;
                }

                continue;
            }

            // " " 識別子（標準）
            if (c == '"')
            {
                i++;

                while (i < length && sql[i] != '"')
                {
                    i++;
                }

                if (i < length)
                {
                    i++;
                }

                if (semicolonSeen)
                {
                    multipleStatements = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // ステートメント区切り
            if (c == ';')
            {
                semicolonSeen = true;
                i++;
                continue;
            }

            // パラメータ（@xxx）またはシステム変数（@@xxx）
            if (c == '@')
            {
                // @@ で始まるものはシステム変数＝パラメータ扱いしない
                if (i + 1 < length && sql[i + 1] == '@')
                {
                    i += 2;

                    while (i < length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                    {
                        i++;
                    }
                }
                else
                {
                    var nameStart = i + 1;
                    var j = nameStart;

                    while (j < length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
                    {
                        j++;
                    }

                    if (j > nameStart)
                    {
                        var name = sql[nameStart..j];

                        if (usedSet.Add(name))
                        {
                            usedParameters.Add(name);
                        }

                        i = j;
                    }
                    else
                    {
                        // 単独の @（名前が続かない）は無視して 1 文字進める
                        i++;
                    }
                }

                if (semicolonSeen)
                {
                    multipleStatements = true;
                }

                continue;
            }

            // その他の意味のある文字（識別子・演算子・数値など）
            if (semicolonSeen)
            {
                multipleStatements = true;
            }

            i++;
        }

        var findings = new List<RawSqlFinding>();

        // 未宣言（SQL 内で使用・宣言に無い。出現順）
        foreach (var name in usedParameters)
        {
            if (!declaredSet.Contains(name))
            {
                findings.Add(new RawSqlFinding(RawSqlIssueKind.UndeclaredParameter, name));
            }
        }

        // 未使用（宣言済み・SQL 内で未使用。宣言順・重複排除）
        var reportedUnused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in declaredList)
        {
            if (!usedSet.Contains(name) && reportedUnused.Add(name))
            {
                findings.Add(new RawSqlFinding(RawSqlIssueKind.UnusedParameter, name));
            }
        }

        // 複文
        if (multipleStatements)
        {
            findings.Add(new RawSqlFinding(RawSqlIssueKind.MultipleStatements, null));
        }

        return findings;
    }

    /// <summary>
    /// 検出結果 1 件を現在の UI 言語の単文メッセージへ整形する（ダイアログ・生成診断で共用）。
    /// </summary>
    /// <remarks>
    /// 英語固定で描画したい面（外部 AI エージェント向け MCP サーバ）は
    /// <see cref="DescribeText"/> を使い、<see cref="QueryDiagnosticText.FormatEnglish"/> で描画する。
    /// </remarks>
    public static string Describe(RawSqlFinding finding) => DescribeText(finding).Format(null);

    /// <summary>
    /// 検出結果 1 件を「資源キー＋書式引数」のまま返す（描画時に面ごとのカルチャを明示できる）。
    /// </summary>
    public static QueryDiagnosticText DescribeText(RawSqlFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return finding.Kind switch
        {
            RawSqlIssueKind.UndeclaredParameter => new QueryDiagnosticText(
                nameof(Strings.CodeGen_Query_RawSqlUndeclaredParameter),
                finding.ParameterName
            ),
            RawSqlIssueKind.UnusedParameter => new QueryDiagnosticText(
                nameof(Strings.CodeGen_Query_RawSqlUnusedParameter),
                finding.ParameterName
            ),
            RawSqlIssueKind.MultipleStatements => new QueryDiagnosticText(
                nameof(Strings.CodeGen_Query_RawSqlMultipleStatements)
            ),
            // 想定外の種別は空文言（キーが解決できないので ResourceKey＝空文字がそのまま返る）
            _ => new QueryDiagnosticText(string.Empty),
        };
    }
}
