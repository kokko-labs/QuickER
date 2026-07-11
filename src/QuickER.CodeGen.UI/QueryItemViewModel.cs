using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;

namespace QuickER.CodeGen.UI;

/// <summary>
/// クエリ定義 1 件の編集用 ViewModel（マスター・ディテールのディテール側）
/// </summary>
/// <remarks>
/// 入力の <see cref="QueryDefinition" /> を複製して構築し、編集はすべてこの複製に対して行う
/// （元の定義は OK 確定まで一切変更しない）。エンティティ・列は図のスナップショットを参照する。
/// </remarks>
public partial class QueryItemViewModel : ObservableObject
{
    /// <summary>編集対象の一意識別子（元定義から引き継ぐ）</summary>
    public Guid Id { get; }

    /// <summary>列名解決・条件検証・シグネチャプレビューに使う図のエンティティ一覧</summary>
    private readonly IReadOnlyList<Entity> _entities;

    /// <summary>親（ダイアログ VM）へ変更を通知するためのコールバック（OK 可否・重複名の再評価用）</summary>
    private readonly Action? _notifyParent;

    /// <summary>子コレクションの一括入替中に個別変更フックの発火を抑止するフラグ</summary>
    private bool _suppressChildRefresh;

    /// <summary>クエリ定義の複製から編集用 ViewModel を構築する</summary>
    /// <param name="source">複製元の定義（この参照は変更しない）</param>
    /// <param name="entities">図のエンティティ一覧（列・エンティティ名の解決先）</param>
    /// <param name="notifyParent">編集内容が変わったときに親へ知らせるコールバック</param>
    public QueryItemViewModel(
        QueryDefinition source,
        IReadOnlyList<Entity> entities,
        Action? notifyParent = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entities);

        Id = source.Id;
        _entities = entities;
        _notifyParent = notifyParent;

        _suppressChildRefresh = true;
        try
        {
            _entityId = source.EntityId;
            _name = source.Name;
            _description = source.Description;
            _returns = source.Returns;
            _scalarType = source.ScalarType ?? string.Empty;
            _condition = source.Condition ?? string.Empty;
            _hasPaging = source.HasPaging;
            _implementation = source.Implementation;
            _sqlServerSql = source.Sql.TryGetValue("sqlserver", out var mssql)
                ? mssql
                : string.Empty;
            _sqliteSql = source.Sql.TryGetValue("sqlite", out var sqlite) ? sqlite : string.Empty;
            _resultTypeName = source.ResultTypeName ?? string.Empty;

            foreach (var parameter in source.Parameters)
            {
                Parameters.Add(
                    Track(
                        new QueryParameterViewModel(DeriveFieldToken)
                        {
                            Name = parameter.Name,
                            Type = parameter.Type,
                            IsList = parameter.IsList,
                            SourceColumnId = parameter.SourceColumnId,
                        }
                    )
                );
            }

            foreach (var ordering in source.OrderBy)
            {
                OrderBy.Add(
                    Track(
                        new QueryOrderingViewModel
                        {
                            ColumnId = ordering.ColumnId,
                            Descending = ordering.Descending,
                        }
                    )
                );
            }

            foreach (var field in source.Fields)
            {
                Fields.Add(
                    Track(
                        new ProjectionFieldViewModel(DeriveFieldToken)
                        {
                            Name = field.Name,
                            Type = field.Type,
                            SourceColumnId = field.SourceColumnId,
                        }
                    )
                );
            }

            RebuildAvailableColumns();
        }
        finally
        {
            _suppressChildRefresh = false;
        }

