using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using QuickER.Model;

namespace QuickER.Services;

/// <summary>ER 図を Mermaid の <c>erDiagram</c> 記法へ変換するサービス</summary>
/// <remarks>
/// キー標識は Mermaid の単一キー方式（1 カラム 1 標識）に合わせ <c>PK &gt; FK &gt; UK</c> の優先度で 1 つに畳む。
/// 一意制約は単一列のものだけを <c>UK</c> として出力し、複合制約は出力しない
/// （<see cref="CollectSingleColumnUniqueMembers"/> 参照）。
/// </remarks>
public static partial class MermaidExporter
{
    /// <summary>制約名を持たないリレーションに書くラベル</summary>
    /// <remarks>
    /// Mermaid のリレーション行はラベルが構文上必須なので、名前の無いリレーションにも何か書く必要がある。
    /// 取込（<see cref="MermaidImporter"/>）はこの値を制約名として採らない
    /// </remarks>
    internal const string UnnamedRelationshipLabel = "relates";

    /// <summary>型が空の列に書く型トークン</summary>
    /// <remarks>
    /// 属性行は「型 名前 標識」の並びで型を省けず、空のまま書くと名前が型の位置へずれて取込が
    /// 名前を型・標識を名前と読む。取込（<see cref="MermaidImporter"/>）はこの値を空の型へ戻す
    /// </remarks>
    internal const string EmptyDataTypeToken = "_";

    /// <summary>ER 図定義から Mermaid 文字列を生成する</summary>
    public static string Build(ErDiagram diagram)
    {
        var builder = new StringBuilder();
        builder.AppendLine("erDiagram");

        var entityNames = BuildEntityNames(diagram.Entities);

        foreach (var entity in diagram.Entities)
        {
            builder.AppendLine($"    {entityNames[entity.Id]} {{");

            var uniqueColumnIds = CollectSingleColumnUniqueMembers(entity);
            var attributeNames = BuildAttributeNames(entity);

            foreach (var column in entity.Columns)
            {
                builder.AppendLine(
                    $"        {BuildColumnLine(column, uniqueColumnIds.Contains(column.Id), attributeNames[column.Id])}"
                );
            }

            builder.AppendLine("    }");
        }

        if (diagram.Entities.Count > 0 && diagram.Relationships.Count > 0)
        {
            builder.AppendLine();
        }

        foreach (var relationship in diagram.Relationships)
        {
            var line = BuildRelationshipLine(relationship, entityNames);
            if (line is not null)
            {
                builder.AppendLine($"    {line}");
            }
        }

        return builder.ToString();
    }

    /// <summary>Mermaid 文字列をファイルへ保存し、この形式では表現できず落ちた情報の種類を返す</summary>
    public static IReadOnlyList<ExportOmissionKind> SaveTo(ErDiagram diagram, string path)
    {
        File.WriteAllText(path, Build(diagram), Encoding.UTF8);

        return DetectOmissions(diagram);
    }

