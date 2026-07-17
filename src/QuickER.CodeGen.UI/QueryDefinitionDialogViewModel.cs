using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.CodeGen.UI.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.UI;

/// <summary>
/// 名前付きクエリ定義エディタ（マスター・ディテール）の ViewModel
/// </summary>
/// <remarks>
/// 入力の図（<see cref="ErDiagram" />）からエンティティ一覧と既存クエリを取り込み、
/// 編集はすべて複製した <see cref="QueryItemViewModel" /> に対して行う。OK 確定でのみ
/// <see cref="Result" /> に確定リストを設定し、キャンセルは元の定義に一切影響しない。
/// </remarks>
public partial class QueryDefinitionDialogViewModel : ObservableObject
{
    /// <summary>列名解決・条件検証・シグネチャプレビューに使う図のエンティティ一覧（モデル）</summary>
    private readonly IReadOnlyList<Entity> _entities;

    /// <summary>確定結果（OK 確定まで null。編集した定義の複製リスト）</summary>
    public List<QueryDefinition>? Result { get; private set; }

    /// <summary>ダイアログを閉じる際に呼ぶアクション（引数は確定可否）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>図からエンティティ一覧・既存クエリを取り込んで ViewModel を構築する</summary>
    /// <param name="diagram">エンティティと既存クエリを含む ER 図（この参照は変更しない）</param>
    public QueryDefinitionDialogViewModel(ErDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        _entities = diagram.Entities.ToList();
        Entities = new ObservableCollection<EntityChoice>(
            _entities.Select(e => new EntityChoice(e.Id, e.TableName))
        );

        foreach (var query in diagram.Queries)
        {
            Queries.Add(new QueryItemViewModel(query, _entities, Revalidate));
        }

        SelectedQuery = Queries.FirstOrDefault();
        Revalidate();
    }

    /// <summary>編集中のクエリ一覧（マスター）</summary>
    public ObservableCollection<QueryItemViewModel> Queries { get; } = new();

    /// <summary>エンティティ選択ドロップダウンの選択肢</summary>
    public ObservableCollection<EntityChoice> Entities { get; }

    /// <summary>右ペインに表示中のクエリ（未選択は null）</summary>
    [ObservableProperty]
    private QueryItemViewModel? _selectedQuery;

    /// <summary>入力エラーや補助メッセージ（フッタに赤字表示）</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>OK を実行できるか（全クエリがフォーム検証を満たす）</summary>
    private bool _canOk = true;

    partial void OnSelectedQueryChanged(QueryItemViewModel? value) =>
        RemoveQueryCommand.NotifyCanExecuteChanged();

    /// <summary>クエリを 1 件追加して選択する（既定エンティティは先頭）</summary>
    [RelayCommand]
    private void AddQuery()
    {
        var definition = new QueryDefinition
        {
            EntityId = _entities.FirstOrDefault()?.Id ?? Guid.Empty,
        };
        var item = new QueryItemViewModel(definition, _entities, Revalidate);
        Queries.Add(item);
        SelectedQuery = item;
        Revalidate();
    }

    /// <summary>選択中のクエリを削除する</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveQuery))]
    private void RemoveQuery()
    {
        if (SelectedQuery is null)
        {
            return;
        }

        var index = Queries.IndexOf(SelectedQuery);
        Queries.Remove(SelectedQuery);
        SelectedQuery = Queries.Count == 0 ? null : Queries[Math.Min(index, Queries.Count - 1)];
        Revalidate();
    }

    /// <summary>削除可能か（クエリを選択中）</summary>
    private bool CanRemoveQuery() => SelectedQuery is not null;

    /// <summary>編集結果を確定してダイアログを閉じる（不正時は閉じない）</summary>
    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok()
    {
        if (!_canOk)
        {
            return;
        }

        Result = Queries.Select(q => q.ToModel()).ToList();
        CloseAction?.Invoke(true);
    }

    /// <summary>OK 実行可否（フォーム検証結果）</summary>
    private bool CanOk() => _canOk;

    /// <summary>確定せずダイアログを閉じる（元の定義は変更しない）</summary>
    [RelayCommand]
    private void Cancel() => CloseAction?.Invoke(false);

    /// <summary>全クエリを検証し、状態メッセージと OK 可否を更新する</summary>
    /// <remarks>
    /// 深い検証（予約名など）は生成時診断に委ねる。ここではフォームとして最低限
    /// （名前必須・エンティティ必須・射影の DTO/フィールド・条件診断・同一エンティティ内の重複名）を見る。
    /// </remarks>
    public void Revalidate()
    {
        StatusMessage = FindFirstError() ?? string.Empty;
        _canOk = StatusMessage.Length == 0;
        OkCommand.NotifyCanExecuteChanged();
    }

    /// <summary>最初に見つかったフォーム検証エラー（なければ null）を返す</summary>
    private string? FindFirstError()
    {
        foreach (var query in Queries)
        {
            if (string.IsNullOrWhiteSpace(query.Name))
            {
                return Strings.QueryDialog_Status_NameRequired;
            }

            if (_entities.All(e => e.Id != query.EntityId))
            {
                return Strings.QueryDialog_Status_EntityRequired;
            }

            if (
                query.Returns == QueryReturnShape.Projection
                && (string.IsNullOrWhiteSpace(query.ResultTypeName) || query.Fields.Count == 0)
            )
            {
                return Strings.QueryDialog_Status_ProjectionRequired;
            }

            // スカラー戻り値は簡易 DSL では成立しない（生 SQL / 手動実装 専用）。
            // ラジオ無効化だけでは、選択済みで DSL へ切替・既存定義の読み込みで到達し得るため防御する。
            if (QueryItemViewModel.IsScalarDslConflict(query.Returns, query.Implementation))
            {
                return Strings.QueryDialog_ScalarRequiresSqlOrManual;
            }

            if (!query.IsConditionValid)
            {
                return Strings.QueryDialog_Status_ConditionInvalid;
            }
        }

        // 同一エンティティ内でのメソッド名重複（大文字小文字を区別しない）
        var duplicate = Queries
            .GroupBy(q => (q.EntityId, Name: q.Name.Trim().ToLowerInvariant()))
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            return string.Format(
                Strings.QueryDialog_Status_DuplicateName,
                duplicate.First().Name.Trim()
            );
        }

        return null;
    }
}

/// <summary>エンティティ選択ドロップダウンの選択肢</summary>
/// <param name="Id">エンティティ ID</param>
/// <param name="TableName">表示名（テーブル名）</param>
public sealed record EntityChoice(Guid Id, string TableName);
