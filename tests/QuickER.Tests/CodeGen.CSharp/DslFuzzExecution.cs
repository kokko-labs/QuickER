using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 簡易 DSL の総当たりの実行部: 各実装先へ同じ行を自分のリポジトリで入れ、読み戻した行へオラクルを当てて結果と照合する
/// </summary>
/// <remarks>
/// <para>
/// 期待値は「その実装先から読み戻した全行」へオラクルを当てて求める（TranslatorParity と同じ流儀＝格納で値が変わる型でも、
/// 条件の意味だけを比べる）。オラクルが判定しない行（大文字小文字の扱いが実装先任せの行）は、結果にあっても無くてもよい。
/// </para>
/// <para>
/// 文字列の大文字小文字・末尾の空白の扱いは、実装先ごとの <see cref="DslFuzzBackend.Rule"/> が
/// docs/code-generation(.ja).md の「大文字小文字・空白・日付部品」の表から写す。
/// </para>
/// </remarks>
internal static class DslFuzzExecution
{
    /// <summary>単位を各実装先で実行し、食い違いを記録する（実装先ごとに、結果を照合したクエリの数を返す）</summary>
    public static async Task<IReadOnlyDictionary<string, int>> RunAsync(
        FuzzFailureLog ledger,
        DslFuzzUnit unit,
        DslFuzzTarget target,
        Assembly assembly,
        ErDiagram diagram,
        CancellationToken ct,
        DslFuzzCoverage? coverage = null
    )
    {
        var executed = new Dictionary<string, int>(StringComparer.Ordinal);
        var types = assembly.GetTypes();
        var probe = types.Single(t => t.Name == DslFuzzProbe.TypeName);
        var repositoryType = types.Single(t => t.IsInterface && t.Name == "IItemRepository");
        var entityType = types.Single(t => t.IsClass && t.Name == "ItemEntity");
        var shape = new EntityShape(entityType, unit);

        foreach (var backend in target.Backends)
        {
            await using var session = await backend.OpenAsync(probe, diagram, ct);
            executed[backend.Name] = await RunBackendAsync(
                ledger,
                $"{target.Suite}-exec:{backend.Name}:{unit.Name}",
                unit,
                backend,
                session.Services.GetRequiredService(repositoryType),
                repositoryType,
                shape,
                coverage is null
                    ? null
                    : query => coverage.Observe(target.Dbms, backend.Name, query),
                ct
            );
        }

        return executed;
    }

