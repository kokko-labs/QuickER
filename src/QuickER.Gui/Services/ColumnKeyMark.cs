namespace QuickER.Services;

/// <summary>ER 図のカラム行左端に表示するキー標識の種類</summary>
/// <remarks>
/// 1 カラムが主キー・外部キー・一意制約の構成列を兼ねることはあるため、
/// 表示は <c>PK &gt; FK &gt; UQ</c> の優先度で 1 つに畳む（欄幅を増やさないための割り切り）。
/// </remarks>
public enum ColumnKeyMark
{
    /// <summary>標識なし</summary>
    None,

    /// <summary>主キー</summary>
    PrimaryKey,

    /// <summary>外部キー</summary>
    ForeignKey,

    /// <summary>一意制約（UNIQUE）の構成列</summary>
    Unique,
}

/// <summary>キー標識の判定と表示（表示文字・配色）を 1 箇所へ集約する共通ヘルパー</summary>
/// <remarks>
/// キャンバス XAML・ベクタ印刷（<see cref="DiagramVectorRenderer"/>）・SVG 出力
/// （<see cref="ImageExportService"/>）・寸法計測（<see cref="DiagramMetricsService"/>）の
/// 4 箇所が同じ規則を共有するため、判定と配色をここへ 1 本化する。
/// </remarks>
public static class ColumnKeyMarkPalette
{
    /// <summary>主キー標識の色</summary>
    public const string PrimaryKeyColor = "#D93025";

    /// <summary>外部キー標識の色</summary>
    public const string ForeignKeyColor = "#1A73E8";

    /// <summary>一意制約標識の色</summary>
    public const string UniqueColor = "#188038";

    /// <summary>標識なしの既定色（表示文字が空のため実際には見えないが、束縛先の型を満たすために用いる）</summary>
    public const string DefaultColor = "#374151";

    /// <summary>カラムの各フラグから表示するキー標識を決める（PK &gt; FK &gt; UQ の優先度）</summary>
    /// <param name="isPrimaryKey">主キーかどうか</param>
    /// <param name="isForeignKey">外部キーかどうか</param>
    /// <param name="isUniqueConstraintMember">いずれかの一意制約の構成列かどうか</param>
    public static ColumnKeyMark Resolve(
        bool isPrimaryKey,
        bool isForeignKey,
        bool isUniqueConstraintMember
    )
    {
        if (isPrimaryKey)
        {
            return ColumnKeyMark.PrimaryKey;
        }

        if (isForeignKey)
        {
            return ColumnKeyMark.ForeignKey;
        }

        return isUniqueConstraintMember ? ColumnKeyMark.Unique : ColumnKeyMark.None;
    }

    /// <summary>キー標識の表示文字を返す（いずれも 2 文字＝キー欄の幅は標識の種類に依らない）</summary>
    public static string GetText(ColumnKeyMark mark) =>
        mark switch
        {
            ColumnKeyMark.PrimaryKey => "PK",
            ColumnKeyMark.ForeignKey => "FK",
            ColumnKeyMark.Unique => "UQ",
            _ => string.Empty,
        };

    /// <summary>キー標識の表示色（#RRGGBB）を返す</summary>
    public static string GetColor(ColumnKeyMark mark) =>
        mark switch
        {
            ColumnKeyMark.PrimaryKey => PrimaryKeyColor,
            ColumnKeyMark.ForeignKey => ForeignKeyColor,
            ColumnKeyMark.Unique => UniqueColor,
            _ => DefaultColor,
        };

    /// <summary>簡易表示（PK/FK カラムのみ）で行を表示するかどうかを判定する</summary>
    /// <remarks>
    /// UQ 行は簡易表示で畳む（カード高さの意味論を PK/FK 時代から変えないため）。
    /// 主キー・外部キーを兼ねる一意制約構成列は <see cref="Resolve"/> の優先度により表示側へ残る。
    /// </remarks>
    public static bool IsVisibleInCompactView(ColumnKeyMark mark) =>
        mark is ColumnKeyMark.PrimaryKey or ColumnKeyMark.ForeignKey;
}