    /// <summary>この形式では表現できないために落ちる情報の種類を、図の実際の中身から判定する</summary>
    /// <remarks>
    /// Mermaid の <c>erDiagram</c> は「型・名前・単一キー標識」しか属性行に書けず、説明・NULL 許可・
    /// 複合制約・外部キーの列対応・参照アクションを表現する構文を持たない（ビュー用の表現形式であり、
    /// 往復での完全性は DBML・定義書・保存 JSON が担う）。ここではその欠落のうち、
    /// <b>図に実際に中身が入っているものだけ</b>を返す（説明が空の図で「説明が落ちる」とは言わない）
    /// </remarks>
    public static IReadOnlyList<ExportOmissionKind> DetectOmissions(ErDiagram diagram)
    {
        var checks = new (bool Detected, ExportOmissionKind Kind)[]
        {
            (
                diagram.Entities.Any(entity => !string.IsNullOrWhiteSpace(entity.Description)),
                ExportOmissionKind.TableDescription
            ),
            (
                diagram.Entities.Any(entity => !string.IsNullOrWhiteSpace(entity.Memo)),
                ExportOmissionKind.TableMemo
            ),
            (
                diagram.Entities.Any(entity =>
                    entity.Columns.Any(column => !string.IsNullOrWhiteSpace(column.Description))
                ),
                ExportOmissionKind.ColumnDescription
            ),
            (
                // Mermaid の属性行は NULL 許可を書けず、取込時は全列が NULL 許可になる
                diagram.Entities.Any(entity => entity.Columns.Any(column => !column.IsNullable)),
                ExportOmissionKind.ColumnNullability
            ),
            (
                diagram.Entities.Any(entity =>
                    entity.UniqueConstraints.Any(constraint => constraint.ColumnIds.Count > 1)
                ),
                ExportOmissionKind.CompositeUniqueConstraint
            ),
            (
                diagram.Entities.Any(entity =>
                    entity.UniqueConstraints.Any(constraint =>
                        !string.IsNullOrWhiteSpace(constraint.Name)
                    )
                ),
                ExportOmissionKind.UniqueConstraintName
            ),
            (
                diagram.Relationships.Any(relationship => relationship.ColumnPairs.Count > 0),
                ExportOmissionKind.ForeignKeyColumnPairs
            ),
            (
                diagram.Relationships.Any(relationship =>
                    relationship.OnDelete != ForeignKeyReferentialAction.NoAction
                    || relationship.OnUpdate != ForeignKeyReferentialAction.NoAction
                ),
                ExportOmissionKind.ReferentialAction
            ),
            (diagram.Queries.Count > 0, ExportOmissionKind.NamedQuery),
            (
                // 属性名として書けない文字を含む列名は書き換えて出す（黙って変えない）
                diagram.Entities.Any(entity =>
                    entity.Columns.Any(column =>
                    {
                        var sanitized = Identifier(column.Name);
                        return NormalizeAttributeName(sanitized) != sanitized;
                    })
                ),
                ExportOmissionKind.ColumnNameNormalized
            ),
            (
                // 属性行は主キー列に PK を付けるだけで順序の構文が無く、取込では列の並びが主キーの順序になる。
                // 判定の正本は GetReorderedPrimaryKeyColumnNames（列の並びと一致するなら落ちるものが無い）
                diagram.Entities.Any(entity =>
                    entity.GetReorderedPrimaryKeyColumnNames() is not null
                ),
                ExportOmissionKind.PrimaryKeyOrder
            ),
            (
                diagram.Entities.Any(entity => entity.Columns.Any(column => column.IsComputed)),
                ExportOmissionKind.ComputedColumn
            ),
            (
                // 型トークンに書けない文字を畳んだ結果、取込の復元で元の表記へ戻らない型（nvarchar(max)・double precision 等）。
                // 判定は書き出し（NormalizeDataType）と取込の復元（DenormalizeDataType）を実際に往復させて行う
                diagram.Entities.Any(entity =>
                    entity.Columns.Any(column =>
                        MermaidImporter.DenormalizeDataType(NormalizeDataType(column.DataType))
                        != column.DataType
                    )
                ),
                ExportOmissionKind.ColumnTypeNormalized
            ),
            (
                // 引用しても書けない文字（" % \）を含むテーブル名は畳んで出す（判定は BuildEntityNames と共有）
                diagram.Entities.Any(entity => FoldsEntityName(entity.TableName)),
                ExportOmissionKind.TableNameNormalized
            ),
            (
                // キー欄は 1 列 1 標識で FK が UK より優先されるため、外部キー列の単一列 UNIQUE は書けない
                // （主キー列の UNIQUE は主キー自身が一意なので落としても意味は変わらない）。判定は KeyMarker と共有
                diagram.Entities.Any(entity =>
                {
                    var uniqueColumnIds = CollectSingleColumnUniqueMembers(entity);

                    return entity.Columns.Any(column =>
                        uniqueColumnIds.Contains(column.Id)
                        && !column.IsPrimaryKey
                        && KeyMarker(column, isUnique: true) != "UK"
                    );
                }),
                ExportOmissionKind.UniqueConstraintOnForeignKeyColumn
            ),
            (
                LosesRelationshipConstraintNames(diagram),
                ExportOmissionKind.RelationshipConstraintName
            ),
        };

        return checks.Where(check => check.Detected).Select(check => check.Kind).ToList();
    }

