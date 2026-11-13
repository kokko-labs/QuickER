using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>キーと表名の形（実装先どうしの一致の図の親子 3 組）</summary>
public enum KeyShape
{
    /// <summary>文字列の主キー＝GuidKey の値オブジェクト（<c>tokens</c>・<c>token_uses</c>）</summary>
    GuidKey,

    /// <summary><c>binary(4)</c> の主キー＝byte[] を包む値オブジェクト（<c>blobs</c>・<c>blob_tags</c>）</summary>
    BinaryKey,

    /// <summary>ドットを含む表名（<c>main.regions</c>・<c>main.region_notes</c>）</summary>
    DottedTable,
}

/// <summary>キーと表名の形の手</summary>
public enum KeyShapeStep
{
    /// <summary>親を挿入する（<c>Key</c>・<c>Text</c>）</summary>
    InsertParent,

    /// <summary>子を挿入する（<c>Child</c> の子が <c>Key</c> の親を指す）</summary>
    InsertChild,

    /// <summary>親を、新しく作ったキーのインスタンスで <c>GetByIdAsync</c> する</summary>
    GetParent,

    /// <summary>親を読み、文字列を変えて <c>UpdateAsync</c> する</summary>
    UpdateParentLoaded,

    /// <summary>読まずに組み立てた親（新しいキーのインスタンス）で <c>UpdateAsync</c> する</summary>
    UpdateParentDetached,

    /// <summary>新しい親（Added）と新しい子（Added）を 1 回のグラフ保存で書く</summary>
    SaveNewGraph,

    /// <summary>親と子 <c>Child</c> を別々に読んで親のコレクションへ足し、親の更新・その子の削除・新しい子 <c>Child</c>+1 の追加を 1 回のグラフ保存で書く</summary>
    SaveEditGraph,

    /// <summary>親とその子をすべて読んで親のコレクションへ足し、親を削除するグラフ保存（子はカスケード）</summary>
    SaveRemoveParentGraph,

    /// <summary>子を <c>DeleteAsync</c> する（キーは新しいインスタンス）</summary>
    DeleteChild,

    /// <summary>子を持たない親を <c>DeleteAsync</c> する（キーは新しいインスタンス）</summary>
    DeleteParent,

    /// <summary>無い親 <c>Key</c> を指す子 <c>Child</c> を挿入する（DB は外部キーで断る）</summary>
    InsertOrphanChild,

    /// <summary>GuidKey の引数なし <c>Create()</c> のキーで挿入し、値から作り直したキーで読み、消す</summary>
    GeneratedKeyRoundTrip,

    /// <summary>全機能面：キーの等値の条件で親を読む</summary>
    QueryParentByKey,

    /// <summary>全機能面：キーのリストの <c>Contains</c>（IN）で親を読む（<c>Key</c> と <c>Child</c> の 2 つの親）</summary>
    QueryParentsInKeys,

    /// <summary>全機能面：親を子のコレクションごと Include して読む</summary>
    IncludeChildren,

    /// <summary>全機能面：全部の子を親の参照ごと Include して読む</summary>
    IncludeParent,

    /// <summary>全機能面：親を <c>Child</c> 件 <c>BulkInsertAsync</c> する（キーは <c>Key</c> から）</summary>
    BulkInsertParents,

    /// <summary>除外 blob の Stream で親の photo を書く（ドットを含む表名だけ）</summary>
    WriteBlob,

    /// <summary>除外 blob の Stream で親の photo を読む（ドットを含む表名だけ）</summary>
    ReadBlob,
}