        ValidateCondition();
    }

    // ===== スカラー/基本プロパティ =====

    /// <summary>クエリが属するエンティティの ID</summary>
    [ObservableProperty]
    private Guid _entityId;

    /// <summary>メソッド名の意味部分（生成時に Async を付与）</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>クエリの説明（生成メソッドの XML コメントに反映）</summary>
    [ObservableProperty]
    private string _description;

    /// <summary>戻り形（一覧・単一・件数・スカラー・射影）</summary>
    [ObservableProperty]
    private QueryReturnShape _returns;

    /// <summary>戻り形がスカラーのときの型トークン（例: <c>decimal(10,2)</c>）</summary>
    [ObservableProperty]
    private string _scalarType;

    /// <summary>検索条件（ミニ DSL）</summary>
    [ObservableProperty]
    private string _condition;

    /// <summary>ページングを有効にするか</summary>
    [ObservableProperty]
    private bool _hasPaging;

    /// <summary>実装方式（ミニ DSL・自由 SQL・manual）</summary>
    [ObservableProperty]
    private QueryImplementationKind _implementation;

    /// <summary>自由 SQL（SQL Server 方言）</summary>
    [ObservableProperty]
    private string _sqlServerSql;

    /// <summary>自由 SQL（SQLite 方言）</summary>
    [ObservableProperty]
    private string _sqliteSql;

    /// <summary>戻り形が射影のときの DTO 型名</summary>
    [ObservableProperty]
    private string _resultTypeName;

    // ===== 子コレクション =====

    /// <summary>パラメータ一覧（生成メソッドの引数）</summary>
    public ObservableCollection<QueryParameterViewModel> Parameters { get; } = new();

    /// <summary>並び順一覧</summary>
    public ObservableCollection<QueryOrderingViewModel> OrderBy { get; } = new();

    /// <summary>射影フィールド一覧</summary>
    public ObservableCollection<ProjectionFieldViewModel> Fields { get; } = new();

    /// <summary>並び順・射影の列ドロップダウン用の選択肢（対象エンティティの列）</summary>
    public ObservableCollection<ColumnChoice> AvailableColumns { get; } = new();

    /// <summary>射影フィールドの参照元列ドロップダウン用の選択肢（先頭に「なし＝自由フィールド」）</summary>
    public ObservableCollection<ColumnChoice> AvailableColumnsWithNone { get; } = new();

    // ===== 条件検証 =====

    /// <summary>条件式の診断メッセージ一覧（空なら有効）</summary>
    public ObservableCollection<string> ConditionDiagnostics { get; } = new();

    /// <summary>条件式が有効か（診断なし。ミニ DSL 以外は常に有効）</summary>
    [ObservableProperty]
    private bool _isConditionValid = true;

    // ===== 表示制御（派生） =====

    /// <summary>選択中エンティティの表示名（マスター一覧のグループ見出しに使う）</summary>
    public string EntityName =>
        _entities.FirstOrDefault(e => e.Id == EntityId)?.TableName ?? string.Empty;

    /// <summary>生成されるメソッド名（Async 付与形）のプレビュー</summary>
    public string GeneratedMethodName =>
        string.IsNullOrWhiteSpace(Name) ? string.Empty
        : Name.EndsWith("Async", StringComparison.Ordinal) ? Name
        : Name + "Async";

    /// <summary>戻り形＝スカラーのときの型入力欄を表示するか</summary>
    public bool ShowScalarType => Returns == QueryReturnShape.Scalar;

    /// <summary>戻り形＝射影のときの射影設定を表示するか</summary>
    public bool ShowProjection => Returns == QueryReturnShape.Projection;

    /// <summary>実装方式＝ミニ DSL のとき（条件欄を有効化・即時検証する）</summary>
    public bool IsDslImplementation => Implementation == QueryImplementationKind.Dsl;

    /// <summary>実装方式＝生 SQL のとき（方言別 SQL 欄を表示する）</summary>
    public bool ShowSqlEditors => Implementation == QueryImplementationKind.Sql;

    /// <summary>戻り形＝スカラーを選べるか（DSL 以外＝生 SQL / 手動実装のときのみ）</summary>
    /// <remarks>
    /// スカラーは生 SQL か手動実装でしか成立しない（DSL は列比較の条件式に閉じる）ため、
    /// DSL のときはラジオを無効化して選ばせない。既に選ばれている場合はフォーム検証が弾く。
    /// </remarks>
    public bool CanSelectScalar => Implementation != QueryImplementationKind.Dsl;

    // ===== 戻り形ラジオ（bool プロキシ。既存ダイアログの流儀に合わせる） =====

    /// <summary>戻り形＝一覧</summary>
    public bool ReturnsList
    {
        get => Returns == QueryReturnShape.List;
        set
        {
            if (value)
            {
                Returns = QueryReturnShape.List;
            }
        }
    }

    /// <summary>戻り形＝単一</summary>
    public bool ReturnsSingle
    {
        get => Returns == QueryReturnShape.Single;
        set
        {
            if (value)
            {
                Returns = QueryReturnShape.Single;
            }
        }
    }

    /// <summary>戻り形＝件数</summary>
    public bool ReturnsCount
    {
        get => Returns == QueryReturnShape.Count;
        set
        {
            if (value)
            {
                Returns = QueryReturnShape.Count;
            }
        }
    }

    /// <summary>戻り形＝スカラー</summary>
    public bool ReturnsScalar
    {
        get => Returns == QueryReturnShape.Scalar;
        set
        {
            if (value)
            {
                Returns = QueryReturnShape.Scalar;
            }
        }
    }

    /// <summary>戻り形＝射影</summary>
    public bool ReturnsProjection
    {
        get => Returns == QueryReturnShape.Projection;
        set
        {
            if (value)
            {
                Returns = QueryReturnShape.Projection;
            }
        }
    }

    // ===== 実装方式ラジオ（bool プロキシ） =====

    /// <summary>実装方式＝ミニ DSL</summary>
    public bool ImplementationDsl
    {
        get => Implementation == QueryImplementationKind.Dsl;
        set
        {
            if (value)
            {
                Implementation = QueryImplementationKind.Dsl;
            }
        }
    }

    /// <summary>実装方式＝自由 SQL</summary>
    public bool ImplementationSql
    {
        get => Implementation == QueryImplementationKind.Sql;
        set
        {
            if (value)
            {
                Implementation = QueryImplementationKind.Sql;
            }
        }
    }

    /// <summary>実装方式＝manual</summary>
    public bool ImplementationManual
    {
        get => Implementation == QueryImplementationKind.Manual;
        set
        {
            if (value)
            {
                Implementation = QueryImplementationKind.Manual;
            }
        }
    }

    // ===== 変更フック =====

    partial void OnEntityIdChanged(Guid value)
    {
        RebuildAvailableColumns();
        ClearStaleColumnReferences();
        OnPropertyChanged(nameof(EntityName));
        RaiseSignatureChanged();
        ValidateCondition();
        _notifyParent?.Invoke();
    }

    /// <summary>エンティティ変更後、新しいエンティティに存在しない列参照を掃除する</summary>
    /// <remarks>
    /// 並び順行は列参照そのものが本体のため行ごと取り除き、射影フィールドは参照元列を
    /// 「なし（自由フィールド）」へ解除して名前・型トークンは保持する。旧参照を残すと
    /// ドロップダウンが空欄のまま確定でき、生成時まで気づけない壊れた定義になるため。
    /// 条件テキストはユーザーの入力文言なので保持し、旧エンティティの列参照は
    /// 条件検証の診断（赤字表示・OK 無効化）として表面化させる。
    /// </remarks>
    private void ClearStaleColumnReferences()
    {
        var validIds = AvailableColumns.Select(c => c.Id).ToHashSet();

        foreach (var ordering in OrderBy.Where(o => !validIds.Contains(o.ColumnId)).ToList())
        {
            OrderBy.Remove(Untrack(ordering));
        }

        foreach (var field in Fields)
        {
            if (field.SourceColumnId is { } id && !validIds.Contains(id))
            {
                field.SourceColumnId = null;
            }
        }

        // 列参照で型付けされたパラメータも同様に「トークン型付け」へ解除する（名前・型表示は保持）
        foreach (var parameter in Parameters)
        {
            if (
                parameter.SourceColumnId is { } parameterColumnId
                && !validIds.Contains(parameterColumnId)
            )
            {
                parameter.SourceColumnId = null;
            }
        }
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(GeneratedMethodName));
        RaiseSignatureChanged();
        _notifyParent?.Invoke();
    }

    partial void OnReturnsChanged(QueryReturnShape value)
    {
        OnPropertyChanged(nameof(ReturnsList));
        OnPropertyChanged(nameof(ReturnsSingle));
        OnPropertyChanged(nameof(ReturnsCount));
        OnPropertyChanged(nameof(ReturnsScalar));
        OnPropertyChanged(nameof(ReturnsProjection));
        OnPropertyChanged(nameof(ShowScalarType));
        OnPropertyChanged(nameof(ShowProjection));
        RaiseSignatureChanged();
        _notifyParent?.Invoke();
    }

    partial void OnScalarTypeChanged(string value) => RaiseSignatureChanged();

    partial void OnResultTypeNameChanged(string value)
    {
        RaiseSignatureChanged();
        _notifyParent?.Invoke();
    }

    partial void OnConditionChanged(string value) => ValidateCondition();

    partial void OnHasPagingChanged(bool value) => RaiseSignatureChanged();

    partial void OnImplementationChanged(QueryImplementationKind value)
    {
        // 簡易 DSL はスカラー戻り値を持てないため、スカラー選択中に DSL へ切り替えたら既定（一覧）へ戻す。
        // 図の読み込み時はフィールド直接代入でここを通らないため、不正な既存定義はフォーム検証が防ぐ
        if (value == QueryImplementationKind.Dsl && Returns == QueryReturnShape.Scalar)
        {
            Returns = QueryReturnShape.List;
        }

        OnPropertyChanged(nameof(ImplementationDsl));
        OnPropertyChanged(nameof(ImplementationSql));
        OnPropertyChanged(nameof(ImplementationManual));
        OnPropertyChanged(nameof(IsDslImplementation));
        OnPropertyChanged(nameof(ShowSqlEditors));
        OnPropertyChanged(nameof(CanSelectScalar));
        ValidateCondition();
        _notifyParent?.Invoke();
    }

    partial void OnIsConditionValidChanged(bool value) => _notifyParent?.Invoke();

    // ===== 子行の追加・削除コマンド =====

    /// <summary>パラメータ行を追加する</summary>
    [RelayCommand]
    private void AddParameter() =>
        Parameters.Add(
            Track(new QueryParameterViewModel(DeriveFieldToken) { Name = "param", Type = "int32" })
        );

    /// <summary>指定パラメータ行を削除する</summary>
    [RelayCommand]
    private void RemoveParameter(QueryParameterViewModel? parameter)
    {
        if (parameter is not null && Parameters.Remove(Untrack(parameter)))
        {
            RaiseSignatureChanged();
            ValidateCondition();
            _notifyParent?.Invoke();
        }
    }

    /// <summary>並び順行を追加する（既定は先頭列・昇順）</summary>
    [RelayCommand]
    private void AddOrdering() =>
        OrderBy.Add(
            Track(
                new QueryOrderingViewModel
                {
                    ColumnId = AvailableColumns.FirstOrDefault()?.Id ?? Guid.Empty,
                }
            )
        );

    /// <summary>指定並び順行を削除する</summary>
    [RelayCommand]
    private void RemoveOrdering(QueryOrderingViewModel? ordering)
    {
        if (ordering is not null)
        {
            OrderBy.Remove(Untrack(ordering));
        }
    }

    /// <summary>射影フィールド行を追加する（既定は自由フィールド）</summary>
    [RelayCommand]
    private void AddField()
    {
        Fields.Add(
            Track(new ProjectionFieldViewModel(DeriveFieldToken) { Name = "Field", Type = "int32" })
        );
        _notifyParent?.Invoke();
    }

    /// <summary>指定射影フィールド行を削除する</summary>
    [RelayCommand]
    private void RemoveField(ProjectionFieldViewModel? field)
    {
        if (field is not null && Fields.Remove(Untrack(field)))
        {
            _notifyParent?.Invoke();
        }
    }

    // ===== モデル化 =====

    /// <summary>編集内容から <see cref="QueryDefinition" /> を組み立てる（Id は保持）</summary>
    public QueryDefinition ToModel()
    {
        var sql = new Dictionary<string, string>();

        if (Implementation == QueryImplementationKind.Sql)
        {
            if (!string.IsNullOrWhiteSpace(SqlServerSql))
            {
                sql["sqlserver"] = SqlServerSql;
            }

            if (!string.IsNullOrWhiteSpace(SqliteSql))
            {
                sql["sqlite"] = SqliteSql;
            }
        }

        return new QueryDefinition
        {
            Id = Id,
            EntityId = EntityId,
            Name = Name.Trim(),
            Description = Description,
            Returns = Returns,
            ScalarType = string.IsNullOrWhiteSpace(ScalarType) ? null : ScalarType.Trim(),
            Parameters = Parameters
                .Select(p => new QueryParameter
                {
                    Name = p.Name.Trim(),
                    Type = p.Type.Trim(),
                    IsList = p.IsList,
                    SourceColumnId = p.SourceColumnId,
                })
                .ToList(),
            Condition =
                Implementation == QueryImplementationKind.Dsl
                && !string.IsNullOrWhiteSpace(Condition)
                    ? Condition
                    : null,
            OrderBy = OrderBy
                .Select(o => new QueryOrdering { ColumnId = o.ColumnId, Descending = o.Descending })
                .ToList(),
            HasPaging = HasPaging,
            Implementation = Implementation,
            Sql = sql,
            ResultTypeName = string.IsNullOrWhiteSpace(ResultTypeName)
                ? null
                : ResultTypeName.Trim(),
            Fields = Fields
                .Select(f => new ProjectionField
                {
                    Name = f.Name.Trim(),
                    Type = f.Type.Trim(),
                    SourceColumnId = f.SourceColumnId,
                })
                .ToList(),
        };
    }

    // ===== 内部処理 =====

    /// <summary>条件式（ミニ DSL）を検証し、診断一覧・有効フラグを更新する</summary>
    private void ValidateCondition()
    {
        ConditionDiagnostics.Clear();

        // ミニ DSL 以外・空条件・エンティティ未選択のときは検証をスキップ（有効扱い）。
        // エンティティ未選択はフォーム全体の検証（エンティティ必須）が別途弾く。
        var entity = _entities.FirstOrDefault(e => e.Id == EntityId);

        if (
            Implementation != QueryImplementationKind.Dsl
            || string.IsNullOrWhiteSpace(Condition)
            || entity is null
        )
        {
            IsConditionValid = true;
            return;
        }

        var parameters = Parameters
            .Select(p => new QueryParameter
            {
                Name = p.Name,
                Type = p.Type,
                IsList = p.IsList,
            })
            .ToList();
        var result = QueryConditionParser.ParseAndValidate(Condition, entity, parameters);

        foreach (var diagnostic in result.Diagnostics)
        {
            ConditionDiagnostics.Add(diagnostic.Message);
        }

        IsConditionValid = result.Success;
    }

    /// <summary>対象エンティティの列から、並び順・射影用の選択肢コレクションを作り直す</summary>
    private void RebuildAvailableColumns()
    {
        AvailableColumns.Clear();
        AvailableColumnsWithNone.Clear();
        AvailableColumnsWithNone.Add(ColumnChoice.None);

        var entity = _entities.FirstOrDefault(e => e.Id == EntityId);

        if (entity is null)
        {
            return;
        }

        foreach (var column in entity.Columns)
        {
            var choice = new ColumnChoice(column.Id, column.Name);
            AvailableColumns.Add(choice);
            AvailableColumnsWithNone.Add(choice);
        }
    }

    /// <summary>参照元列 ID から射影フィールドの型トークン（列の宣言型）を導出する</summary>
    /// <remarks>
    /// 列サブセット射影の型は生成時に列型から解決されるため、ここでは表示用に列の宣言型を写す。
    /// </remarks>
    private string? DeriveFieldToken(Guid? columnId)
    {
        if (columnId is not { } id)
        {
            return null;
        }

        var entity = _entities.FirstOrDefault(e => e.Id == EntityId);

        return entity?.Columns.FirstOrDefault(c => c.Id == id)?.DataType;
    }

    /// <summary>シグネチャプレビューの再計算を通知する</summary>
    private void RaiseSignatureChanged() => OnPropertyChanged(nameof(SignaturePreview));

    /// <summary>戻り形とパラメータから近似シグネチャプレビューを組み立てる（表示専用の目安）</summary>
    public string SignaturePreview
    {
        get
        {
            var methodName = string.IsNullOrWhiteSpace(GeneratedMethodName)
                ? "Query"
                : GeneratedMethodName;
            var entity = _entities.FirstOrDefault(e => e.Id == EntityId);
            var entityClassName = entity is null
                ? "Entity"
                : QueryTypeTokenFormatter.ToEntityClassName(entity.TableName);

            var returnType = Returns switch
            {
                QueryReturnShape.List => $"Task<IReadOnlyList<{entityClassName}>>",
                QueryReturnShape.Single => $"Task<{entityClassName}?>",
                QueryReturnShape.Count => "Task<int>",
                QueryReturnShape.Scalar =>
                    $"Task<{QueryTypeTokenFormatter.ToCSharpType(ScalarType)}>",
                QueryReturnShape.Projection =>
                    $"Task<IReadOnlyList<{(string.IsNullOrWhiteSpace(ResultTypeName) ? "Row" : ResultTypeName.Trim())}>>",
                _ => "Task",
            };

            var arguments = new List<string>();

            foreach (var parameter in Parameters)
            {
                // 列参照型付けは列の宣言型からの近似（VO 有効時の実際の型は生成時に確定する）
                var type = QueryTypeTokenFormatter.ToCSharpType(
                    parameter.SourceColumnId is { } sourceColumnId
                        ? DeriveFieldToken(sourceColumnId) ?? parameter.Type
                        : parameter.Type
                );
                var name = string.IsNullOrWhiteSpace(parameter.Name)
                    ? "arg"
                    : parameter.Name.Trim();
                arguments.Add(
                    parameter.IsList ? $"IReadOnlyList<{type}> {name}" : $"{type} {name}"
                );
            }

            if (HasPaging)
            {
                arguments.Add("int take");
                arguments.Add("int skip = 0");
            }

            arguments.Add("CancellationToken cancellationToken = default");

            return $"{returnType} {methodName}({string.Join(", ", arguments)})";
        }
    }

    /// <summary>子行の PropertyChanged を購読し、シグネチャ・条件検証・OK 可否へ波及させる</summary>
    private T Track<T>(T child)
        where T : INotifyPropertyChanged
    {
        child.PropertyChanged += OnChildPropertyChanged;
        return child;
    }

    /// <summary>子行の購読を解除して返す（削除時に使う）</summary>
    private T Untrack<T>(T child)
        where T : INotifyPropertyChanged
    {
        child.PropertyChanged -= OnChildPropertyChanged;
        return child;
    }

    /// <summary>子行（パラメータ・並び順・射影）の変更を上位の派生値へ反映する</summary>
    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressChildRefresh)
        {
            return;
        }

        if (sender is QueryParameterViewModel)
        {
            RaiseSignatureChanged();
            ValidateCondition();
            _notifyParent?.Invoke();
        }
        else if (sender is ProjectionFieldViewModel)
        {
            _notifyParent?.Invoke();
        }
    }
}