    /// <summary>名前付きのリレーションのうち、取込で同じ制約名に戻らないものがあるかを判定する</summary>
    /// <remarks>
    /// <para>
    /// 取込がラベルを制約名に採るのは、引用の無い 1 語で、<see cref="UnnamedRelationshipLabel"/> でなく、
    /// ほかのリレーションに付いておらず、主キー・一意制約の名前と重ならないときだけ（<see cref="MermaidImporter"/>）。
    /// 引用して書いた名前（<see cref="FormatRelationshipLabel"/>）・同じ名前の外部キーが複数ある図・前後の空白や制御文字を
    /// 書き換えた名前は、取り込むと名前が落ちるか別の名前になる。
    /// </para>
    /// <para>
    /// 判定は出力を実際に取り込み直して、書いた順に制約名を突き合わせて行う（取込の規則を出力側へ写さないので、
    /// 規則が変わっても判定がずれない＝型の書き換えの判定と同じ流儀）。名前付きのリレーションが無ければ取り込まない。
    /// 取込そのものが失敗する図（大文字小文字だけ違うテーブル名が並ぶ等）は、名前も戻らないものとして扱う
    /// </para>
    /// </remarks>
    private static bool LosesRelationshipConstraintNames(ErDiagram diagram)
    {
        var entityIds = diagram.Entities.Select(entity => entity.Id).ToHashSet();

        // 書き出す関係行は、両端のテーブルが図にあるリレーションだけ（BuildRelationshipLine と同じ条件）で、順序も同じ
        var written = diagram
            .Relationships.Where(relationship =>
                entityIds.Contains(relationship.SourceEntityId)
                && entityIds.Contains(relationship.TargetEntityId)
            )
            .ToList();

        if (!written.Any(relationship => !string.IsNullOrWhiteSpace(relationship.ConstraintName)))
        {
            return false;
        }

        ErDiagram reimported;

        try
        {
            reimported = MermaidImporter.Parse(Build(diagram));
        }
        catch (InvalidDataException)
        {
            return true;
        }

        if (reimported.Relationships.Count != written.Count)
        {
            return true;
        }

        return written
            .Select((relationship, index) => (relationship, index))
            .Any(item =>
                !string.IsNullOrWhiteSpace(item.relationship.ConstraintName)
                && !string.Equals(
                    reimported.Relationships[item.index].ConstraintName,
                    item.relationship.ConstraintName,
                    StringComparison.Ordinal
                )
            );
    }

