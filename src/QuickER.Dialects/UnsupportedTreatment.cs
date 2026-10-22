namespace QuickER.Dialects;

/// <summary>図で表せない定義を扱う機能</summary>
public enum UnsupportedFeature
{
    /// <summary>DB 取込</summary>
    Import,

    /// <summary>同期の差分</summary>
    Diff,

    /// <summary>同期の計画・スクリプト</summary>
    Sync,

    /// <summary>DDL 生成</summary>
    Ddl,

    /// <summary>C# 生成</summary>
    CSharpGeneration,

    /// <summary>出力（Mermaid・DBML・Excel・HTML）</summary>
    Export,

    /// <summary>GUI と MCP・内蔵チャットのツール</summary>
    ToolHosts,

    /// <summary>C# リバース</summary>
    Reverse,
}

/// <summary>機能ごとの扱い</summary>
/// <remarks>
/// 機能ごとに使える値が決まっている（網羅テストが <see cref="UnsupportedTreatments.AllowedFor"/> と照合する）。
/// </remarks>
public enum UnsupportedTreatment
{
    /// <summary>その機能にその種類のための処理が無い（検出しない・出さない・比べない）</summary>
    None,

    /// <summary>取込：箇所ごとに告知する（段 1）</summary>
    Notice,

    /// <summary>取込：図に印を付けて、箇所ごとに告知する</summary>
    MarkWithNotice,

    /// <summary>取込：取り込まずに、箇所ごとに告知する</summary>
    ExcludeWithNotice,

    /// <summary>取込：取り込まずに、種類ごとに件数と名前を 1 項目で告知する（段 2）</summary>
    ExcludeWithAggregatedNotice,

    /// <summary>取込：取り込まず、告知しない（段 3＝DB・拡張・ツールの内部のもの）</summary>
    ExcludeSilently,

    /// <summary>差分：比較で畳む（方言の実際の振る舞いが表せる値と同じ、または誰にも実現できない）</summary>
    Fold,

    /// <summary>差分：選択できない案内項目にする（QuickER は実行できないが利用者が手で実現できる）</summary>
    GuideItem,

    /// <summary>同期・DDL・ツール：注記する</summary>
    Annotation,

    /// <summary>同期・DDL：句を省いて注記する</summary>
    OmitWithAnnotation,

    /// <summary>同期・リバース：警告する（同期は計画の警告・テーブル再構築の喪失の警告）</summary>
    Warning,

    /// <summary>同期：計画から外して警告する</summary>
    ExcludeWithWarning,

    /// <summary>同期：DB の定義を保つ（DB から読み直して付け直す、または触らずに残す）</summary>
    Preserve,

    /// <summary>同期：逐次 DDL で直接実行する</summary>
    HandledNatively,

    /// <summary>同期：テーブルの作り直しへ畳む</summary>
    HandledByRebuild,

    /// <summary>同期・DDL：図全体を断る（入口の検証）</summary>
    Reject,

    /// <summary>同期・DDL：並べ替えて出す</summary>
    Reorder,

    /// <summary>C# 生成・出力・ツール：図の印に従う</summary>
    FollowMark,

    /// <summary>C# 生成：生成時の診断で告げる</summary>
    Diagnostic,

    /// <summary>出力：欠落を告知する</summary>
    OmissionNotice,

    /// <summary>ツール：適用の前に一覧を見せて確認する</summary>
    ConfirmBeforeApply,

    /// <summary>リバース：生成物の属性・引数で往復する</summary>
    RoundTrip,
}

/// <summary>種類 × 機能のマスの扱い（方言ごと）</summary>
/// <param name="SqlServer">SQL Server</param>
/// <param name="PostgreSql">PostgreSQL</param>
/// <param name="MySql">MySQL</param>
/// <param name="Oracle">Oracle</param>
/// <param name="Sqlite">SQLite</param>
/// <remarks>方言に依らない機能（C# 生成・出力・ツール・リバース）も同じ形で持ち、5 方言に同じ値を書く。</remarks>
public sealed record TreatmentCell(
    UnsupportedTreatment SqlServer,
    UnsupportedTreatment PostgreSql,
    UnsupportedTreatment MySql,
    UnsupportedTreatment Oracle,
    UnsupportedTreatment Sqlite
)
{
    /// <summary>5 方言に同じ扱い</summary>
    public static TreatmentCell Same(UnsupportedTreatment treatment) =>
        new(treatment, treatment, treatment, treatment, treatment);

    /// <summary>方言名から扱いを引く</summary>
    /// <exception cref="ArgumentException">知らない方言名</exception>
    public UnsupportedTreatment For(string dialect) =>
        DialectNames.Find(dialect) switch
        {
            DialectNames.SqlServer => SqlServer,
            DialectNames.PostgreSql => PostgreSql,
            DialectNames.MySql => MySql,
            DialectNames.Oracle => Oracle,
            DialectNames.Sqlite => Sqlite,
            _ => throw new ArgumentException(
                $"Unknown dialect name: '{dialect}'.",
                nameof(dialect)
            ),
        };
}
