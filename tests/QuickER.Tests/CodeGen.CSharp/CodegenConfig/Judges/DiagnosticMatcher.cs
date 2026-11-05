using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>生成の診断 1 件を種類（resx のキー）と名指しした項目に分けたもの</summary>
/// <param name="Severity">重要度</param>
/// <param name="Key">resx のキー（どの書式にも当たらなければ null）</param>
/// <param name="Items">書式の <c>{n}</c> に入った値（一覧は 1 行 1 項目に分け、前後の空白を除く）</param>
/// <param name="Message">元の文</param>
internal sealed record IdentifiedDiagnostic(
    GenerationDiagnosticSeverity Severity,
    string? Key,
    IReadOnlyList<string> Items,
    string Message
);

/// <summary>
/// 診断の文を resx の書式で照合し、種類と項目を取り出す（文言の断片では照合しない）
/// </summary>
/// <remarks>
/// <para>
/// 書式の <c>{n}</c> をワイルドカード（捕まえる組）にした正規表現を、診断を作ったときと同じ UI カルチャの書式で作り、
/// 文全体に当てる。重要度と同じ接頭辞（<c>CodeGen_Error_</c> / <c>CodeGen_Warning_</c> / <c>CodeGen_Info_</c>）のキーと、重要度を名前に持たない名前付きクエリのキー（<c>CodeGen_Query_</c>）だけを候補にし、
/// 複数に当たればワイルドカードでない文字が最も多い書式を採る（その文字数が並ぶ曖昧な当たり方は例外にして赤にする）。
/// </para>
/// <para>
/// 文の断片にしか使わないキー（一覧の行に付く接尾辞）は候補から外す。
/// </para>
/// </remarks>
internal static class DiagnosticMatcher
{
    /// <summary>名前付きクエリの診断のキーの接頭辞（重要度を名前に持たないので、重要度を問わず候補にする）</summary>
    private const string QueryPrefix = "CodeGen_Query_";

    /// <summary>単独の診断にならない断片のキー（ほかの書式の引数の中に入る）</summary>
    private static readonly HashSet<string> FragmentKeys = new(StringComparer.Ordinal)
    {
        nameof(Strings.CodeGen_Info_SyncSupportVersionlessTableSuffix),
    };

    /// <summary>診断の書式のキー（resx の生成クラスのプロパティから集める）</summary>
    private static readonly IReadOnlyList<string> Keys = typeof(Strings)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType == typeof(string))
        .Select(property => property.Name)
        .Where(name =>
            (
                name.StartsWith("CodeGen_Error_", StringComparison.Ordinal)
                || name.StartsWith("CodeGen_Warning_", StringComparison.Ordinal)
                || name.StartsWith("CodeGen_Info_", StringComparison.Ordinal)
                || name.StartsWith(QueryPrefix, StringComparison.Ordinal)
            ) && !FragmentKeys.Contains(name)
        )
        .ToList();

    private static readonly ConcurrentDictionary<
        (string Culture, string Key),
        (Regex Pattern, int LiteralLength)
    > Patterns = new();

    /// <summary>診断を種類と項目に分ける（いまの UI カルチャの書式で照合する）</summary>
    /// <param name="diagnostic">生成の診断</param>
    public static IdentifiedDiagnostic Identify(GenerationDiagnostic diagnostic)
    {
        var culture = CultureInfo.CurrentUICulture;
        var prefix = diagnostic.Severity switch
        {
            GenerationDiagnosticSeverity.Error => "CodeGen_Error_",
            GenerationDiagnosticSeverity.Warning => "CodeGen_Warning_",
            _ => "CodeGen_Info_",
        };
        var message = diagnostic.Message.ReplaceLineEndings("\n");
        (string Key, int LiteralLength, IReadOnlyList<string> Items)? best = null;

        foreach (
            var key in Keys.Where(key =>
                key.StartsWith(prefix, StringComparison.Ordinal)
                || key.StartsWith(QueryPrefix, StringComparison.Ordinal)
            )
        )
        {
            var (pattern, literalLength) = Patterns.GetOrAdd(
                (culture.Name, key),
                entry =>
                    Build(Strings.ResourceManager.GetString(entry.Key, culture) ?? string.Empty)
            );
            var match = pattern.Match(message);

            if (match.Success && best is { } tied && tied.LiteralLength == literalLength)
            {
                throw new InvalidOperationException(
                    $"診断が複数の書式に当たる（曖昧）: {tied.Key} / {key}: {message}"
                );
            }

            if (!match.Success || (best is { } current && current.LiteralLength >= literalLength))
            {
                continue;
            }

            var items = match
                .Groups.Cast<Group>()
                .Skip(1)
                .SelectMany(group => group.Value.Split('\n'))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            best = (key, literalLength, items);
        }

        return best is { } found
            ? new IdentifiedDiagnostic(
                diagnostic.Severity,
                found.Key,
                found.Items,
                diagnostic.Message
            )
            : new IdentifiedDiagnostic(diagnostic.Severity, null, [], diagnostic.Message);
    }

    /// <summary>書式を正規表現にする（<c>{n}</c> は捕まえる組、<c>{{</c> / <c>}}</c> は波括弧そのもの）</summary>
    private static (Regex Pattern, int LiteralLength) Build(string format)
    {
        var builder = new StringBuilder("^");
        var literalLength = 0;
        var group = 0;
        format = format.ReplaceLineEndings("\n");

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];

            if (c == '{' && i + 1 < format.Length && format[i + 1] == '{')
            {
                builder.Append(Regex.Escape("{"));
                literalLength++;
                i++;
                continue;
            }

            if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                builder.Append(Regex.Escape("}"));
                literalLength++;
                i++;
                continue;
            }

            if (c == '{')
            {
                var end = format.IndexOf('}', i);

                if (end > i)
                {
                    builder.Append(
                        string.Create(CultureInfo.InvariantCulture, $"(?<g{group}>.*?)")
                    );
                    group++;
                    i = end;
                    continue;
                }
            }

            builder.Append(Regex.Escape(c.ToString()));
            literalLength++;
        }

        builder.Append('$');
        return (
            new Regex(builder.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant),
            literalLength
        );
    }
}