    /// <summary>テーブル ID → Mermaid の実体名（必要なら引用つき）の写像を作る</summary>
    /// <remarks>
    /// <para>
    /// Mermaid の素の実体名に使えるのは非 ASCII 文字・英数字・<c>_</c>・<c>-</c>・<c>*</c>・<c>.</c> だけで、
    /// それ以外（空白・括弧など）を含む名前は引用して <c>"Order Details"</c> と書く。素で書くと関係行の端点が
    /// 空白で切れ、取込全体が失敗する。素で書ける名前の出力は変えない。
    /// </para>
    /// <para>
    /// 引用の中にも書けない <c>"</c> <c>%</c> <c>\</c> は <c>_</c> へ畳み、
    /// <see cref="ExportOmissionKind.TableNameNormalized"/> で告知する。畳んだ結果が別のテーブルとぶつかると取込が
    /// 重複として断るため、列名（<see cref="BuildAttributeNames"/>）と同じく、畳む必要のない名前を先に予約し、
    /// 畳んだ名前に <c>_2</c>, <c>_3</c>, … を付けて空くまで進める（取込はテーブル名の大文字小文字を区別しないので、予約も区別しない）
    /// </para>
    /// </remarks>
    private static Dictionary<Guid, string> BuildEntityNames(IEnumerable<Entity> entities)
    {
        var result = new Dictionary<Guid, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folded = new List<Entity>();

        foreach (var entity in entities)
        {
            if (FoldsEntityName(entity.TableName))
            {
                folded.Add(entity);
                continue;
            }

            var sanitized = Identifier(entity.TableName);
            used.Add(sanitized);
            result[entity.Id] = PlainNamePattern().IsMatch(sanitized)
                ? sanitized
                : $"\"{sanitized}\"";
        }

        foreach (var entity in folded)
        {
            var replaced = UnquotableEntityNameCharacters()
                .Replace(Identifier(entity.TableName), "_");
            var candidate = replaced.Length == 0 ? "_" : replaced;
            var unique = candidate;

            for (var suffix = 2; !used.Add(unique); suffix++)
            {
                unique = $"{candidate}_{suffix}";
            }

            result[entity.Id] = PlainNamePattern().IsMatch(unique) ? unique : $"\"{unique}\"";
        }

        return result;
    }

    /// <summary>テーブル名を畳む必要があるか（素で書けず、引用にも書けない文字を含むか）を判定する</summary>
    private static bool FoldsEntityName(string tableName)
    {
        var sanitized = Identifier(tableName);

        return !PlainNamePattern().IsMatch(sanitized)
            && (sanitized.Length == 0 || UnquotableEntityNameCharacters().IsMatch(sanitized));
    }

    /// <summary>
    /// Mermaid が引用なしで読める名前（非 ASCII・英数字・<c>_ - * .</c>）に一致する正規表現を生成する
    /// </summary>
    /// <remarks>
    /// Mermaid の字句規則の <c>UNICODE_TEXT</c> と同じ文字集合で、実体名と関係行のラベルの両方がこの形なら素で書ける
    /// （それ以外は引用が要る）
    /// </remarks>
    [GeneratedRegex(@"^(?:[^\x00-\x7F]|[A-Za-z0-9_\-*.])+$", RegexOptions.Compiled)]
    private static partial Regex PlainNamePattern();

    /// <summary>Mermaid の引用実体名の中に書けない文字に一致する正規表現を生成する</summary>
    [GeneratedRegex(@"[""%\\]", RegexOptions.Compiled)]
    private static partial Regex UnquotableEntityNameCharacters();

    /// <summary>
    /// テーブル内の列 ID → Mermaid の属性名の写像を作る（衝突したら決定的に連番を付ける）
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mermaid の属性行は「型 名前 標識」を空白で区切るため、空白や記号を含む列名は後半が黙って消える。
    /// 型が既に <see cref="NormalizeDataType"/> で <c>_</c> へ畳まれているのと同じ扱いにし、
    /// 名前も畳む（畳んだことは <see cref="ExportOmissionKind.ColumnNameNormalized"/> で告知する）。
    /// </para>
    /// <para>
    /// 畳んだ結果が同じテーブルの別の列とぶつかると、読み込み直したときに黙って 1 つへ潰れる。
    /// <b>畳む必要のない名前を先に予約</b>してから、畳んだ名前に <c>_2</c>, <c>_3</c>, … を付けて空くまで進める
    /// （既にある名前を動かさない・列の宣言順で決まるので出力は決定的）。
    /// 畳む必要のある列が 1 つも無いテーブルの出力は従来どおり。
    /// </para>
    /// </remarks>
    private static Dictionary<Guid, string> BuildAttributeNames(Entity entity)
    {
        var result = new Dictionary<Guid, string>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        // 畳む必要のない名前を先に確定・予約する
        foreach (var column in entity.Columns)
        {
            var sanitized = Identifier(column.Name);

            if (NormalizeAttributeName(sanitized) == sanitized)
            {
                result[column.Id] = sanitized;
                used.Add(sanitized);
            }
        }

        foreach (var column in entity.Columns)
        {
            if (result.ContainsKey(column.Id))
            {
                continue;
            }

            var candidate = NormalizeAttributeName(Identifier(column.Name));
            var unique = candidate;

            for (var suffix = 2; !used.Add(unique); suffix++)
            {
                unique = $"{candidate}_{suffix}";
            }

            result[column.Id] = unique;
        }

        return result;
    }

