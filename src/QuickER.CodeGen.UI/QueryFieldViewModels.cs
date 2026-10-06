using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickER.CodeGen.UI;

/// <summary>
/// クエリの子行（パラメータ・射影フィールド）に共通する「列参照で型付けする欄」の基底 ViewModel
/// </summary>
/// <remarks>
/// 参照元列（<see cref="SourceColumnId"/>）を選ぶと型トークン（<see cref="Type"/>）の表示は列の宣言型に追従し、
/// 手入力は不可（<see cref="IsTypeEditable"/> が false）になる。「なし」へ戻すと、列を選ぶ前の手入力値へ戻す。
/// 手入力値が無い（列つきで読み込んだ行）ときは、外していた列の型を中立トークンへ変換した値にし、変換できなければ空にする
/// （列の宣言型は DB の型表記で、型トークンとしては解決できないため、そのまま残すと保存後に生成で失敗する）。
/// 型トークン導出関数は必須（非 null）とし、両方の子行で同一の挙動に揃える
/// （nullable にすると、導出関数を渡さない子行だけ列選択時に型が追従しないドリフトが起きる）。
/// </remarks>
public abstract partial class QueryFieldViewModelBase : ObservableObject
{
    /// <summary>参照元列 ID から型の表示（列の宣言型）を導出する関数</summary>
    private readonly Func<Guid?, string?> _deriveToken;

    /// <summary>参照元列 ID から中立の型トークンを導出する関数（「なし」へ戻したときの既定値。導出できなければ null）</summary>
    private readonly Func<Guid, string?> _deriveCanonicalToken;

    /// <summary>列を選ぶ直前に入っていた型トークン（「なし」へ戻したときに戻す値）</summary>
    private string _manualType = string.Empty;

    /// <summary>型トークン導出関数を注入して構築する</summary>
    /// <param name="deriveToken">参照元列 ID → 型の表示（列の宣言型）の導出関数</param>
    /// <param name="deriveCanonicalToken">
    /// 参照元列 ID → 中立の型トークンの導出関数（列を「なし」へ戻したとき、手入力値が無ければ使う。省略時は導出しない）
    /// </param>
    protected QueryFieldViewModelBase(
        Func<Guid?, string?> deriveToken,
        Func<Guid, string?>? deriveCanonicalToken = null
    )
    {
        ArgumentNullException.ThrowIfNull(deriveToken);
        _deriveToken = deriveToken;
        _deriveCanonicalToken = deriveCanonicalToken ?? (_ => null);
    }

    /// <summary>名前（パラメータ名・射影 DTO のプロパティ名）</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>型トークン（方言中立。例: <c>int32</c> / <c>string(50)</c>。列参照時は列由来・編集不可）</summary>
    [ObservableProperty]
    private string _type = "int32";

    /// <summary>型付けの参照元列 ID（null＝型トークンで型付け）。列を選ぶと VO 有効の図では VO 型になる</summary>
    [ObservableProperty]
    private Guid? _sourceColumnId;

    /// <summary>型トークンを手入力できるか（列参照でないときのみ）</summary>
    public bool IsTypeEditable => SourceColumnId is null;

    partial void OnSourceColumnIdChanged(Guid? oldValue, Guid? newValue)
    {
        if (newValue is not null)
        {
            // 「なし」から列を選んだときは、それまでの型トークンを手入力値として覚える（列から列への切替では覚え直さない）
            if (oldValue is null)
            {
                _manualType = Type;
            }

            // 参照元列を選ぶと型表示は列由来になる
            if (_deriveToken(newValue) is { } token)
            {
                Type = token;
            }
        }
        else if (oldValue is { } previousColumnId)
        {
            // 「なし」へ戻したときは列を選ぶ前の手入力値へ戻す。無ければ外した列の型の中立トークン（変換できなければ空）
            Type = !string.IsNullOrWhiteSpace(_manualType)
                ? _manualType
                : _deriveCanonicalToken(previousColumnId) ?? string.Empty;
        }

        OnPropertyChanged(nameof(IsTypeEditable));
    }
}

/// <summary>クエリパラメータ 1 件の編集用 ViewModel</summary>
public partial class QueryParameterViewModel : QueryFieldViewModelBase
{
    /// <summary>型トークン導出関数を注入して構築する（既定の名前は <c>param</c>）</summary>
    /// <param name="deriveToken">参照元列 ID → 型の表示（列の宣言型）の導出関数</param>
    /// <param name="deriveCanonicalToken">参照元列 ID → 中立の型トークンの導出関数（省略時は導出しない）</param>
    public QueryParameterViewModel(
        Func<Guid?, string?> deriveToken,
        Func<Guid, string?>? deriveCanonicalToken = null
    )
        : base(deriveToken, deriveCanonicalToken)
    {
        Name = "param";
    }

    /// <summary>リスト型か（IN 条件用）</summary>
    [ObservableProperty]
    private bool _isList;
}

/// <summary>並び順 1 件の編集用 ViewModel</summary>
public partial class QueryOrderingViewModel : ObservableObject
{
    /// <summary>並び替えキーの列 ID</summary>
    [ObservableProperty]
    private Guid _columnId;

    /// <summary>降順か（既定は昇順）</summary>
    [ObservableProperty]
    private bool _descending;
}

/// <summary>射影フィールド 1 件の編集用 ViewModel</summary>
public partial class ProjectionFieldViewModel : QueryFieldViewModelBase
{
    /// <summary>型トークン導出関数を注入して構築する（既定の名前は <c>Field</c>）</summary>
    /// <param name="deriveToken">参照元列 ID → 型の表示（列の宣言型）の導出関数</param>
    /// <param name="deriveCanonicalToken">参照元列 ID → 中立の型トークンの導出関数（省略時は導出しない）</param>
    public ProjectionFieldViewModel(
        Func<Guid?, string?> deriveToken,
        Func<Guid, string?>? deriveCanonicalToken = null
    )
        : base(deriveToken, deriveCanonicalToken)
    {
        Name = "Field";
    }

    /// <summary>生成 DTO のプロパティを NULL 許容にするか（null＝自動。UI では編集せず定義の値を保持する）</summary>
    public bool? IsNullable { get; set; }
}

/// <summary>列ドロップダウンの選択肢（並び順・射影の参照元列）</summary>
/// <param name="Id">列 ID（「なし＝自由フィールド」のときは null）</param>
/// <param name="Name">表示名</param>
public sealed record ColumnChoice(Guid? Id, string Name)
{
    /// <summary>「なし＝自由フィールド」を表す選択肢（表示名は呼び出し側で差し替える）</summary>
    public static ColumnChoice None { get; } =
        new(null, QuickER.CodeGen.UI.Resources.Strings.QueryDialog_FieldSourceNone);
}
