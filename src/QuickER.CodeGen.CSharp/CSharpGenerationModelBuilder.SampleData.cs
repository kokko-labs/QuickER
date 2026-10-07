using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// インメモリ Repository の決定的シーダー（<c>InMemorySampleData</c>）が各列へ代入するサンプル値式と、
/// エンティティごとのシード行数を組み立てる部分。
/// </summary>
/// <remarks>
/// <para>
/// シーダーは各エンティティを最大 <see cref="MaxSampleRows"/> 行（ループ変数 <c>index</c> = 1..n）投入する。
/// 値は型だけでなく<b>列の役割</b>から作る。役割は 3 つ:
/// </para>
/// <list type="number">
///   <item>外部キーの子側の列（リレーションの列ペアの子側）: 参照先の列（参照の連鎖は根まで辿る）の式を、
///     子の型で同じ <c>index</c> で作る＝子の i 行目は親の i 行目を指す。文字列の外部キーや、一意制約の親列を
///     参照する外部キーでも、親の値と一致する。</item>
///   <item>主キー・一意制約の構成列: 行ごとに違う値を作る（数値は <c>index</c>・文字列は末尾の <c>index</c> を
///     残して長さに収める）。</item>
///   <item>それ以外の列: 型ごとの決定的な値。</item>
/// </list>
/// <para>
/// 行ごとに違う値を 3 つ作れない型がある（NOT NULL の <c>bool</c> は 2 種類・既定値へ倒す未知の型と配列は 1 種類）。
/// そうした列だけでできた主キー・一意制約を持つエンティティはシード行数を減らし、それを参照する子も
/// 親に実在する行だけを指すよう行数を減らす（不動点で決める）。重複した値をストアへ入れると、
/// 以後その行の更新・保存が一意制約違反で必ず失敗するため。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>1 エンティティあたりのシード行数の上限（生成物の <c>InMemorySampleData.RowsPerEntity</c> と同じ値）</summary>
    internal const int MaxSampleRows = 3;

    /// <summary>外部キーの子側の列 ID → 参照の連鎖の根の列（サンプル値を共有する列）</summary>
    private Dictionary<Guid, Column> _sampleReferences = [];

    /// <summary>行ごとに違う値を要する列（主キー・一意制約の構成列）の ID</summary>
    private HashSet<Guid> _sampleDistinctColumns = [];

    /// <summary>エンティティ ID → シード行数</summary>
    private Dictionary<Guid, int> _sampleRowCounts = [];

    /// <summary>図からシード値の組み立てに使う役割（参照先・行ごとに違う値を要する列・行数）を決める</summary>
    private void PrepareSampleData(ErDiagram diagram)
    {
        var columnsById = new Dictionary<Guid, Column>();
        var entityOfColumn = new Dictionary<Guid, Entity>();

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                columnsById[column.Id] = column;
                entityOfColumn[column.Id] = entity;
            }
        }

        // 子の列 → 直接の参照先の列（同じ子の列が複数のリレーションに現れるときは図の宣言順で最初のもの）
        var directReferences = new Dictionary<Guid, Column>();

        foreach (var relationship in diagram.Relationships)
        {
            foreach (var pair in relationship.ColumnPairs)
            {
                if (
                    columnsById.TryGetValue(pair.SourceColumnId, out var parent)
                    && columnsById.ContainsKey(pair.TargetColumnId)
                )
                {
                    directReferences.TryAdd(pair.TargetColumnId, parent);
                }
            }
        }

        // 参照の連鎖を根まで辿る（循環は最初に再訪した列で止める＝循環に属する列は同じ式を共有する）
        _sampleReferences = [];

        foreach (var (childId, parent) in directReferences)
        {
            var visited = new HashSet<Guid> { childId };
            var root = parent;

            while (visited.Add(root.Id) && directReferences.TryGetValue(root.Id, out var next))
            {
                root = next;
            }

            _sampleReferences[childId] = root;
        }

        _sampleDistinctColumns = [];

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns.Where(column => column.IsPrimaryKey))
            {
                _sampleDistinctColumns.Add(column.Id);
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                _sampleDistinctColumns.UnionWith(constraint.ColumnIds);
            }
        }

        _sampleRowCounts = ResolveSampleRowCounts(diagram, columnsById, entityOfColumn);
    }

    /// <summary>エンティティごとのシード行数を不動点で決める</summary>
    /// <remarks>
    /// 出発点は「主キー・一意制約がそれぞれ何行ぶん違う値の組を作れるか」の最小。そこから、外部キーの子の列が
    /// 親に実在する値（参照先の列が NULL でない行）だけを指すよう子の行数を減らす。行数は減る一方なので
    /// 必ず止まり、自己参照（i 行目が自分自身を指す）も相互参照も同じ規則で収束する。
    /// </remarks>
    private Dictionary<Guid, int> ResolveSampleRowCounts(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, Column> columnsById,
        IReadOnlyDictionary<Guid, Entity> entityOfColumn
    )
    {
        var counts = diagram.Entities.ToDictionary(
            entity => entity.Id,
            entity => Math.Min(MaxSampleRows, ConstraintCapacity(entity, columnsById))
        );

        bool changed;

        do
        {
            changed = false;

            foreach (var entity in diagram.Entities)
            {
                foreach (var column in entity.Columns)
                {
                    if (!_sampleReferences.TryGetValue(column.Id, out var root))
                    {
                        continue;
                    }

                    var parentRows = counts[entityOfColumn[root.Id].Id];

                    // 参照先の列が NULL 許容なら、その列は 3 行目で NULL になる（実在する値は 2 行目まで）
                    var referencedRows = root.IsNullable ? Math.Min(parentRows, 2) : parentRows;

                    // NULL 許容の子の列も 3 行目は NULL なので、実在を要するのは 2 行目まで
                    var limit =
                        column.IsNullable && referencedRows >= 2 ? MaxSampleRows : referencedRows;

                    if (counts[entity.Id] > limit)
                    {
                        counts[entity.Id] = limit;
                        changed = true;
                    }
                }
            }
        } while (changed);

        return counts;
    }

    /// <summary>エンティティの主キーと各一意制約が、それぞれ何行ぶん違う値の組を作れるかの最小</summary>
    /// <remarks>組は構成列のどれか 1 つが違えば違うので、組の行数は構成列ごとの行数の最大になる</remarks>
    private int ConstraintCapacity(Entity entity, IReadOnlyDictionary<Guid, Column> columnsById)
    {
        var capacity = MaxSampleRows;
        var keyColumns = entity.Columns.Where(column => column.IsPrimaryKey).ToList();

        if (keyColumns.Count > 0)
        {
            capacity = Math.Min(capacity, keyColumns.Max(DistinctSampleCapacity));
        }

        foreach (var constraint in entity.UniqueConstraints)
        {
            var members = constraint
                .ColumnIds.Where(columnsById.ContainsKey)
                .Select(id => columnsById[id])
                .ToList();

            if (members.Count > 0)
            {
                capacity = Math.Min(capacity, members.Max(DistinctSampleCapacity));
            }
        }

        return Math.Max(capacity, 1);
    }

    /// <summary>行ごとに違う値を要する列が、何行ぶん違う値を作れるか（NULL の行は比較から外れるので違う値に数える）</summary>
    private int DistinctSampleCapacity(Column column)
    {
        // 外部キーの子の列の値は親の行ごとに違う（子の行数は親の行数で別に抑える）
        if (_sampleReferences.ContainsKey(column.Id) || !_columnTypes.ContainsKey(column.Id))
        {
            return MaxSampleRows;
        }

        var valueObject = ResolveValueObject(column);
        var baseType = valueObject?.ValueTypeName ?? _columnTypes[column.Id].TypeName.TrimEnd('?');
        var nullRow = column.IsNullable ? 1 : 0;

        if (valueObject is { IsGuidKey: true })
        {
            return MaxSampleRows;
        }

        return baseType switch
        {
            "bool" => 2 + nullRow,
            _ when baseType.EndsWith("[]", StringComparison.Ordinal) && baseType != "byte[]" => 1,
            _ when IsKnownSampleType(baseType) => MaxSampleRows,
            _ => 1,
        };
    }

    /// <summary>
    /// インメモリ Repository の決定的シーダーで、指定カラムへ代入する値の C# 式を構築する。
    /// </summary>
    /// <remarks>
    /// サンプル値は列の宣言制約（string の MaxLength・decimal の precision/scale）を満たす形で組み立てる＝
    /// VO 有効時はシードが <c>Create</c> の検証を通るため、制約を見ない固定値は DI 登録（既定シード）を起動時例外にする。
    /// ただし利用者が書く検証（OnValidate・手書き VO の規則）は生成器には知りようがなく満たせないため、
    /// VO の Create はシーダー内のガード（CreateSampleValue）経由で呼び、拒否されたときに
    /// 「どのプロパティへ何を入れようとしたか」と対処（seedSampleData: false）が例外メッセージに載るようにする。
    /// NULL 可列は 3 行目を null にする。
    /// </remarks>
    private string BuildSampleValueExpression(
        Column column,
        CSharpTypeInfo typeInfo,
        CSharpValueObjectModel? valueObject,
        string? entityClassName
    )
    {
        // 外部キーの子の列は、参照の連鎖の根の列と同じ式で値を作る（子の i 行目が親の i 行目を指す）
        var source = _sampleReferences.TryGetValue(column.Id, out var root) ? root : column;
        var sourceType = _columnTypes.TryGetValue(source.Id, out var rootType)
            ? rootType
            : typeInfo;

        // 値オブジェクトは内包値を作ってから Create で包む（GuidKey は無引数生成の代わりに決定的 GUID を渡す）
        if (valueObject is not null)
        {
            var innerExpr = valueObject.IsGuidKey
                ? SampleGuidLiteral() + ".ToString()"
                : BuildSampleScalarExpression(
                    source,
                    valueObject.ValueTypeName,
                    sourceType.MaxLength,
                    sourceType.Precision,
                    sourceType.Scale
                );

            // 失敗文脈（"{クラス}.{プロパティ}"）を添えたガード経由で Create する
            var propertyName = _nameConverter.ToPropertyName(column.Name);
            var target = entityClassName is null
                ? propertyName
                : $"{entityClassName}.{propertyName}";
            var created =
                $"CreateSampleValue<{valueObject.ClassName}, {valueObject.ValueTypeName}>(\"{target}\", {innerExpr})";

            // NULL 可 VO 列は 3 件中 1 件を null にする（確定値型は VoClass?）
            return column.IsNullable ? $"index == 3 ? null : {created}" : created;
        }

        var baseType = typeInfo.TypeName.TrimEnd('?');
        var scalar = BuildSampleScalarExpression(
            source,
            baseType,
            sourceType.MaxLength,
            sourceType.Precision,
            sourceType.Scale
        );

        // NULL 可列は 3 件中 1 件（index == 3）を null にする（プロパティ型は T?・target-typed conditional で解決）
        return column.IsNullable ? $"index == 3 ? null : {scalar}" : scalar;
    }

    /// <summary>シーダーが行ごとに違う値を作れる型か（<see cref="BuildSampleScalarExpression"/> が専用の式を持つ型）</summary>
    private static bool IsKnownSampleType(string baseType) =>
        baseType
            is "int"
                or "long"
                or "short"
                or "byte"
                or "sbyte"
                or "ushort"
                or "uint"
                or "ulong"
                or "decimal"
                or "double"
                or "float"
                or "Guid"
                or "DateTime"
                or "DateTimeOffset"
                or "TimeSpan"
                or "byte[]"
                or "System.Net.IPAddress"
                or "System.Net.IPNetwork"
                or "string";

    /// <summary>型・列の役割から、非 NULL のスカラーサンプル値式を構築する</summary>
    /// <param name="column">値の源になる列（外部キーの子の列なら参照の連鎖の根の列）</param>
    /// <param name="baseType">代入先のプロパティの型（外部キーの子の列なら子の型）</param>
    /// <param name="maxLength">源の列の最大長</param>
    /// <param name="precision">源の列の精度</param>
    /// <param name="scale">源の列のスケール</param>
    private string BuildSampleScalarExpression(
        Column column,
        string baseType,
        int? maxLength,
        int? precision,
        int? scale
    )
    {
        // 主キー・外部キー・一意制約の構成列は決定的な行番号（index）で表現し、行ごとに違う値と親子の整合を保つ
        var isKey =
            column.IsPrimaryKey
            || column.IsForeignKey
            || _sampleDistinctColumns.Contains(column.Id);

        switch (baseType)
        {
            case "int":
                return isKey ? "index" : "index * 10";

            case "long":
                return isKey ? "(long)index" : "index * 10L";

            case "short":
                return isKey ? "(short)index" : "(short)(index * 10)";

            case "byte":
                return "(byte)index";

            case "sbyte":
                return isKey ? "(sbyte)index" : "(sbyte)(index * 10)";

            case "ushort":
                return isKey ? "(ushort)index" : "(ushort)(index * 10)";

            case "uint":
                return isKey ? "(uint)index" : "(uint)(index * 10)";

            case "ulong":
                return isKey ? "(ulong)index" : "(ulong)(index * 10)";

            case "decimal":
                return BuildSampleDecimalExpression(precision, scale);

            case "double":
                return "index * 100.5d";

            case "float":
                return "index * 100.5f";

            case "bool":
                return "index % 2 == 1";

            case "Guid":
                return SampleGuidLiteral();

            case "DateTime":
                return "new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index)";

            case "DateTimeOffset":
                return "new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index)";

            case "TimeSpan":
                return "TimeSpan.FromHours(index)";

            case "byte[]":
                return "new byte[] { (byte)index, (byte)(index + 1), (byte)(index + 2) }";

            // PostgreSQL の inet / cidr（行ごとに違う決定的なアドレス・ネットワーク）
            case "System.Net.IPAddress":
                return "new System.Net.IPAddress(new byte[] { 10, 0, 0, (byte)index })";

            case "System.Net.IPNetwork":
                return "new System.Net.IPNetwork(new System.Net.IPAddress(new byte[] { 10, (byte)index, 0, 0 }), 16)";

            case "string":
                return BuildSampleStringExpression(column, maxLength);

            default:
                // 配列列は空配列で始める（default は null で、NOT NULL の列へ入れられない）
                if (baseType.EndsWith("[]", StringComparison.Ordinal))
                {
                    return $"Array.Empty<{baseType[..^2]}>()";
                }

                // enum など未知の値型は既定値へフォールバックする（決定的・コンパイル可能）
                return $"default({baseType})";
        }
    }

    /// <summary>decimal 列のサンプル値式を構築する（precision / scale が既知なら、その桁で表現できる決定的な値にする）</summary>
    /// <remarks>
    /// decimal は値だけでなく桁の持ち方（<c>decimal.Scale</c>）も保持し、整数 <c>index</c> を掛けても
    /// リテラル由来の Scale は減らない。生成される検証（ValidateDecimal）は precision があるときだけ出るため、
    /// 桁を合わせる必要があるのも既知のときだけで、未知（自由記述の decimal 等）は従来の固定値のままにする。
    /// 組み立ての不変条件は「小数部の桁数＝宣言 scale（宣言の全桁を実際に使う）・index=1..3 を掛けても
    /// 整数部が precision - scale 桁に収まる（整数部は最大 3 桁に抑え、繰り上がり分も 1 桁目に収まる 5 を使う）」。
    /// どの形も <c>index</c> の倍数なので、行ごとに違う値になる。
    /// </remarks>
    private static string BuildSampleDecimalExpression(int? precision, int? scale)
    {
        if (precision is not int p || p <= 0)
        {
            return "index * 100.50m";
        }

        // ValidateDecimal と同じ解釈（scale 省略は 0）。範囲外の宣言は防御的に 0..precision へ丸める
        var s = Math.Clamp(scale ?? 0, 0, p);
        var integralDigits = p - s;

        // 整数部が使えない列（precision == scale）は最小桁の刻み＝index * 10^-s（index=3 でも 1 未満）
        if (integralDigits == 0)
        {
            return $"index * 0.{new string('0', s - 1)}1m";
        }

        var integral = "1" + new string('0', Math.Min(integralDigits, 3) - 1);
        return s == 0 ? $"index * {integral}m" : $"index * {integral}.{new string('0', s - 1)}5m";
    }

    /// <summary>string 列のサンプル値式を構築する（主キーは "{列名の大文字}-00n"・それ以外は "{列名} n"）</summary>
    /// <remarks>
    /// 最大長に収まらないときは、行を区別する末尾の <c>index</c>（1 桁）を残して、その前の列名の側を切り詰める
    /// （区切りも落とす）。左から切り詰めると <c>index</c> が消え、3 行が同じ値になる。
    /// 切り詰めは生成時に決まるので、生成物には収まる形の補間文字列だけが出る。
    /// </remarks>
    private static string BuildSampleStringExpression(Column column, int? maxLength)
    {
        var (label, separator) = column.IsPrimaryKey
            ? (column.Name.ToUpperInvariant(), "-00")
            : (column.Name, " ");

        // index は 1..3 の 1 桁
        if (maxLength is int limit && limit > 0 && label.Length + separator.Length + 1 > limit)
        {
            var kept = TruncateWithoutSplittingSurrogate(label, limit - 1);
            return $"$\"{EscapeForCSharpInterpolatedString(kept)}{{index}}\"";
        }

        return $"$\"{EscapeForCSharpInterpolatedString(label)}{separator}{{index}}\"";
    }

    /// <summary>文字列を指定の長さ（UTF-16 の単位）以下へ切り詰める（サロゲートペアの途中では切らない）</summary>
    private static string TruncateWithoutSplittingSurrogate(string text, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= length)
        {
            return text;
        }

        var cut = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
        return text[..cut];
    }

    /// <summary>決定的な GUID リテラル式を返す（index を末尾に埋め込み 3 件を区別する）</summary>
    private static string SampleGuidLiteral() =>
        "new Guid($\"00000000-0000-0000-0000-00000000000{index}\")";
}