    /// <summary>Mermaid の属性名に書けない文字（英数字とアンダースコア以外）に一致する正規表現を生成する</summary>
    [GeneratedRegex(@"[^\w]+", RegexOptions.Compiled)]
    private static partial Regex AttributeNamePattern();

    /// <summary>Mermaid の属性名として書けない文字（空白・記号）を <c>_</c> へ畳む</summary>
    /// <remarks>型トークンの正規化と同じ規則（連続する対象は 1 つの <c>_</c> へ・末尾の <c>_</c> は落とす）</remarks>
    private static string NormalizeAttributeName(string sanitizedName)
    {
        var folded = AttributeNamePattern().Replace(sanitizedName, "_").Trim('_');
        return folded.Length == 0 ? "_" : folded;
    }

    /// <summary>Mermaid の属性型トークン用に DataType を正規化する</summary>
    /// <remarks>
    /// <para>
    /// 英数字・<c>_</c>・<c>-</c>・<c>[</c> <c>]</c>・<c>.</c>・<c>*</c> 以外の文字の連続を 1 つのアンダースコアへ置換し、
    /// 末尾のアンダースコアを落とす。例: <c>decimal(10,2)</c> → <c>decimal_10_2</c>（取込が括弧の形へ戻す）・
    /// <c>integer[]</c> はそのまま。括弧とカンマは Mermaid の型にも書けるが、取込の復元
    /// （<see cref="MermaidImporter.DenormalizeDataType"/>）と対になる表記として畳む。
    /// </para>
    /// <para>
    /// 空白・括弧・カンマ以外の記号（<c>"</c> <c>'</c> 等）も畳む。残すと、<c>"</c> は取込が属性行の説明の開始と読んで
    /// 行が壊れ、<c>'</c> は Mermaid として不正になる。取込で元の表記へ戻らない書き換えは
    /// <see cref="ExportOmissionKind.ColumnTypeNormalized"/> で告知する。空の型は <see cref="EmptyDataTypeToken"/> で書く
    /// </para>
    /// </remarks>
    private static string NormalizeDataType(string dataType)
    {
        // 先に改行・制御文字を空白へ畳んでおく（制御文字が型トークンへ残ると行が壊れる）
        var result = DataTypeFoldPattern().Replace(Identifier(dataType), "_").TrimEnd('_');

        return result.Length == 0 ? EmptyDataTypeToken : result;
    }

    /// <summary>Mermaid の型トークンへ書かない文字の連続に一致する正規表現を生成する</summary>
    [GeneratedRegex(@"[^\w\-\[\].*]+", RegexOptions.Compiled)]
    private static partial Regex DataTypeFoldPattern();

    /// <summary>識別子（テーブル名・カラム名・リレーションのラベル）を Mermaid の行へ載せる形へ整える</summary>
    /// <remarks>
    /// Mermaid の <c>erDiagram</c> は 1 行 1 要素の記法で、名前に改行が混じると行が途中で終わり、
    /// 残りが別の宣言として解釈される（図の名前で出力の構造を壊せてしまう）。改行・制御文字は空白へ畳む
    /// </remarks>
    private static string Identifier(string? name) => ExportTextSanitizer.Sanitize(name);