/// <summary>
/// キーと表名の形の手。形ごとの束縛（<see cref="KeyShapes"/>）で、キーで引く・キーを比べる・名前を引用する経路を全部の実装先へ流す。
/// </summary>
/// <remarks>
/// 読み込んだエンティティはスロットに置かず、戻り値の文字列だけを比べる（保存先の行は <see cref="ParityBackend.ReadStateAsync"/> が読む）。
/// キーは手のたびに新しいインスタンスで作る（GuidKey は同じ文字列の別の値オブジェクト、binary は同じバイト列の別の配列）。
/// </remarks>
/// <param name="Shape">形</param>
/// <param name="Step">手</param>
/// <param name="Key">親のキーの番号</param>
/// <param name="Child">子のキーの番号（手によっては 2 つ目の親・件数）</param>
/// <param name="Text">書く文字列</param>
public sealed record KeyShapeOp(
    KeyShape Shape,
    KeyShapeStep Step,
    int Key,
    int Child = 0,
    string Text = ""
) : ParityOp
{
    public override string Name => $"KeyShape{Step}";

    public override bool Supports(ParityBackendKind kind) =>
        Step switch
        {
            KeyShapeStep.QueryParentByKey
            or KeyShapeStep.QueryParentsInKeys
            or KeyShapeStep.IncludeChildren
            or KeyShapeStep.IncludeParent
            or KeyShapeStep.BulkInsertParents => !ParityBackendKinds.IsRemote(kind),
            KeyShapeStep.WriteBlob or KeyShapeStep.ReadBlob => ParityBackendKinds.HasBlobStream(
                kind
            ),
            _ => true,
        };

    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        Shape switch
        {
            KeyShape.GuidKey => KeyShapes.Tokens.RunAsync(this, backend, ct),
            KeyShape.BinaryKey => KeyShapes.Blobs.RunAsync(this, backend, ct),
            KeyShape.DottedTable => KeyShapes.Regions.RunAsync(this, backend, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(Shape), Shape, null),
        };
}

/// <summary>キーと表名の形の束縛（形ごとの型・キーの作り方・行の書式）</summary>
public static class KeyShapes
{
    /// <summary>GuidKey の親のキー（番号から決まる GUID の文字列）</summary>
    public static string TokenKeyText(int key) =>
        $"00000000-0000-4000-8000-{key.ToString("D12", CultureInfo.InvariantCulture)}";

    /// <summary>binary(4) の親のキー（base64 にすると <c>+</c> と <c>/</c> を含むバイト列＋番号）</summary>
    public static byte[] BlobKeyBytes(int key) => [0xFB, 0xFF, 0xBF, (byte)key];

    /// <summary>GuidKey の束縛</summary>
    internal static KeyShapeBinding<
        TokenEntity,
        TokenIdValue,
        TokenUseEntity,
        UseIdValue
    > Tokens { get; } =
        new()
        {
            ParentTable = "tokens",
            ChildTable = "token_uses",
            ParentKey = key => TokenIdValue.Create(TokenKeyText(key)),
            ChildKey = UseIdValue.Create,
            ParentKeyText = k => k.Value,
            ChildKeyText = k => k.Value.ToString(CultureInfo.InvariantCulture),
            KeyOfParent = p => p.TokenId,
            KeyOfChild = c => c.UseId,
            ParentOfChild = c => c.TokenId,
            NewParent = (key, text) =>
                new TokenEntity { TokenId = key, Caption = CaptionValue.Create(text) },
            NewChild = (child, parent, text) =>
                new TokenUseEntity
                {
                    UseId = child,
                    TokenId = parent,
                    Purpose = PurposeValue.Create(text),
                },
            TextOfParent = p => p.Caption?.Value,
            TextOfChild = c => c.Purpose?.Value,
            SetParentText = (p, text) => p.Caption = CaptionValue.Create(text),
            Children = p => p.TokenUses,
            ParentNavigation = c => c.Token,
            RemoteParents = b => b.Remote<ITokenRemoteRepository>(),
            RemoteChildren = b => b.Remote<ITokenUseRemoteRepository>(),
            FullParents = b => b.Full<ITokenRepository>(),
            FullChildren = b => b.Full<ITokenUseRepository>(),
            OutsideParents = r => r.Tokens,
            OutsideChildren = r => r.TokenUses,
            KeyEquals = key => p => p.TokenId == key,
            KeyIn = keys => p => keys.Contains(p.TokenId),
            IncludeChildrenOf = q => q.Include(p => p.TokenUses),
            IncludeParentOf = q => q.Include(c => c.Token),
            GeneratedKey = TokenIdValue.Create,
        };

