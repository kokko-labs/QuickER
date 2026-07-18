using QuickER.CodeGen.CSharp.Queries;
using QuickER.Extensibility;

namespace QuickER.CodeGen.UI;

/// <summary>
/// カラム名がユーザー編集で変更されたとき、その列が属するエンティティの名前付きクエリの
/// 条件式（ミニ DSL）内の列参照を新名へ追従して書き換えるフォロワー。
/// </summary>
/// <remarks>
/// アプリ本体 <c>MainViewModel</c> の <c>OnColumnRenamed</c> から移設した機能。
/// 適用はエンティティ単位（<see cref="QueryDefinition.EntityId"/> 一致）なので、他エンティティに
/// 同名の列があっても巻き込まない。書き換えは <see cref="QueryConditionRenamer"/> が列参照のスパンだけを
/// 置換するため、パラメータ名や文字列リテラル中の同名文字列には影響しない。
/// </remarks>
public sealed class QueryConditionRenameFollower
{
    /// <summary>列リネーム通知の発火元・クエリ差し替え先となるホスト契約</summary>
    private readonly IErDiagramHost _host;

    /// <summary>依存を注入して生成する</summary>
    public QueryConditionRenameFollower(IErDiagramHost host)
    {
        _host = host;
    }

    /// <summary>ホストの列リネーム通知の購読を開始する（モジュール初期化時に 1 回だけ呼ぶ）</summary>
    public void Attach()
    {
        _host.ColumnRenamed += OnColumnRenamed;
    }

    /// <summary>
    /// リネームされた列を持つエンティティの名前付きクエリの条件式内の列参照を新名へ書き換える。
    /// </summary>
    /// <remarks>
    /// 1 件も書き換えなければ <see cref="IErDiagramHost.ReplaceQueries"/> を呼ばない
    /// （無関係なリネームのたびに自動保存を走らせないため）。
    /// </remarks>
    private void OnColumnRenamed(object? sender, ColumnRenamedEventArgs e)
    {
        var queries = _host.GetDiagram().Queries;
        var rewritten = false;

        foreach (var query in queries.Where(query => query.EntityId == e.EntityId))
        {
            if (query.Condition is { } condition)
            {
                query.Condition = QueryConditionRenamer.RenameColumn(
                    condition,
                    e.OldName,
                    e.NewName
                );
                rewritten = true;
            }
        }

        // 対象エンティティに条件式を持つクエリが 1 件も無ければ書き戻さない（自動保存を抑止する）
        if (rewritten)
        {
            _host.ReplaceQueries(queries);
        }
    }
}