    /// <summary>Mermaid の属性行を構築する</summary>
    /// <remarks>
    /// Mermaid は同一カラムへの PK と FK の同時指定を構文エラーとして扱うため、キー標識は
    /// <c>PK &gt; FK &gt; UK</c> の優先度で 1 つだけ出力する（GUI のキー標識
    /// <see cref="ColumnKeyMarkPalette"/> と同じ序列）
    /// </remarks>
    private static string BuildColumnLine(Column column, bool isUnique, string attributeName)
    {
        var builder = new StringBuilder();
        builder.Append(NormalizeDataType(column.DataType));
        builder.Append(' ');
        builder.Append(attributeName);

        if (KeyMarker(column, isUnique) is { } marker)
        {
            builder.Append(' ').Append(marker);
        }

        return builder.ToString();
    }

    /// <summary>列に書くキー標識を <c>PK &gt; FK &gt; UK</c> の優先度で 1 つ選ぶ（無ければ <c>null</c>）</summary>
    /// <remarks>
    /// 出力（<see cref="BuildColumnLine"/>）と欠落の判定（外部キー列の単一列 UNIQUE が書けないこと）が共有する
    /// </remarks>
    private static string? KeyMarker(Column column, bool isUnique) =>
        column.IsPrimaryKey ? "PK"
        : column.IsForeignKey ? "FK"
        : isUnique ? "UK"
        : null;

    /// <summary>単一列の一意制約に参加しているカラム ID を集める</summary>
    /// <remarks>
    /// Mermaid の ER 記法には複数列をひとまとめにする構文が無く、複合制約を列ごとの <c>UK</c> へ分解すると
    /// 取込時に「単一列の一意制約 × N」という別の意味になってしまう。そのため
    /// <b>出力するのは単一列制約の構成列だけ</b>とし、複合制約は出力しない（Mermaid はビュー用の
    /// 表現形式であり、往復での完全性は DBML・定義書・保存 JSON が担う）
    /// </remarks>
    private static HashSet<Guid> CollectSingleColumnUniqueMembers(Entity entity)
    {
        var members = new HashSet<Guid>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            if (constraint.ColumnIds.Count != 1)
            {
                continue;
            }

            var columnId = constraint.ColumnIds[0];

            if (entity.Columns.Any(column => column.Id == columnId))
            {
                members.Add(columnId);
            }
        }

        return members;
    }

    /// <summary>Mermaid のリレーション行を構築する。参照先エンティティが解決できない場合は null を返す</summary>
    /// <param name="relationship">対象のリレーション</param>
    /// <param name="entityNames">テーブル ID → 実体名（<see cref="BuildEntityNames"/>）</param>
    private static string? BuildRelationshipLine(
        Relationship relationship,
        IReadOnlyDictionary<Guid, string> entityNames
    )
    {
        if (
            !entityNames.TryGetValue(relationship.SourceEntityId, out var source)
            || !entityNames.TryGetValue(relationship.TargetEntityId, out var target)
        )
        {
            return null;
        }

        var symbol = relationship.Type switch
        {
            RelationshipType.OneToOne => "||--||",
            RelationshipType.OneToMany => "||--o{",
            RelationshipType.ManyToMany => "}o--o{",
            _ => "||--o{",
        };
        return $"{source} {symbol} {target} : {FormatRelationshipLabel(relationship.ConstraintName)}";
    }

    /// <summary>関係行のラベル（制約名、名前が無ければ <see cref="UnnamedRelationshipLabel"/>）を書く形にする</summary>
    /// <remarks>
    /// Mermaid のラベルは引用なしの 1 語か引用文字列で、空白や記号を含む名前を素で書くと行が構文として壊れる。
    /// 素で書けない名前は <c>"…"</c> で囲み、引用の中に書けない <c>"</c> は <c>_</c> へ畳む。
    /// 取込は引用されたラベルを制約名に採らないので、引用した名前は取込で落ちる
    /// （<see cref="ExportOmissionKind.RelationshipConstraintName"/> で告知する）
    /// </remarks>
    private static string FormatRelationshipLabel(string? constraintName)
    {
        if (string.IsNullOrWhiteSpace(constraintName))
        {
            return UnnamedRelationshipLabel;
        }

        var label = Identifier(constraintName);

        return PlainNamePattern().IsMatch(label) ? label : $"\"{label.Replace('"', '_')}\"";
    }
}