    /// <summary>binary(4) の主キーの束縛</summary>
    internal static KeyShapeBinding<
        BlobEntity,
        HashValue,
        BlobTagEntity,
        TagIdValue
    > Blobs { get; } =
        new()
        {
            ParentTable = "blobs",
            ChildTable = "blob_tags",
            ParentKey = key => HashValue.Create(BlobKeyBytes(key)),
            ChildKey = TagIdValue.Create,
            ParentKeyText = k => Convert.ToHexString(k.Value),
            ChildKeyText = k => k.Value.ToString(CultureInfo.InvariantCulture),
            KeyOfParent = p => p.Hash,
            KeyOfChild = c => c.TagId,
            ParentOfChild = c => c.Hash,
            NewParent = (key, text) =>
                new BlobEntity { Hash = key, Title = TitleValue.Create(text) },
            NewChild = (child, parent, text) =>
                new BlobTagEntity
                {
                    TagId = child,
                    Hash = parent,
                    TagText = TagTextValue.Create(text),
                },
            TextOfParent = p => p.Title?.Value,
            TextOfChild = c => c.TagText?.Value,
            SetParentText = (p, text) => p.Title = TitleValue.Create(text),
            Children = p => p.BlobTags,
            ParentNavigation = c => c.Blob,
            RemoteParents = b => b.Remote<IBlobRemoteRepository>(),
            RemoteChildren = b => b.Remote<IBlobTagRemoteRepository>(),
            FullParents = b => b.Full<IBlobRepository>(),
            FullChildren = b => b.Full<IBlobTagRepository>(),
            OutsideParents = r => r.Blobs,
            OutsideChildren = r => r.BlobTags,
            KeyEquals = key => p => p.Hash == key,
            KeyIn = keys => p => keys.Contains(p.Hash),
            IncludeChildrenOf = q => q.Include(p => p.BlobTags),
            IncludeParentOf = q => q.Include(c => c.Blob),
        };

    /// <summary>ドットを含む表名の束縛</summary>
    internal static KeyShapeBinding<
        MainRegionEntity,
        RegionIdValue,
        MainRegionNoteEntity,
        RegionNoteIdValue
    > Regions { get; } =
        new()
        {
            ParentTable = "main.regions",
            ChildTable = "main.region_notes",
            ParentKey = RegionIdValue.Create,
            ChildKey = RegionNoteIdValue.Create,
            ParentKeyText = k => k.Value.ToString(CultureInfo.InvariantCulture),
            ChildKeyText = k => k.Value.ToString(CultureInfo.InvariantCulture),
            KeyOfParent = p => p.RegionId,
            KeyOfChild = c => c.RegionNoteId,
            ParentOfChild = c => c.RegionId,
            NewParent = (key, text) =>
                new MainRegionEntity { RegionId = key, RegionName = RegionNameValue.Create(text) },
            NewChild = (child, parent, text) =>
                new MainRegionNoteEntity
                {
                    RegionNoteId = child,
                    RegionId = parent,
                    Body = BodyValue.Create(text),
                },
            TextOfParent = p => p.RegionName?.Value,
            TextOfChild = c => c.Body?.Value,
            SetParentText = (p, text) => p.RegionName = RegionNameValue.Create(text),
            Children = p => p.MainRegionNotes,
            ParentNavigation = c => c.MainRegion,
            RemoteParents = b => b.Remote<IMainRegionRemoteRepository>(),
            RemoteChildren = b => b.Remote<IMainRegionNoteRemoteRepository>(),
            FullParents = b => b.Full<IMainRegionRepository>(),
            FullChildren = b => b.Full<IMainRegionNoteRepository>(),
            OutsideParents = r => r.Regions,
            OutsideChildren = r => r.RegionNotes,
            KeyEquals = key => p => p.RegionId == key,
            KeyIn = keys => p => keys.Contains(p.RegionId),
            IncludeChildrenOf = q => q.Include(p => p.MainRegionNotes),
            IncludeParentOf = q => q.Include(c => c.MainRegion),
            WriteBlob = (b, key, data, ct) =>
                b.Remote<IMainRegionRemoteRepository>()
                    .WritePhotoAsync(key, new MemoryStream(data), data.Length, ct),
            ReadBlob = (b, key, destination, ct) =>
                b.Remote<IMainRegionRemoteRepository>().ReadPhotoAsync(key, destination, ct),
        };

    /// <summary>形の行のキー（フックの記録・<see cref="ParityFormat.KeyOf"/>）。形の型でなければ null</summary>
    public static string? KeyOf(EntityBaseCore entity) =>
        entity switch
        {
            TokenEntity t => Tokens.ParentKeyOf(t),
            TokenUseEntity u => Tokens.ChildKeyOf(u),
            BlobEntity b => Blobs.ParentKeyOf(b),
            BlobTagEntity t => Blobs.ChildKeyOf(t),
            MainRegionEntity r => Regions.ParentKeyOf(r),
            MainRegionNoteEntity n => Regions.ChildKeyOf(n),
            _ => null,
        };

