using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace QuickER.ViewModels;

/// <summary>
/// エンティティ検索（Ctrl+F オーバーレイ）の状態とコマンドを担う partial
/// </summary>
/// <remarks>
/// テーブル名・カラム名の部分一致（大文字小文字無視）で絞り込み、
/// 最初の一致エンティティへ即座に視点移動＋選択する。Enter で次候補へ巡回、Esc で閉じる。
/// 視点移動そのものは <see cref="ScrollToEntityRequested"/> で View（コードビハインド）へ委譲する。
/// スクロールオフセットとビューポート実寸を持つのは View 側のため、座標適用はそこで行う。
/// </remarks>
public partial class MainViewModel
{
    /// <summary>検索オーバーレイの表示状態のバッキングフィールド</summary>
    private bool _isSearchOverlayVisible;

    /// <summary>検索クエリのバッキングフィールド（setter で絞り込みを実行する）</summary>
    private string _searchQuery = string.Empty;

    /// <summary>現在フォーカスしている検索結果のインデックス（未確定・0 件時は -1）</summary>
    private int _currentMatchIndex = -1;

    /// <summary>指定エンティティへ視点移動するよう View（コードビハインド）へ要求するイベント</summary>
    /// <remarks>現在の倍率を保ったまま該当エンティティをビューポート中央へ据える処理は View 側が行う</remarks>
    public event EventHandler<EntityViewModel>? ScrollToEntityRequested;

    /// <summary>検索オーバーレイを表示中かどうか</summary>
    public bool IsSearchOverlayVisible
    {
        get => _isSearchOverlayVisible;
        set => SetProperty(ref _isSearchOverlayVisible, value);
    }

    /// <summary>検索クエリ。設定するたびに現在の <see cref="Entities"/> から再評価する</summary>
    /// <remarks>
    /// 絞り込みは常に現在の Entities を走査する（結果コレクションのライブ同期はしない）。
    /// オーバーレイを開いたまま図が変わっても、入力・Enter 時の再評価で自然に整合するため
    /// エンティティ削除・図置換の購読は設けない。
    /// </remarks>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RunSearch();
            }
        }
    }

    /// <summary>検索結果（1 エンティティにつき最大 1 件）</summary>
    public ObservableCollection<EntitySearchResult> SearchResults { get; } = new();

    /// <summary>件数表示文字列（例 "2/5"。0 件時は "0/0"）</summary>
    public string MatchCountText
    {
        get
        {
            if (SearchResults.Count == 0)
            {
                return "0/0";
            }

            // インデックスは 0 始まりのため、表示は 1 始まりへ補正する
            return $"{_currentMatchIndex + 1}/{SearchResults.Count}";
        }
    }

    /// <summary>ListBox の選択に双方向バインドする、現在フォーカス中の検索結果（未確定時は null）</summary>
    /// <remarks>候補クリック時はこの setter 経由で該当エンティティへジャンプする</remarks>
    public EntitySearchResult? SelectedSearchResult
    {
        get =>
            _currentMatchIndex >= 0 && _currentMatchIndex < SearchResults.Count
                ? SearchResults[_currentMatchIndex]
                : null;
        set
        {
            // クリック等で結果が選ばれたら、そのインデックスへフォーカスを移してジャンプする
            if (value is null)
            {
                return;
            }

            var index = SearchResults.IndexOf(value);

            if (index < 0 || index == _currentMatchIndex)
            {
                return;
            }

            _currentMatchIndex = index;
            OnPropertyChanged(nameof(SelectedSearchResult));
            OnPropertyChanged(nameof(MatchCountText));
            JumpToCurrentMatch();
        }
    }

    /// <summary>検索オーバーレイを表示する（既存クエリがあれば再評価して視点も合わせる）</summary>
    [RelayCommand]
    private void OpenSearch()
    {
        IsSearchOverlayVisible = true;

        // 既にクエリが入っている状態で開き直したときも最新の図で絞り込み直す
        if (!string.IsNullOrEmpty(_searchQuery))
        {
            RunSearch();
        }
    }

    /// <summary>検索オーバーレイを閉じる（クエリ・結果は保持する）</summary>
    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOverlayVisible = false;
    }

    /// <summary>次の候補へフォーカスを移す（末尾の次は先頭へラップ）</summary>
    [RelayCommand]
    private void GoToNextMatch()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }

        // 末尾で押されたら先頭へ折り返す
        _currentMatchIndex = (_currentMatchIndex + 1) % SearchResults.Count;
        OnPropertyChanged(nameof(SelectedSearchResult));
        OnPropertyChanged(nameof(MatchCountText));
        JumpToCurrentMatch();
    }

    /// <summary>現在のクエリで <see cref="Entities"/> を再評価し、結果コレクションと視点を更新する</summary>
    private void RunSearch()
    {
        SearchResults.Clear();

        var query = _searchQuery?.Trim() ?? string.Empty;

        if (query.Length > 0)
        {
            foreach (var entity in Entities)
            {
                var result = BuildSearchResult(entity, query);

                if (result is not null)
                {
                    SearchResults.Add(result);
                }
            }
        }

        // 絞り込みのたびにフォーカスを先頭へ戻す（0 件時は -1）
        _currentMatchIndex = SearchResults.Count > 0 ? 0 : -1;

        OnPropertyChanged(nameof(SelectedSearchResult));
        OnPropertyChanged(nameof(MatchCountText));

        // インクリメンタル検索: 最初の一致へ即座に視点移動＋選択する
        if (_currentMatchIndex >= 0)
        {
            JumpToCurrentMatch();
        }
    }

    /// <summary>1 エンティティに対する検索結果を組み立てる。一致しなければ null</summary>
    /// <remarks>
    /// テーブル名一致を優先表記し、テーブル名が一致しない場合のみ最初に一致したカラム名を併記する。
    /// 複数カラムが一致しても表記は最初の 1 カラムのみ（候補は 1 エンティティにつき 1 件）。
    /// </remarks>
    private static EntitySearchResult? BuildSearchResult(EntityViewModel entity, string query)
    {
        // テーブル名一致は最優先。表記はテーブル名のみとする
        if ((entity.TableName ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new EntitySearchResult(entity, entity.TableName ?? string.Empty);
        }

        // テーブル名が一致しない場合、最初に一致したカラム名を「テーブル名 (カラム名)」で表記する
        foreach (var column in entity.Columns)
        {
            if ((column.Name ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return new EntitySearchResult(entity, $"{entity.TableName} ({column.Name})");
            }
        }

        return null;
    }

    /// <summary>現在フォーカス中の候補のエンティティを選択し、視点移動を要求する</summary>
    private void JumpToCurrentMatch()
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= SearchResults.Count)
        {
            return;
        }

        var entity = SearchResults[_currentMatchIndex].Entity;

        // 既存の選択ハイライトを流用する（単一選択＋他の選択解除）
        SelectSingleEntity(entity);

        // 現在の倍率を保ったまま該当エンティティをビューポート中央へ据えるよう View へ要求する
        ScrollToEntityRequested?.Invoke(this, entity);
    }
}

/// <summary>エンティティ検索の 1 候補（1 エンティティにつき 1 件）</summary>
/// <param name="Entity">ジャンプ対象のエンティティ</param>
/// <param name="DisplayText">候補リストに表示する文字列（テーブル名 or「テーブル名 (カラム名)」）</param>
public sealed record EntitySearchResult(EntityViewModel Entity, string DisplayText);
