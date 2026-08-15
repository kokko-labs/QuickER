using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// マルチ方言（実効方言 2 つ以上）の列型辞書を突き合わせ、共有 Entity の型整合性を検証・補完する純ロジック。
/// </summary>
/// <remarks>
/// 生成器は DB 非依存を保つため、型解決（DB 型 → C# 型）は呼び出し側（プロバイダ）が方言ごとに行って渡す。
/// 共有 Entity は 1 型のため、方言間で C# 型（型名・参照/値区分）が食い違うと生成物が壊れる。ここで不一致を
/// 診断エラーにして黙って劣化させないほか、sqlserver がターゲットに含まれる場合は <c>[SqlColumnType]</c> の
/// メタ情報（SqlDbType・Size 等）を sqlserver 辞書から主辞書へ補完する（図の方言が非 sqlserver でも属性を出す）。
/// 行バージョン列だけは食い違いを不一致とせず、行バージョンとして解決した方言の型（<c>byte[]</c>）へ統一する
/// （<see cref="ReconcileRowVersionTypes"/>）。
/// </remarks>
internal static class MultiDialectTypeReconciler
{
    /// <summary>
    /// 行バージョン列の型統一の結果。
    /// </summary>
    /// <param name="ColumnTypes">統一後の主辞書（統一対象が無ければ入力と同一インスタンス）</param>
    /// <param name="UnifiedColumnIds">統一した（＝型不一致検証の対象外にする）カラム ID の集合</param>
    /// <param name="Lines">Info 診断へ載せる <c>{テーブル}.{列}</c> 表記の一覧（図の並び順）</param>
    /// <param name="RowVersionDialect">行バージョンとして解決した方言名（統一対象が無ければ <c>null</c>）</param>
    public sealed record RowVersionReconciliation(
        IReadOnlyDictionary<Guid, CSharpTypeInfo> ColumnTypes,
        IReadOnlySet<Guid> UnifiedColumnIds,
        IReadOnlyList<string> Lines,
        string? RowVersionDialect
    );