    /// <summary>保存先の形の行（表の順・キー順。<see cref="ParityBackend.ReadStateAsync"/> が使う）</summary>
    public static async Task<IReadOnlyList<string>> ReadStateAsync(
        ParityRepositories outside,
        CancellationToken ct
    ) =>
        [
            .. await Tokens.ReadStateAsync(outside, ct),
            .. await Blobs.ReadStateAsync(outside, ct),
            .. await Regions.ReadStateAsync(outside, ct),
        ];
}

/// <summary>1 つの形の親子の型・キーの作り方・行の書式と、手の実装</summary>
internal sealed class KeyShapeBinding<TParent, TParentKey, TChild, TChildKey>
    where TParent : EntityBaseCore, new()
    where TChild : EntityBaseCore, new()
{
    public required string ParentTable { get; init; }
    public required string ChildTable { get; init; }
    public required Func<int, TParentKey> ParentKey { get; init; }
    public required Func<int, TChildKey> ChildKey { get; init; }
    public required Func<TParentKey, string> ParentKeyText { get; init; }
    public required Func<TChildKey, string> ChildKeyText { get; init; }
    public required Func<TParent, TParentKey?> KeyOfParent { get; init; }
    public required Func<TChild, TChildKey?> KeyOfChild { get; init; }
    public required Func<TChild, TParentKey?> ParentOfChild { get; init; }
    public required Func<TParentKey, string, TParent> NewParent { get; init; }
    public required Func<TChildKey, TParentKey, string, TChild> NewChild { get; init; }
    public required Func<TParent, string?> TextOfParent { get; init; }
    public required Func<TChild, string?> TextOfChild { get; init; }
    public required Action<TParent, string> SetParentText { get; init; }
    public required Func<TParent, ICollection<TChild>> Children { get; init; }
    public required Func<TChild, TParent?> ParentNavigation { get; init; }
    public required Func<
        ParityBackend,
        IRemoteRepository<TParent, TParentKey>
    > RemoteParents { get; init; }
    public required Func<
        ParityBackend,
        IRemoteRepository<TChild, TChildKey>
    > RemoteChildren { get; init; }
    public required Func<ParityBackend, IRepository<TParent, TParentKey>> FullParents { get; init; }
    public required Func<ParityBackend, IRepository<TChild, TChildKey>> FullChildren { get; init; }
    public required Func<
        ParityRepositories,
        IRepository<TParent, TParentKey>
    > OutsideParents { get; init; }
    public required Func<
        ParityRepositories,
        IRepository<TChild, TChildKey>
    > OutsideChildren { get; init; }
    public required Func<TParentKey, Expression<Func<TParent, bool>>> KeyEquals { get; init; }
    public required Func<List<TParentKey>, Expression<Func<TParent, bool>>> KeyIn { get; init; }
    public required Func<
        SqlQuery<TParent>,
        IncludableSqlQuery<TParent, TChild>
    > IncludeChildrenOf { get; init; }
    public required Func<
        SqlQuery<TChild>,
        IncludableSqlQuery<TChild, TParent>
    > IncludeParentOf { get; init; }

    /// <summary>引数なしで新しいキーを作る（GuidKey だけ）</summary>
    public Func<TParentKey>? GeneratedKey { get; init; }

    /// <summary>除外 blob の Stream の書き込み（ドットを含む表名だけ）</summary>
    public Func<
        ParityBackend,
        TParentKey,
        byte[],
        CancellationToken,
        Task<bool>
    >? WriteBlob { get; init; }

    /// <summary>除外 blob の Stream の読み取り（ドットを含む表名だけ）</summary>
    public Func<
        ParityBackend,
        TParentKey,
        Stream,
        CancellationToken,
        Task<bool>
    >? ReadBlob { get; init; }

    /// <summary>親の行のキー（<c>tokens/…</c> の形）</summary>
    public string ParentKeyOf(TParent parent) =>
        $"{ParentTable}/{(KeyOfParent(parent) is { } key ? ParentKeyText(key) : "null")}";

    /// <summary>子の行のキー（<c>token_uses/…</c> の形）</summary>
    public string ChildKeyOf(TChild child) =>
        $"{ChildTable}/{(KeyOfChild(child) is { } key ? ChildKeyText(key) : "null")}";

    private string ParentRow(TParent parent) =>
        $"{ParentKeyOf(parent)} text={TextOfParent(parent)}";

    private string ChildRow(TChild child) =>
        $"{ChildKeyOf(child)} parent={(ParentOfChild(child) is { } key ? ParentKeyText(key) : "null")} "
        + $"text={TextOfChild(child)}";

    /// <summary>保存先の行（親の表・子の表の順、それぞれキーの文字列の序数順）</summary>
    public async Task<IReadOnlyList<string>> ReadStateAsync(
        ParityRepositories outside,
        CancellationToken ct
    ) =>
        [
            .. (await OutsideParents(outside).GetAllAsync(ct))
                .Select(ParentRow)
                .Order(StringComparer.Ordinal),
            .. (await OutsideChildren(outside).GetAllAsync(ct))
                .Select(ChildRow)
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>手を流して、戻り値を文字列で返す</summary>
    public async Task<string> RunAsync(KeyShapeOp op, ParityBackend backend, CancellationToken ct)
    {
        var parents = RemoteParents(backend);
        var children = RemoteChildren(backend);

        switch (op.Step)
        {
            case KeyShapeStep.InsertParent:
            {
                var parent = NewParent(ParentKey(op.Key), op.Text);
                await parents.InsertAsync(parent, ct);
                return ParentRow(parent);
            }

            case KeyShapeStep.InsertChild:
            case KeyShapeStep.InsertOrphanChild:
            {
                var child = NewChild(ChildKey(op.Child), ParentKey(op.Key), op.Text);
                await children.InsertAsync(child, ct);
                return ChildRow(child);
            }

            case KeyShapeStep.GetParent:
                return await parents.GetByIdAsync(ParentKey(op.Key), ct) is { } stored
                    ? ParentRow(stored)
                    : "null";

            case KeyShapeStep.UpdateParentLoaded:
            {
                var parent =
                    await parents.GetByIdAsync(ParentKey(op.Key), ct)
                    ?? throw new InvalidOperationException(
                        $"{ParentTable}/{op.Key} is not stored."
                    );
                SetParentText(parent, op.Text);
                return $"updated={await parents.UpdateAsync(parent, cancellationToken: ct)}";
            }

            case KeyShapeStep.UpdateParentDetached:
                return $"updated={await parents.UpdateAsync(NewParent(ParentKey(op.Key), op.Text), cancellationToken: ct)}";

            case KeyShapeStep.SaveNewGraph:
            {
                var parent = NewParent(ParentKey(op.Key), op.Text);
                parent.MarkAdded();
                var child = NewChild(ChildKey(op.Child), ParentKey(op.Key), op.Text);
                child.MarkAdded();
                Children(parent).Add(child);
                var saved = await parents.SaveAsync(parent, cancellationToken: ct);
                return $"saved={saved} parent={parent.RowState} child={child.RowState}";
            }

            case KeyShapeStep.SaveEditGraph:
            {
                var parent =
                    await parents.GetByIdAsync(ParentKey(op.Key), ct)
                    ?? throw new InvalidOperationException(
                        $"{ParentTable}/{op.Key} is not stored."
                    );
                var removed =
                    await children.GetByIdAsync(ChildKey(op.Child), ct)
                    ?? throw new InvalidOperationException(
                        $"{ChildTable}/{op.Child} is not stored."
                    );
                Children(parent).Add(removed);
                SetParentText(parent, op.Text);
                parent.MarkUpdated();
                removed.MarkRemoved();
                var added = NewChild(ChildKey(op.Child + 1), ParentKey(op.Key), op.Text);
                added.MarkAdded();
                Children(parent).Add(added);
                var saved = await parents.SaveAsync(parent, cancellationToken: ct);
                var left = string.Join(
                    ",",
                    Children(parent)
                        .Select(c => $"{ChildKeyOf(c)}:{c.RowState}")
                        .Order(StringComparer.Ordinal)
                );
                return $"saved={saved} parent={parent.RowState} removed={removed.RowState} children=[{left}]";
            }

            case KeyShapeStep.SaveRemoveParentGraph:
            {
                var parent =
                    await parents.GetByIdAsync(ParentKey(op.Key), ct)
                    ?? throw new InvalidOperationException(
                        $"{ParentTable}/{op.Key} is not stored."
                    );
                var parentKeyText = ParentKeyText(ParentKey(op.Key));

                foreach (
                    var child in (await children.GetAllAsync(ct)).Where(c =>
                        ParentOfChild(c) is { } key && ParentKeyText(key) == parentKeyText
                    )
                )
                {
                    Children(parent).Add(child);
                }

                var count = Children(parent).Count;
                parent.MarkRemoved();
                var saved = await parents.SaveAsync(parent, cancellationToken: ct);
                return $"saved={saved} children={count} parent={parent.RowState}";
            }

            case KeyShapeStep.DeleteChild:
                return $"deleted={await children.DeleteAsync(ChildKey(op.Child), ct)}";

            case KeyShapeStep.DeleteParent:
                return $"deleted={await parents.DeleteAsync(ParentKey(op.Key), ct)}";

            case KeyShapeStep.GeneratedKeyRoundTrip:
            {
                var key = (
                    GeneratedKey
                    ?? throw new InvalidOperationException($"{ParentTable} has no generated key.")
                )();
                await parents.InsertAsync(NewParent(key, op.Text), ct);
                var text = ParentKeyText(key);
                var reread = await parents.GetByIdAsync(RebuildKey(text), ct);
                var deleted = await parents.DeleteAsync(RebuildKey(text), ct);
                return $"length={text.Length} found={reread is not null} deleted={deleted}";
            }

            case KeyShapeStep.QueryParentByKey:
            {
                var rows = await FullParents(backend)
                    .Query()
                    .Where(KeyEquals(ParentKey(op.Key)))
                    .ToListAsync(ct);
                return string.Join("|", rows.Select(ParentRow).Order(StringComparer.Ordinal));
            }

            case KeyShapeStep.QueryParentsInKeys:
            {
                var rows = await FullParents(backend)
                    .Query()
                    .Where(KeyIn([ParentKey(op.Key), ParentKey(op.Child)]))
                    .ToListAsync(ct);
                return string.Join("|", rows.Select(ParentRow).Order(StringComparer.Ordinal));
            }

            case KeyShapeStep.IncludeChildren:
            {
                var rows = await IncludeChildrenOf(
                        FullParents(backend).Query().Where(KeyEquals(ParentKey(op.Key)))
                    )
                    .ToListAsync(ct);
                return string.Join(
                    "|",
                    rows.Select(p =>
                            $"{ParentRow(p)} children=[{string.Join(",", Children(p).Select(ChildKeyOf).Order(StringComparer.Ordinal))}]"
                        )
                        .Order(StringComparer.Ordinal)
                );
            }

            case KeyShapeStep.IncludeParent:
            {
                var rows = await IncludeParentOf(FullChildren(backend).Query()).ToListAsync(ct);
                return string.Join(
                    "|",
                    rows.Select(c =>
                            $"{ChildKeyOf(c)}->{(ParentNavigation(c) is { } parent ? ParentKeyOf(parent) : "null")}"
                        )
                        .Order(StringComparer.Ordinal)
                );
            }

            case KeyShapeStep.BulkInsertParents:
            {
                var rows = Enumerable
                    .Range(op.Key, op.Child)
                    .Select(key => NewParent(ParentKey(key), op.Text))
                    .ToList();
                return $"inserted={await FullParents(backend).BulkInsertAsync(rows, ct)}";
            }

            case KeyShapeStep.WriteBlob:
            {
                var write =
                    WriteBlob ?? throw new InvalidOperationException($"{ParentTable} has no blob.");
                return $"written={await write(backend, ParentKey(op.Key), [0x01, 0x02, 0x03], ct)}";
            }

            case KeyShapeStep.ReadBlob:
            {
                var read =
                    ReadBlob ?? throw new InvalidOperationException($"{ParentTable} has no blob.");
                using var destination = new MemoryStream();
                var hasBlob = await read(backend, ParentKey(op.Key), destination, ct);
                return $"found={hasBlob} bytes={Convert.ToHexString(destination.ToArray())}";
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op.Step, null);
        }
    }

    /// <summary>キーの文字列から新しいキーのインスタンスを作り直す（GuidKey だけ）</summary>
    private TParentKey RebuildKey(string text) =>
        typeof(TParentKey) == typeof(TokenIdValue)
            ? (TParentKey)(object)TokenIdValue.Create(text)
            : throw new InvalidOperationException($"{ParentTable} cannot rebuild a key from text.");
}
