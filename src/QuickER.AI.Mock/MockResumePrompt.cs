using System.Text;
using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// 状態再開（会話は新規・フォルダ内容をコンテキスト注入）用の初回プロンプトを組み立てる。
/// </summary>
/// <remarks>
/// 既存モックの続きであること・画面/遷移/改訂履歴・共有 CSS の規約・現在の ER スキーマ全文・
/// スキーマ差異の注記・「修正前に get_screen で現状取得」の指示を含める。
/// 見出し・注記・指示文は resx（中立＝英語 / <c>ja</c> サテライト＝日本語）から解決し、表示言語に追従する。
/// 組立構造（項目の並び・箇条書き記法）は言語非依存でコード側に保つ。
/// </remarks>
public static class MockResumePrompt
{
    /// <summary>改訂履歴として表示する直近件数</summary>
    private const int RecentRevisionCount = 5;

    /// <summary>再開用の初回プロンプトを組み立てる</summary>
    /// <param name="currentSchema">現在の ER スキーマ記述テキスト（<see cref="MockSchemaSerializer.Serialize"/> の結果）</param>
    /// <param name="manifest">再開対象のマニフェスト</param>
    /// <param name="schemaChanged">前回保存時からスキーマが変わっているか</param>
    /// <returns>エンジンへ送る再開プロンプト本文</returns>
    public static string Build(string currentSchema, MockManifest manifest, bool schemaChanged)
    {
        var builder = new StringBuilder();

        builder.AppendLine(Strings.Mock_ResumePromptIntro);
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(manifest.Title))
        {
            builder.AppendLine(Strings.Mock_ResumePromptTitleHeading);
            builder.AppendLine(manifest.Title.Trim());
            builder.AppendLine();
        }

        AppendScreens(builder, manifest);
        AppendTransitions(builder, manifest);
        AppendRevisions(builder, manifest);

        // 共有 CSS 規約の明示（フォルダ直下 style.css を共有し、各画面から link する）
        builder.AppendLine(Strings.Mock_ResumePromptStylesheetHeading);
        builder.AppendLine(
            string.Format(
                Strings.Mock_ResumePromptStylesheetBodyFormat,
                MockManifest.StylesheetFileName
            )
        );
        builder.AppendLine();

        builder.AppendLine(Strings.Mock_ResumePromptSchemaHeading);
        builder.AppendLine();
        builder.AppendLine(currentSchema);
        builder.AppendLine();

        if (schemaChanged)
        {
            builder.AppendLine(Strings.Mock_ResumePromptSchemaChangedNote);
            builder.AppendLine();
        }

        builder.AppendLine(Strings.Mock_ResumePromptGetScreenNote);

        return builder.ToString().TrimEnd();
    }

    /// <summary>現在スキーマがマニフェストのスナップショットから変化しているかを判定する（改行差は吸収）</summary>
    /// <param name="currentSchema">現在の ER スキーマ記述テキスト</param>
    /// <param name="manifest">比較対象のマニフェスト</param>
    /// <returns>差異があれば true</returns>
    public static bool IsSchemaChanged(string currentSchema, MockManifest manifest)
    {
        return !string.Equals(
            Normalize(currentSchema),
            Normalize(manifest.SourceSchema),
            StringComparison.Ordinal
        );
    }

    /// <summary>画面一覧を書き出す</summary>
    private static void AppendScreens(StringBuilder builder, MockManifest manifest)
    {
        builder.AppendLine(Strings.Mock_ResumePromptScreensHeading);
        builder.AppendLine();

        if (manifest.Screens.Count == 0)
        {
            builder.AppendLine(Strings.Mock_ResumePromptNoScreens);
            builder.AppendLine();
            return;
        }

        foreach (var screen in manifest.Screens)
        {
            var name = string.IsNullOrWhiteSpace(screen.Name) ? screen.File : screen.Name.Trim();
            var line = $"- {screen.File}（{name}）";

            if (!string.IsNullOrWhiteSpace(screen.Description))
            {
                line += $": {screen.Description.Trim()}";
            }

            builder.AppendLine(line);
            // 各画面の宣言状態（宣言済み: エンティティ＋CRUD／未宣言）をサブ行で注入する
            builder.AppendLine($"  - {DescribeScreenEntities(screen)}");
        }

        builder.AppendLine();
    }

    /// <summary>画面のエンティティ宣言状態を 1 行の説明文にする（宣言済みは <c>Name(CRU)</c> 形式・未宣言は専用文言）</summary>
    private static string DescribeScreenEntities(MockScreen screen)
    {
        var entities = screen.Entities;

        if (entities is null || entities.Count == 0)
        {
            return Strings.Mock_ResumePromptEntitiesNone;
        }

        var parts = entities.Select(entity =>
            string.IsNullOrEmpty(entity.Operations)
                ? entity.Name
                : $"{entity.Name}({entity.Operations})"
        );

        return string.Format(
            Strings.Mock_ResumePromptEntitiesLabelFormat,
            string.Join(", ", parts)
        );
    }

    /// <summary>遷移一覧を書き出す</summary>
    private static void AppendTransitions(StringBuilder builder, MockManifest manifest)
    {
        builder.AppendLine(Strings.Mock_ResumePromptTransitionsHeading);
        builder.AppendLine();

        if (manifest.Transitions.Count == 0)
        {
            builder.AppendLine(Strings.Mock_ResumePromptNoTransitions);
            builder.AppendLine();
            return;
        }

        foreach (var transition in manifest.Transitions)
        {
            var trigger = string.IsNullOrWhiteSpace(transition.Trigger)
                ? string.Empty
                : $"（{transition.Trigger.Trim()}）";
            builder.AppendLine($"- {transition.From} → {transition.To}{trigger}");
        }

        builder.AppendLine();
    }

    /// <summary>改訂履歴（直近数件）を書き出す</summary>
    private static void AppendRevisions(StringBuilder builder, MockManifest manifest)
    {
        builder.AppendLine(Strings.Mock_ResumePromptRevisionsHeading);
        builder.AppendLine();

        if (manifest.Revisions.Count == 0)
        {
            builder.AppendLine(Strings.Mock_ResumePromptNoRevisions);
            builder.AppendLine();
            return;
        }

        var recent = manifest
            .Revisions.Skip(Math.Max(0, manifest.Revisions.Count - RecentRevisionCount))
            .ToList();

        foreach (var revision in recent)
        {
            builder.AppendLine(
                $"- {revision.Timestamp:yyyy-MM-dd HH:mm} {revision.Note}".TrimEnd()
            );
        }

        builder.AppendLine();
    }

    /// <summary>改行差（CRLF/CR/LF）と末尾空白のみを吸収して正規化する</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