    /// <summary>
    /// 行バージョン列（<see cref="CSharpTypeInfo.IsRowVersion"/>）を、そう解決した方言の型で主辞書へ統一する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同じ DB 型表記が方言によって別の意味を持つことがある。<c>timestamp</c> は SQL Server では行バージョン
    /// （<c>byte[]</c>）だが SQLite では日時（<c>DateTime</c>）で、そのままでは
    /// <see cref="DiagnoseTypeMismatches"/> が食い違いエラーにしてマルチターゲット生成自体が通らない。
    /// 行バージョンは「DB が採番する 8 バイトのバイナリ」という具体的な形を持つため、共有 Entity はその解決
    /// （<c>byte[]</c>・<see cref="CSharpTypeInfo.IsRowVersion"/>）へ寄せるのが唯一意味の通る統一先になる。
    /// </para>
    /// <para>
    /// 統一しても行バージョンとして扱うのはそう解決した方言の Repository だけで、他方言の実装ではただのバイナリ列
    /// （INSERT / UPDATE で書き込む・版ガードなし）になる。呼び出し側はこの非対称を Info 診断で通知する。
    /// </para>
    /// <para>
    /// 実効方言の辞書が 1 つ以下のとき（単一方言）は何もしない（統一する相手がおらず、出力もバイト不変）。
    /// </para>
    /// </remarks>
    public static RowVersionReconciliation ReconcileRowVersionTypes(
        ErDiagram diagram,
        IReadOnlyList<string> effectiveDialects,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> primaryColumnTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> columnTypesByDialect
    )
    {
        var dicts = ResolveDialectDictionaries(effectiveDialects, columnTypesByDialect);

        if (dicts.Count < 2)
        {
            return new RowVersionReconciliation(
                primaryColumnTypes,
                new HashSet<Guid>(),
                [],
                RowVersionDialect: null
            );
        }

        var unified = new HashSet<Guid>();
        var lines = new List<string>();
        string? rowVersionDialect = null;
        Dictionary<Guid, CSharpTypeInfo>? replaced = null;

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                // 行バージョンとして解決した方言を探す（実効方言の並び順＝先に指定した方言を優先する）
                var owner = dicts.FirstOrDefault(pair =>
                    pair.Types.TryGetValue(column.Id, out var info) && info.IsRowVersion
                );

                if (owner.Types is null)
                {
                    continue;
                }

                unified.Add(column.Id);
                lines.Add($"{entity.TableName}.{column.Name}");
                rowVersionDialect ??= owner.Dialect;

                // 主辞書が既に行バージョンの解決（図の方言が sqlserver の通常ケース）ならそのままでよい
                if (
                    primaryColumnTypes.TryGetValue(column.Id, out var primary)
                    && primary.IsRowVersion
                )
                {
                    continue;
                }

                replaced ??= new Dictionary<Guid, CSharpTypeInfo>(primaryColumnTypes);
                // 行バージョンの解決をそのまま採る。中立トークン（[DbColumnMeta]）は行バージョン列には刻まない規則
                // （CanonicalTypeTokenAttacher）なので、主辞書側に載っていたトークンごと差し替えるのが正しい
                replaced[column.Id] = owner.Types[column.Id];
            }
        }

        return new RowVersionReconciliation(
            replaced ?? primaryColumnTypes,
            unified,
            lines,
            rowVersionDialect
        );
    }

    /// <summary>実効方言のうち列型辞書が渡されているものを、指定順で <c>(方言, 辞書)</c> の一覧にする</summary>
    private static List<(
        string Dialect,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> Types
    )> ResolveDialectDictionaries(
        IReadOnlyList<string> effectiveDialects,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> columnTypesByDialect
    ) =>
        effectiveDialects
            .Where(columnTypesByDialect.ContainsKey)
            .Select(d => (Dialect: d, Types: columnTypesByDialect[d]))
            .ToList();

    /// <summary>
    /// 方言間で共有 Entity の C# 型（型名・参照/値区分）が食い違うカラムを診断エラーにする。
    /// </summary>
    /// <remarks>
    /// 共有 Entity は単一型のため、方言間で型が食い違うと生成物が壊れる。黙って劣化させず明示エラーにする。
    /// ただし <paramref name="rowVersionColumnIds"/>（<see cref="ReconcileRowVersionTypes"/> が統一した行バージョン列）は
    /// 意味の通る統一先が決まっているため、食い違っていてもエラーにしない。
    /// </remarks>
    public static void DiagnoseTypeMismatches(
        ErDiagram diagram,
        IReadOnlyList<string> effectiveDialects,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> columnTypesByDialect,
        IReadOnlySet<Guid> rowVersionColumnIds,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // 実効方言のうち辞書が渡されているものだけを対象にする
        var dicts = ResolveDialectDictionaries(effectiveDialects, columnTypesByDialect);

        if (dicts.Count < 2)
        {
            return;
        }

        var baseline = dicts[0];

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                if (
                    rowVersionColumnIds.Contains(column.Id)
                    || !baseline.Types.TryGetValue(column.Id, out var baseInfo)
                )
                {
                    continue;
                }

                foreach (var (dialect, types) in dicts.Skip(1))
                {
                    if (!types.TryGetValue(column.Id, out var info))
                    {
                        continue;
                    }

                    if (
                        !string.Equals(info.TypeName, baseInfo.TypeName, StringComparison.Ordinal)
                        || info.IsReferenceType != baseInfo.IsReferenceType
                    )
                    {
                        diagnostics.Add(
                            GenerationDiagnostic.Error(
                                string.Format(
                                    Strings.CodeGen_Error_TypeMismatch,
                                    entity.TableName,
                                    column.Name,
                                    baseline.Dialect,
                                    baseInfo.TypeName,
                                    dialect,
                                    info.TypeName
                                )
                            )
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    /// 主辞書に <c>[SqlColumnType]</c> のメタ情報（SqlDbType・宣言長・精度）を sqlserver 辞書から補完する。
    /// </summary>
    /// <remarks>
    /// 図の方言が非 sqlserver でも、sqlserver 実装が明示 SqlParameter を組み立てるため属性が必要になる。
    /// 主辞書の SqlDbTypeName が空で sqlserver 辞書に値がある列だけ、その 4 項目（SqlDbTypeName / SqlDeclaredLength /
    /// Precision / Scale）を差し替えた新しい型情報を作る。型名・参照区分など他の項目は主辞書のまま保つ。
    /// sqlserver がターゲットに含まれない場合は主辞書をそのまま返す。
    /// </remarks>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> SupplementSqlColumnTypes(
        IReadOnlyDictionary<Guid, CSharpTypeInfo> primaryColumnTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> columnTypesByDialect
    )
    {
        var sqlServerKey = columnTypesByDialect.Keys.FirstOrDefault(k =>
            string.Equals(k, "sqlserver", StringComparison.OrdinalIgnoreCase)
        );

        if (sqlServerKey is null)
        {
            return primaryColumnTypes;
        }

        var sqlServerTypes = columnTypesByDialect[sqlServerKey];
        var result = new Dictionary<Guid, CSharpTypeInfo>(primaryColumnTypes.Count);

        foreach (var (columnId, primary) in primaryColumnTypes)
        {
            // 主辞書が既に SqlDbTypeName を持つ（図の方言が sqlserver）場合はそのまま使う
            if (
                primary.SqlDbTypeName is not null
                || !sqlServerTypes.TryGetValue(columnId, out var sqlServer)
                || sqlServer.SqlDbTypeName is null
            )
            {
                result[columnId] = primary;

                continue;
            }

            // 主辞書の型（型名・参照区分・MaxLength）は保ちつつ、SqlServer 由来の SqlColumnType メタ情報だけ載せる
            result[columnId] = new CSharpTypeInfo
            {
                TypeName = primary.TypeName,
                IsReferenceType = primary.IsReferenceType,
                MaxLength = primary.MaxLength,
                Precision = sqlServer.Precision,
                Scale = sqlServer.Scale,
                SqlDbTypeName = sqlServer.SqlDbTypeName,
                SqlDeclaredLength = sqlServer.SqlDeclaredLength,
                IsRowVersion = primary.IsRowVersion,
                IsUnboundedBinary = primary.IsUnboundedBinary,
            };
        }

        return result;
    }
}