/// <summary>クエリパラメータ 1 件の編集用 ViewModel</summary>
public partial class QueryParameterViewModel : ObservableObject
{
    /// <summary>参照元列 ID から型トークン（列の宣言型）を導出する関数（表示補助。null 可）</summary>
    private readonly Func<Guid?, string?>? _deriveToken;

    /// <summary>型トークン導出なしで構築する（テスト用）</summary>
    public QueryParameterViewModel() { }

    /// <summary>型トークン導出関数を注入して構築する</summary>
    /// <param name="deriveToken">参照元列 ID → 型トークンの導出関数</param>
    public QueryParameterViewModel(Func<Guid?, string?> deriveToken)
    {
        _deriveToken = deriveToken;
    }

    /// <summary>パラメータ名</summary>
    [ObservableProperty]
    private string _name = "param";

    /// <summary>型トークン（方言中立。例: <c>int32</c> / <c>string(50)</c>。列参照時は列由来・編集不可）</summary>
    [ObservableProperty]
    private string _type = "int32";

    /// <summary>リスト型か（IN 条件用）</summary>
    [ObservableProperty]
    private bool _isList;

    /// <summary>型付けの参照元列 ID（null＝型トークンで型付け）。列を選ぶと VO 有効の図では VO 型の引数になる</summary>
    [ObservableProperty]
    private Guid? _sourceColumnId;