    private static async Task<int> RunBackendAsync(
        FuzzFailureLog ledger,
        string entrance,
        DslFuzzUnit unit,
        DslFuzzBackend backend,
        object repository,
        Type repositoryType,
        EntityShape shape,
        Action<DslQuery>? observeChecked,
        CancellationToken ct
    )
    {
        var rule = backend.Rule;
        var checkedCount = 0;
        List<Dictionary<string, object?>> rows;

        try
        {
            foreach (var row in unit.Rows)
            {
                await InvokeAsync(
                    repository,
                    Method(repositoryType, "InsertAsync"),
                    new() { ["entity"] = shape.Create(row) },
                    ct
                );
            }

            var stored = (IEnumerable)
                (await InvokeAsync(repository, Method(repositoryType, "GetAllAsync"), [], ct))!;
            rows = stored.Cast<object>().Select(shape.Read).ToList();
        }
        catch (Exception ex)
        {
            ledger.Record(
                new(entrance, "setup-failed", Unwrap(ex).GetType().Name),
                $"seed={unit.Seed}: {Unwrap(ex).Message}"
            );

            return 0;
        }

        var mismatchedKinds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var query in unit.Queries)
        {
            if (backend.Excludes?.Invoke(query) == true)
            {
                continue;
            }

            var key = FailureDifference(query, mismatchedKinds);

            try
            {
                // メソッド名はクエリ名をパスカルケースにしたもの（A1n → A1NAsync）
                var method = repositoryType
                    .GetMethods()
                    .FirstOrDefault(m =>
                        string.Equals(
                            m.Name,
                            query.Name + "Async",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                if (method is null)
                {
                    ledger.Record(
                        new(entrance, "method-missing", key),
                        $"seed={unit.Seed} {query.Name}"
                    );

                    continue;
                }

                var arguments = query.Parameters.ToDictionary(p => p.Name, p => (object?)p);
                var result = await InvokeAsync(repository, method, arguments, ct);
                var problem = Check(
                    query,
                    result,
                    rows,
                    rule,
                    backend.BinaryRule,
                    backend.NullOrder,
                    shape
                );
                checkedCount++;

                // 照合したクエリの原子を網羅として数える（宣言の誤りは下の catch で受け止めずにそのまま投げる）
                observeChecked?.Invoke(query);

                if (problem is null)
                {
                    continue;
                }

                if (query.IsAtomQuery)
                {
                    mismatchedKinds.Add(query.Atoms.Single().Kind);
                }

                if (key is not null)
                {
                    ledger.Record(
                        new(entrance, "mismatch", key),
                        $"seed={unit.Seed} {query.Name}: {query.Text}{Describe(query)} → {problem}"
                    );
                }
            }
            catch (Exception ex) when (ex is not CoverageDeclarationException)
            {
                if (query.IsAtomQuery)
                {
                    mismatchedKinds.Add(query.Atoms.Single().Kind);
                }

                // 合成のクエリの原子が原子のクエリで既に失敗していれば重ねて数えない
                if (key is null)
                {
                    continue;
                }

                ledger.Record(
                    new(entrance, "exception", $"{key}:{Unwrap(ex).GetType().Name}"),
                    $"seed={unit.Seed} {query.Name}: {query.Text}{Describe(query)} → {Unwrap(ex).Message}"
                );
            }
        }

        return checkedCount;
    }

    /// <summary>
    /// 食い違いのキーの違いの部分（原子のクエリは原子の種類と否定の向き・合成のクエリは戻り形。
    /// 合成のクエリの原子が原子のクエリで食い違っていれば null＝記録しない）
    /// </summary>
    private static string? FailureDifference(DslQuery query, HashSet<string> mismatchedKinds)
    {
        if (query.IsAtomQuery)
        {
            var atom = query.Atoms.Single();
            var outer = query.Condition is DslNot ? "not-" : "";

            return $"{atom.Kind}:{outer}{(atom.InherentNegation ? "neg" : "pos")}";
        }

        // 並び替えの列を持つクエリ・パラメータを使い回すクエリは、合成と分けて名指しする
        var family =
            query.Orderings.Count > 0
            || (query.Returns == QueryReturnShape.List && query.ListOrdered)
                ? "ordering"
            : query.SharesParameter ? "reuse"
            : "composition";

        return query.Atoms.Any(atom => mismatchedKinds.Contains(atom.Kind))
            ? null
            : $"{family}:{query.Returns.ToString().ToLowerInvariant()}";
    }

    private static string Describe(DslQuery query) =>
        string.Concat(
            query.Parameters.Select(p =>
                $" @{p.Name}=[{string.Join(",", p.Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)))}]"
            )
        );

    /// <summary>結果を、読み戻した行へオラクルを当てた期待値と比べる（一致すれば null）</summary>
    /// <remarks>
    /// 並び替えを持つクエリ（単一・射影・並べる一覧）は、「追加の列 → 主キー」の並び（NULL の位置は実装先の
    /// <paramref name="nulls"/>）で期待を決める。単一は並びで最初に真になる行（その前の判定しない行も受ける）、射影と一覧は
    /// 返した行を同じ並びで並べ直した順と一致すること。
    /// </remarks>
    private static string? Check(
        DslQuery query,
        object? result,
        List<Dictionary<string, object?>> rows,
        DslCaseRule rule,
        DslBinaryRule binaryRule,
        DslNullOrder nulls,
        EntityShape shape
    )
    {
        var verdicts = rows.Select(row =>
                (
                    Key: row[shape.KeyColumn],
                    Verdict: DslFuzz.Evaluate(query.Condition, row, rule, binaryRule: binaryRule),
                    Row: row
                )
            )
            .ToList();
        var ordering = Comparer<Dictionary<string, object?>>.Create(
            (a, b) => CompareRows(query, shape, nulls, a, b)
        );
        var definite = verdicts
            .Where(v => v.Verdict == true)
            .Select(v => KeyText(v.Key))
            .ToHashSet();
        var possible = verdicts
            .Where(v => v.Verdict != false)
            .Select(v => KeyText(v.Key))
            .ToHashSet();

        switch (query.Returns)
        {
            case QueryReturnShape.Count:
            {
                var count = Convert.ToInt64(result, CultureInfo.InvariantCulture);

                return count >= definite.Count && count <= possible.Count
                    ? null
                    : $"count={count} expected={definite.Count}..{possible.Count}";
            }

            case QueryReturnShape.Single:
            {
                var actual = result is null ? null : KeyText(shape.KeyOf(result));
                var acceptable = new HashSet<string?>();

                foreach (var (key, verdict, _) in verdicts.OrderBy(v => v.Row, ordering))
                {
                    if (verdict is null)
                    {
                        acceptable.Add(KeyText(key));
                    }
                    else if (verdict == true)
                    {
                        acceptable.Add(KeyText(key));
                        break;
                    }
                }

                if (!verdicts.Any(v => v.Verdict == true))
                {
                    acceptable.Add(null);
                }

                return acceptable.Contains(actual)
                    ? null
                    : $"single={actual ?? "null"} expected=[{string.Join(",", acceptable.Select(a => a ?? "null"))}]";
            }

            default:
            {
                var items = ((IEnumerable)result!).Cast<object>().ToList();
                var projected = query.Returns == QueryReturnShape.Projection;
                var keys = items
                    .Select(item =>
                        projected ? shape.Unwrap(Property(item, "Id")) : shape.KeyOf(item)
                    )
                    .ToList();
                var actual = keys.Select(KeyText).ToList();
                var missing = definite.Where(k => !actual.Contains(k)).ToList();
                var extra = actual.Where(k => !possible.Contains(k)).ToList();
                var problems = new List<string>();

                if (missing.Count > 0)
                {
                    problems.Add($"missing=[{string.Join(",", missing)}]");
                }

                if (extra.Count > 0)
                {
                    problems.Add($"extra=[{string.Join(",", extra)}]");
                }

                if (actual.Count != actual.Distinct().Count())
                {
                    problems.Add("duplicates");
                }

                var byKey = verdicts.ToDictionary(v => KeyText(v.Key), v => v.Row);

                // 並べるクエリは、返した行を同じ並びで並べ直した順と比べる（返した行の欠け・余りは上で見ている）
                if (query.IsOrdered && actual.All(byKey.ContainsKey))
                {
                    var sorted = actual
                        .Select(key => byKey[key])
                        .Order(ordering)
                        .Select(row => KeyText(row[shape.KeyColumn]))
                        .ToList();

                    if (!sorted.SequenceEqual(actual))
                    {
                        problems.Add(
                            $"order=[{string.Join(",", actual)}] expected=[{string.Join(",", sorted)}]"
                        );
                    }
                }

                if (projected)
                {
                    foreach (var item in items)
                    {
                        var id = KeyText(shape.Unwrap(Property(item, "Id")));
                        var picked = shape.Unwrap(Property(item, "Picked"));

                        if (
                            byKey.TryGetValue(id, out var row)
                            && !DslFuzz.SameStoredValue(picked, row[query.ProjectedColumn!.Name])
                        )
                        {
                            problems.Add(
                                $"picked[{id}]={picked} stored={row[query.ProjectedColumn!.Name]}"
                            );
                        }
                    }
                }

                return problems.Count == 0 ? null : string.Join(" ", problems);
            }
        }
    }

    /// <summary>
    /// クエリの並び（追加の列を宣言の順・向きで、最後に主キー）での 2 行の順序（NULL は <paramref name="nulls"/> の位置・数値は数値・
    /// 日時は日時・主キーの文字列は序数）
    /// </summary>
    private static int CompareRows(
        DslQuery query,
        EntityShape shape,
        DslNullOrder nulls,
        Dictionary<string, object?> left,
        Dictionary<string, object?> right
    )
    {
        foreach (var ordering in query.Orderings)
        {
            var order = DslFuzz.CompareForOrdering(
                left[ordering.Column.Name],
                right[ordering.Column.Name],
                nulls
            );

            if (order != 0)
            {
                return ordering.Descending ? -order : order;
            }
        }

        var key = DslFuzz.CompareForOrdering(left[shape.KeyColumn], right[shape.KeyColumn], nulls);

        return query.OrderDescending ? -key : key;
    }

    private static string KeyText(object? key) =>
        Convert.ToString(key, CultureInfo.InvariantCulture) ?? "null";

    private static object? Property(object item, string name) =>
        item.GetType().GetProperty(name)!.GetValue(item);

    private static Exception Unwrap(Exception ex) =>
        ex is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : ex;

    /// <summary>インターフェイスの継承をたどってメソッドを探す</summary>
    private static MethodInfo Method(Type repositoryType, string name) =>
        new[] { repositoryType }
            .Concat(repositoryType.GetInterfaces())
            .SelectMany(type => type.GetMethods())
            .First(method => method.Name == name);

    /// <summary>メソッドを呼んで Task の結果を返す（名前で渡す引数のほかは省略値・取り消しトークン）</summary>
    private static async Task<object?> InvokeAsync(
        object target,
        MethodInfo method,
        Dictionary<string, object?> arguments,
        CancellationToken ct
    )
    {
        var values = method
            .GetParameters()
            .Select(parameter =>
                arguments.TryGetValue(parameter.Name!, out var value)
                    ? value is DslParam param
                        ? ConvertArgument(param, parameter.ParameterType)
                        : value
                    : parameter.ParameterType == typeof(CancellationToken)
                        ? ct
                        : parameter.HasDefaultValue
                            ? parameter.DefaultValue
                            : throw new InvalidOperationException(
                                $"{method.Name} の引数 {parameter.Name} を埋められない"
                            )
            )
            .ToArray();
        var task = (Task)method.Invoke(target, values)!;
        await task;

        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    /// <summary>パラメータの値を引数の型へ（リストは List&lt;要素&gt;・値オブジェクトは Create）</summary>
    private static object? ConvertArgument(DslParam param, Type type)
    {
        if (!param.IsList)
        {
            return ValueConversion.To(param.Values[0], type);
        }

        var element = type.IsGenericType
            ? type.GetGenericArguments()[0]
            : throw new InvalidOperationException($"リストの引数の型 {type} の要素型が分からない");
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;

        foreach (var value in param.Values)
        {
            list.Add(ValueConversion.To(value, element));
        }

        return list;
    }

    /// <summary>生成されたエンティティの列（[Column] の名前）と値の読み書き</summary>
    private sealed class EntityShape
    {
        private readonly Type _type;
        private readonly Dictionary<string, PropertyInfo> _properties;

        public EntityShape(Type type, DslFuzzUnit unit)
        {
            _type = type;
            _properties = type.GetProperties()
                .Where(p => p.GetCustomAttribute<ColumnAttribute>() is not null)
                .ToDictionary(
                    p => p.GetCustomAttribute<ColumnAttribute>()!.Name!,
                    StringComparer.Ordinal
                );
            KeyColumn = unit.Key.Name;
        }

        public string KeyColumn { get; }

        public object Create(IReadOnlyDictionary<string, object?> row)
        {
            var entity = Activator.CreateInstance(_type)!;

            foreach (var (column, value) in row)
            {
                var property = _properties[column];
                property.SetValue(entity, ValueConversion.To(value, property.PropertyType));
            }

            return entity;
        }

        public Dictionary<string, object?> Read(object entity) =>
            _properties.ToDictionary(
                pair => pair.Key,
                pair => Unwrap(pair.Value.GetValue(entity)),
                StringComparer.Ordinal
            );

        public object? KeyOf(object entity) => Unwrap(_properties[KeyColumn].GetValue(entity));

        public object? Unwrap(object? value) => ValueConversion.Unwrap(value);
    }
}

/// <summary>値オブジェクトの組み立て・開封と、数値の型の変換</summary>
internal static class ValueConversion
{
    /// <summary>値を型へ（値オブジェクトは静的な Create・数値は Convert.ChangeType）</summary>
    public static object? To(object? value, Type type)
    {
        if (value is null)
        {
            return null;
        }

        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target.IsInstanceOfType(value))
        {
            return value;
        }

        if (IsValueObject(target))
        {
            var create = target
                .GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy
                )
                .First(m =>
                    m.Name == "Create"
                    && m.GetParameters().Length == 1
                    && target.IsAssignableFrom(m.ReturnType)
                );

            return create.Invoke(null, [To(value, create.GetParameters()[0].ParameterType)]);
        }

        if (target == typeof(Guid) && value is string text)
        {
            return Guid.Parse(text);
        }

        if (target == typeof(string))
        {
            return System.Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    /// <summary>値オブジェクトなら中の値を返す</summary>
    public static object? Unwrap(object? value) =>
        value is not null && IsValueObject(value.GetType())
            ? value.GetType().GetProperty("Value")!.GetValue(value)
            : value;

    private static bool IsValueObject(Type type) =>
        type.GetInterfaces().Any(i => i.Name == "IValueObjectCore");
}
