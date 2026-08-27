using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 値オブジェクト（Value Object）の定義レジストリを構築する部分。
/// </summary>
/// <remarks>
/// <para>
/// 全テーブルの列を「正規化したカラム名」でグローバルにグルーピングし、列名ごとに 1 つの VO 型へ集約する。
/// PK と同名の FK が同一 VO 型を共有することで型安全を得る。同名なのに型・長さ・精度などが食い違う場合は
/// 競合として Warning 診断を出し、PK 定義優先（無ければ最大定義）で生成する。
/// </para>
/// <para>
/// <b>リレーションの子側（dependent）列は、親側（principal）列の VO 型を共有する</b>（列名が食い違っていても
/// 同じ識別子は同じ値型になる）。統一しないと、FK 列名 ≠ 参照先 PK 列名の図で「参照先のキーを FK 列へ代入できない」
/// 型の割れが生じ、EF Core ではモデル検証が <c>DbContext</c> ごと落ちる（FK プロパティの CLR 型は主キーの型と
/// 互換である必要があり、値コンバータは判定に関与しない＝Fluent の書き方では直せない）。
/// 統一はこのグルーピング（列 → VO 型の解決）1 箇所で行うため、Entity・EditModel・Mapper・EF Core Fluent・
/// DSL クエリ・一意性チェック・リモート直列化のすべてが自動で追従する。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>1 つの値オブジェクトへ集約される列のメンバー（所属テーブル・列・解決済み C# 型）</summary>
    private readonly record struct ValueObjectMember(
        Entity Entity,
        Column Column,
        CSharpTypeInfo TypeInfo
    );

    /// <summary>列とその所属テーブル（診断文言・キー導出で「どのテーブルの列か」が要るため対にして持つ）</summary>
    private readonly record struct ValueObjectColumnOwner(Entity Entity, Column Column);

    /// <summary>ER 図全体から値オブジェクト定義を構築する（GenerateValueObjects が OFF のときは空）</summary>
    private IReadOnlyDictionary<string, CSharpValueObjectModel> BuildValueObjects(
        ErDiagram diagram,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        _valueObjectKeys = new Dictionary<Guid, string>();

        if (!options.GenerateValueObjects)
        {
            return new Dictionary<string, CSharpValueObjectModel>();
        }

        // 列 → VO キーの対応（FK の子側は親側のキーへ寄せる）。以降のグルーピングはこのキーだけを見る
        _valueObjectKeys = BuildValueObjectKeys(diagram, diagnostics);

        // VO キーでグローバルにグルーピングする（統一で使われなくなった列名のグループは現れない＝VO も生成されない）
        var groups = new Dictionary<string, List<ValueObjectMember>>(StringComparer.Ordinal);
        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                var key = ResolveValueObjectKey(column);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<ValueObjectMember>();
                    groups[key] = list;
                }

                list.Add(new ValueObjectMember(entity, column, _columnTypes[column.Id]));
            }
        }

        var result = new Dictionary<string, CSharpValueObjectModel>(StringComparer.Ordinal);
        foreach (var (key, members) in groups)
        {
            result[key] = BuildValueObjectModel(key, members, options, diagnostics);
        }

        return result;
    }

    /// <summary>
    /// 列 ID → その列が使う VO キー（＝グループキー）の対応を作る。
    /// リレーションの子側（dependent）列は親側（principal）列のキーを共有する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 親子関係は <see cref="Relationship.ColumnPairs"/>（親列 × 子列）が唯一の正本で、複合外部キーも列ごとに
    /// 独立して寄せる。<b>下地の C# 型が親子で食い違う列ペアは統一しない</b>（スキーマ自体が歪んでいるケースで、
    /// 黙って型を差し替えるより現状維持が安全）。
    /// </para>
    /// <para>
    /// FK の FK（チェーン）は不動点まで辿る。相互 FK などの循環は「循環に属する列の親辺をすべて落とす」ことで
    /// 先に断ち切ってから解決するため、解決結果は走査順に依らず決定的で、循環に入った列は自分のキーのままになる。
    /// </para>
    /// </remarks>
    private Dictionary<Guid, string> BuildValueObjectKeys(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var owners = new Dictionary<Guid, ValueObjectColumnOwner>();
        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                owners[column.Id] = new ValueObjectColumnOwner(entity, column);
            }
        }

        var parents = BuildForeignKeyParentEdges(diagram, owners);
        RemoveCyclicEdges(parents);

        var representatives = new Dictionary<Guid, Guid>();
        var keys = new Dictionary<Guid, string>();
        var unifiedLines = new List<string>();

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                var representative = ResolveValueObjectRepresentative(
                    column.Id,
                    parents,
                    owners,
                    representatives,
                    diagnostics
                );
                var target = owners[representative];
                var key = _nameConverter.ToColumnKey(target.Column.Name);
                keys[column.Id] = key;

                // 自分の列名から導いたキーと違うときだけ「参照先の型を共有した」と言える（列名一致の統一は無通知）
                if (
                    !string.Equals(
                        key,
                        _nameConverter.ToColumnKey(column.Name),
                        StringComparison.Ordinal
                    )
                )
                {
                    unifiedLines.Add(
                        $"  {entity.TableName}.{column.Name} → "
                            + $"{_nameConverter.ToValueObjectClassName(target.Column.Name)}"
                            + $"（{target.Entity.TableName}.{target.Column.Name}）"
                    );
                }
            }
        }

        // 生成される型名が列名由来から参照先由来へ変わるため、どの列が寄ったかを Info 診断で名指しする
        if (unifiedLines.Count > 0)
        {
            diagnostics.Add(
                GenerationDiagnostic.Info(
                    string.Format(
                        Strings.CodeGen_Info_ValueObjectForeignKeyUnified,
                        Environment.NewLine + string.Join(Environment.NewLine, unifiedLines)
                    )
                )
            );
        }

        return keys;
    }

    /// <summary>子列 ID → 親列 ID 群（宣言順・重複排除）の対応を作る。型が食い違うペアは辺を張らない</summary>
    private Dictionary<Guid, List<Guid>> BuildForeignKeyParentEdges(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, ValueObjectColumnOwner> owners
    )
    {
        var edges = new Dictionary<Guid, List<Guid>>();

        foreach (var relationship in diagram.Relationships)
        {
            // 多対多は外部キー列を持たない（列ペアは空）
            if (relationship.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            foreach (var pair in relationship.ColumnPairs)
            {
                var parentId = pair.SourceColumnId;
                var childId = pair.TargetColumnId;

                if (parentId == childId)
                {
                    continue;
                }

                if (!owners.ContainsKey(parentId) || !owners.ContainsKey(childId))
                {
                    continue;
                }

                if (
                    !_columnTypes.TryGetValue(parentId, out var parentType)
                    || !_columnTypes.TryGetValue(childId, out var childType)
                )
                {
                    continue;
                }

                // 下地の C# 型が食い違うペアは統一対象外（VO の内包値型が変わってしまうため）
                if (
                    !string.Equals(
                        parentType.TypeName,
                        childType.TypeName,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                if (!edges.TryGetValue(childId, out var list))
                {
                    list = new List<Guid>();
                    edges[childId] = list;
                }

                if (!list.Contains(parentId))
                {
                    list.Add(parentId);
                }
            }
        }

        return edges;
    }

    /// <summary>循環に属する列の親辺をすべて落とし、残りを非循環にする（解決を走査順に依らせないため）</summary>
    private static void RemoveCyclicEdges(Dictionary<Guid, List<Guid>> parents)
    {
        var cyclic = parents.Keys.Where(id => ReachesItself(id, parents)).ToList();

        foreach (var id in cyclic)
        {
            parents.Remove(id);
        }
    }

    /// <summary>親辺を辿って自分自身へ戻れるか（＝循環に属するか）を判定する</summary>
    private static bool ReachesItself(Guid start, IReadOnlyDictionary<Guid, List<Guid>> parents)
    {
        var visited = new HashSet<Guid>();
        var pending = new Queue<Guid>(parents[start]);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            if (current == start)
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            if (parents.TryGetValue(current, out var next))
            {
                foreach (var parent in next)
                {
                    pending.Enqueue(parent);
                }
            }
        }

        return false;
    }

    /// <summary>列の VO 型を代表する列（不動点）を解決する。親を複数持ち型が食い違う場合は診断エラー</summary>
    private Guid ResolveValueObjectRepresentative(
        Guid columnId,
        IReadOnlyDictionary<Guid, List<Guid>> parents,
        IReadOnlyDictionary<Guid, ValueObjectColumnOwner> owners,
        Dictionary<Guid, Guid> memo,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (memo.TryGetValue(columnId, out var cached))
        {
            return cached;
        }

        if (!parents.TryGetValue(columnId, out var candidates) || candidates.Count == 0)
        {
            memo[columnId] = columnId;
            return columnId;
        }

        var resolved = candidates
            .Select(parentId =>
                ResolveValueObjectRepresentative(parentId, parents, owners, memo, diagnostics)
            )
            .ToList();
        // 解決先が同じ VO 型なら（別テーブルの同名列でも）1 つに定まるため、判定は型名で行う
        var distinctTypes = resolved
            .Select(id => _nameConverter.ToValueObjectClassName(owners[id].Column.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Guid result;

        if (distinctTypes.Count > 1)
        {
            // 同じ子列が「別々の VO 型へ解決する親」を複数参照している＝どちらの型を名乗るべきか決められない
            var owner = owners[columnId];
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_ValueObjectForeignKeyTypeConflict,
                        $"{owner.Entity.TableName}.{owner.Column.Name}",
                        string.Join(", ", distinctTypes)
                    )
                )
            );
            result = columnId;
        }
        else
        {
            result = resolved[0];
        }

        memo[columnId] = result;
        return result;
    }

    /// <summary>列に対応する VO キー（グループキー）を返す。統一マップに無い列は自分の列名から導く</summary>
    private string ResolveValueObjectKey(Column column) =>
        _valueObjectKeys.TryGetValue(column.Id, out var key)
            ? key
            : _nameConverter.ToColumnKey(column.Name);

    /// <summary>
    /// 1 グループ（同じ VO キーへ寄った列の集合＝同名列と、そこへ統一された FK 列）から 1 つの VO 生成モデルを
    /// 構築する。競合は警告し PK 優先/最大定義で解決する
    /// </summary>
    private CSharpValueObjectModel BuildValueObjectModel(
        string key,
        List<ValueObjectMember> members,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // 型名・型定義の正は「このグループのキーを自分の列名から名乗る列」（＝統一で寄ってきた子列ではない側）から選ぶ。
        // 寄ってきた列を正にすると、生成される VO の名前が参照先ではなく FK 列名になってしまう
        var natives = members
            .Where(member =>
                string.Equals(
                    _nameConverter.ToColumnKey(member.Column.Name),
                    key,
                    StringComparison.Ordinal
                )
            )
            .ToList();
        var candidates = natives.Count > 0 ? natives : members;

        // PK があれば PK の定義を正、無ければ「最も広い定義」を正とする
        var primaryKeyMember = candidates.FirstOrDefault(member => member.Column.IsPrimaryKey);
        var authoritative = primaryKeyMember.Column is not null
            ? primaryKeyMember
            : candidates.OrderByDescending(WidthScore).First();

        var className = _nameConverter.ToValueObjectClassName(authoritative.Column.Name);
        var isGuidKey =
            options.UseGuidKeyForStringPrimaryKey
            && primaryKeyMember.Column is not null
            && primaryKeyMember.TypeInfo.TypeName == "string";

        var valueType = isGuidKey ? "string" : authoritative.TypeInfo.TypeName;
        var maxLength = isGuidKey ? null : authoritative.TypeInfo.MaxLength;
        var precision = isGuidKey ? null : authoritative.TypeInfo.Precision;
        var scale = isGuidKey ? null : authoritative.TypeInfo.Scale;

        // 同一グループの定義が食い違う場合は競合として警告（NULL 可否は競合に含めない）
        var signatures = members.Select(Signature).Distinct().ToList();
        if (signatures.Count > 1)
        {
            var locations = string.Join(
                "、",
                members.Select(member =>
                    $"{member.Entity.TableName}.{member.Column.Name} ({member.Column.DataType})"
                )
            );
            diagnostics.Add(
                GenerationDiagnostic.Warning(
                    string.Format(
                        Strings.CodeGen_Warning_ValueObjectDefinitionMismatch,
                        className,
                        locations
                    )
                )
            );
        }

        return new CSharpValueObjectModel
        {
            ClassName = className,
            ValueTypeName = valueType,
            BaseDeclaration = BuildValueObjectBaseDeclaration(className, valueType, isGuidKey),
            InterfaceDeclaration = $"IValueObject<{className}, {valueType}>",
            IsGuidKey = isGuidKey,
            ColumnName = authoritative.Column.Name,
            DescriptionXmlDoc = EscapeForXmlDocSummary(authoritative.Column.Description),
            // 表示名解決へ渡すメンバー名（例 "Name"）と代表列の説明。説明が無指定なら null（メンバー名へフォールバックする）
            DisplayNameMemberName = _nameConverter.ToPropertyName(authoritative.Column.Name),
            DisplayNameDescription = string.IsNullOrWhiteSpace(authoritative.Column.Description)
                ? null
                : EscapeForCSharpString(authoritative.Column.Description),
            MaxLength = maxLength,
            Precision = precision,
            Scale = scale,
        };
    }

    /// <summary>列から対応する値オブジェクトを引く（VO 化対象外なら null）。<b>列 → VO 型の解決はここが単一の入口</b></summary>
    /// <remarks>
    /// リレーションの子側の列は親側の VO へ寄っているため、引き当ては列名ではなく統一済みの VO キーで行う。
    /// </remarks>
    private CSharpValueObjectModel? ResolveValueObject(Column column) =>
        _valueObjects.TryGetValue(ResolveValueObjectKey(column), out var model) ? model : null;

    /// <summary>VO 化された列の EditModel プロパティ生成モデルを構築する（確定値は常に VO?、バインド setter は TryCreate で検証）</summary>
    private CSharpEditModelPropertyModel BuildValueObjectEditModelProperty(
        Column column,
        CSharpValueObjectModel valueObject
    )
    {
        var underlying = valueObject.ValueTypeName; // TValue（素の型）
        var isBinary = underlying == "byte[]";
        var isString = underlying == "string";
        var needsParse = !isBinary && !isString; // 数値・日時・bool・Guid 等は文字列から TryParse

        var propertyName = _nameConverter.ToPropertyName(column.Name);
        var bindingPropertyName = "Binding" + propertyName;

        return new CSharpEditModelPropertyModel
        {
            PropertyName = propertyName,
            ColumnName = column.Name,
            // VO 有効プロパティの表示名は VO の静的 DisplayName を参照するため、この説明は使われない（整合のため転記のみ）
            DisplayNameDescription = string.IsNullOrWhiteSpace(column.Description)
                ? null
                : EscapeForCSharpString(column.Description),
            DescriptionXmlDoc = EscapeForXmlDocSummary(column.Description),
            TypeName = valueObject.ClassName + "?", // 確定値は常に NULL 許容
            FieldName = ToFieldName(propertyName),
            BindingPropertyName = bindingPropertyName,
            BindingFieldName = ToFieldName(bindingPropertyName),
            NeedsParse = needsParse,
            ParseTypeName = needsParse ? underlying : string.Empty,
            FieldInitializer = string.Empty, // 参照型・nullable → null 既定
            BindingFieldInitializer = "string.Empty",
            IsNullable = true,
            IsReferenceType = true,
            IsBinary = isBinary,
            // 行バージョン列は DB が採番するため非 NULL でも入力必須にしない（新規行は未入力が正常）
            IsRequired = !column.IsNullable && !_columnTypes[column.Id].IsRowVersion,
            IsRowVersion = _columnTypes[column.Id].IsRowVersion,
            // 日付のみの列は内包値を短い日付書式で表示する（VO の ToString() は時刻部まで出るため）
            RevertBindingExpression = IsDateOnly(_columnTypes[column.Id])
                ? $"{propertyName}?.Value.ToString(\"d\") ?? string.Empty"
                : $"{propertyName}?.ToString() ?? string.Empty",
            IsValueObject = true,
            ValueObjectClassName = valueObject.ClassName,
        };
    }

    /// <summary>競合判定用の定義シグネチャ（型・長さ・精度・スケール。NULL 可否は含めない）</summary>
    private static (string Type, int? MaxLength, int? Precision, int? Scale) Signature(
        ValueObjectMember member
    ) =>
        (
            member.TypeInfo.TypeName,
            member.TypeInfo.MaxLength,
            member.TypeInfo.Precision,
            member.TypeInfo.Scale
        );

    /// <summary>「最も広い定義」を選ぶためのスコア（string は最大長、decimal は精度。無指定 = 無制限を最大とみなす）</summary>
    private static long WidthScore(ValueObjectMember member) =>
        member.TypeInfo.TypeName switch
        {
            "string" => member.TypeInfo.MaxLength ?? long.MaxValue,
            "decimal" => member.TypeInfo.Precision ?? long.MaxValue,
            _ => 0,
        };

    /// <summary>C# 値型から継承すべき基底クラス宣言（型引数込み）を決める</summary>
    private static string BuildValueObjectBaseDeclaration(
        string className,
        string valueType,
        bool isGuidKey
    )
    {
        if (isGuidKey)
        {
            return $"ValueObjectGuidKeyBase<{className}>";
        }

        return valueType switch
        {
            "byte"
            or "short"
            or "int"
            or "long"
            or "float"
            or "double"
            or "decimal"
            or "TimeSpan"
            or "DateTimeOffset" => $"ValueObjectOrderedBase<{className}, {valueType}>",
            "string" => $"ValueObjectStringBase<{className}>",
            "bool" => $"ValueObjectBooleanBase<{className}>",
            "DateTime" => $"ValueObjectDateTimeBase<{className}>",
            "byte[]" => $"ValueObjectBinaryBase<{className}>",
            // Guid など順序付けしない型は等価のみの基底
            _ => $"ValueObjectBase<{className}, {valueType}>",
        };
    }
}