    /// <summary>型トークンを手入力できるか（列参照でないときのみ）</summary>
    public bool IsTypeEditable => SourceColumnId is null;

    partial void OnSourceColumnIdChanged(Guid? value)
    {
        // 参照元列を選ぶと型表示は列由来になる。「なし」へ戻したときは手入力値を保持する。
        if (value is not null && _deriveToken?.Invoke(value) is { } token)
        {
            Type = token;
        }

        OnPropertyChanged(nameof(IsTypeEditable));
    }
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
public partial class ProjectionFieldViewModel : ObservableObject
{
    /// <summary>参照元列 ID から型トークン（列の宣言型）を導出する関数</summary>
    private readonly Func<Guid?, string?> _deriveToken;

    /// <summary>型トークン導出関数を注入して構築する</summary>
    /// <param name="deriveToken">参照元列 ID → 型トークンの導出関数</param>
    public ProjectionFieldViewModel(Func<Guid?, string?> deriveToken)
    {
        _deriveToken = deriveToken;
    }

    /// <summary>フィールド名（DTO のプロパティ名）</summary>
    [ObservableProperty]
    private string _name = "Field";

    /// <summary>型トークン（参照元列があるときは列由来・編集不可）</summary>
    [ObservableProperty]
    private string _type = "int32";

    /// <summary>参照元列 ID（null＝自由フィールド）</summary>
    [ObservableProperty]
    private Guid? _sourceColumnId;

    /// <summary>型トークンを手入力できるか（自由フィールドのときのみ）</summary>
    public bool IsTypeEditable => SourceColumnId is null;

    partial void OnSourceColumnIdChanged(Guid? value)
    {
        // 参照元列を選ぶと型は列由来になる。「なし」へ戻したときは手入力値を保持する。
        if (value is not null && _deriveToken(value) is { } token)
        {
            Type = token;
        }

        OnPropertyChanged(nameof(IsTypeEditable));
    }
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
