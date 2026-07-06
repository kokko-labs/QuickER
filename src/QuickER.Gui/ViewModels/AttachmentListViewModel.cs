using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;

namespace QuickER.ViewModels;

/// <summary>
/// チャット／モック会話へ同梱する「送信待ち添付」を束ねる共通 VM 部品。
/// 追加（ファイルパス群・クリップボード画像）・削除・クリア・上限／種別の検証と通知・
/// <see cref="AttachmentSupport"/> による可否（None=無効＋理由・Images=PDF 拒否）を担う。
/// </summary>
/// <remarks>
/// AiChatDialog / MockGenerationDialog の両方が保持し、UI（チップ列＋クリップボタン）を共通化する。
/// エラー・案内は注入されたコールバック経由で各ダイアログのステータス表示へ流す。
/// </remarks>
public partial class AttachmentListViewModel : ObservableObject
{
    /// <summary>ステータス表示（エラー・案内）を各ダイアログへ流すコールバック</summary>
    private readonly Action<string> _reportStatus;

    /// <summary>画像縮小デリゲート（上限超過画像の自動縮小に使う・null 可）</summary>
    private readonly ChatAttachmentFactory.ImageShrinker? _shrinker;

    /// <summary>送信待ちの添付一覧（チップ列にバインドする）</summary>
    public ObservableCollection<PendingAttachmentItem> Items { get; } = new();

    /// <summary>各アイテムの削除コマンド（チップの × に配線する）</summary>
    public IRelayCommand<PendingAttachmentItem> RemoveCommand { get; }

    private AttachmentSupport _support = AttachmentSupport.None;

    private bool _isTurnInProgress;

    /// <summary>依存を注入して生成する</summary>
    /// <param name="reportStatus">エラー・案内の表示先（各ダイアログの StatusMessage へ流す）</param>
    /// <param name="shrinker">画像縮小デリゲート（省略時は縮小なし＝超過画像は拒否）</param>
    public AttachmentListViewModel(
        Action<string> reportStatus,
        ChatAttachmentFactory.ImageShrinker? shrinker = null
    )
    {
        _reportStatus = reportStatus;
        _shrinker = shrinker;
        RemoveCommand = new RelayCommand<PendingAttachmentItem>(Remove);
    }

    /// <summary>現在のエンジンが受け付けられる添付範囲（バックエンド切替で再評価する）</summary>
    public AttachmentSupport Support
    {
        get => _support;
        set
        {
            if (_support != value)
            {
                _support = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(DisabledReason));
                OnPropertyChanged(nameof(ButtonTooltip));
                OnPropertyChanged(nameof(FileDialogFilter));
                OnPropertyChanged(nameof(AllowsPdf));

                // 非対応（None）になったら送信待ちの添付をクリアして通知する
                if (value == AttachmentSupport.None && Items.Count > 0)
                {
                    Clear();
                    _reportStatus("この接続方式は添付に対応していないため、添付をクリアしました。");
                }
                else if (value == AttachmentSupport.Images && HasPdf)
                {
                    // 画像のみ対応へ切り替わったら PDF を除去する
                    RemovePdfs();
                    _reportStatus("この接続方式は PDF 添付に非対応のため、PDF を除外しました。");
                }
            }
        }
    }

    /// <summary>ターン実行中か（実行中は添付の追加・削除・クリアを禁止する）</summary>
    public bool IsTurnInProgress
    {
        get => _isTurnInProgress;
        set
        {
            if (_isTurnInProgress != value)
            {
                _isTurnInProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(DisabledReason));
                OnPropertyChanged(nameof(ButtonTooltip));
            }
        }
    }

    /// <summary>添付操作が可能か（対応エンジン・ターン非実行中）</summary>
    public bool IsEnabled => Support != AttachmentSupport.None && !IsTurnInProgress;

    /// <summary>PDF を受け付けるか（Images では拒否・ImagesAndPdf のみ許可）</summary>
    public bool AllowsPdf => Support == AttachmentSupport.ImagesAndPdf;

    /// <summary>添付が無効なときの理由（有効時は空文字）</summary>
    public string DisabledReason
    {
        get
        {
            if (Support == AttachmentSupport.None)
            {
                return "この接続方式は添付（画像・PDF）に対応していません。";
            }

            if (IsTurnInProgress)
            {
                return "応答中は添付を変更できません。";
            }

            return string.Empty;
        }
    }

    /// <summary>クリップボタンのツールチップ（無効時は理由・有効時は対応範囲の案内）</summary>
    public string ButtonTooltip
    {
        get
        {
            if (!IsEnabled)
            {
                return DisabledReason;
            }

            return AllowsPdf
                ? "画像・PDF を添付（PNG/JPEG/GIF/WebP/PDF）"
                : "画像を添付（PNG/JPEG/GIF/WebP）";
        }
    }

    /// <summary>送信待ちの添付があるか</summary>
    public bool HasAttachments => Items.Count > 0;

    /// <summary>送信待ちに PDF が含まれるか</summary>
    private bool HasPdf => Items.Any(i => i.Attachment.Kind == ChatAttachmentKind.Pdf);

    /// <summary>現在の画像枚数</summary>
    private int ImageCount => Items.Count(i => i.Attachment.Kind == ChatAttachmentKind.Image);

    /// <summary>ファイル選択ダイアログのフィルタ（対応範囲に応じ画像のみ／画像＋PDF）</summary>
    public string FileDialogFilter =>
        AllowsPdf
            ? "画像・PDF (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.pdf)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.pdf"
            : "画像 (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp";

    /// <summary>ファイルパス群を添付として取り込む（1 件ずつ検証・失敗はステータス通知）</summary>
    /// <param name="paths">追加するファイルのパス群</param>
    public void AddFiles(IEnumerable<string> paths)
    {
        if (!EnsureEditable())
        {
            return;
        }

        foreach (var path in paths)
        {
            var result = ChatAttachmentFactory.CreateFromFile(path, _shrinker);
            TryAdd(result);
        }
    }

    /// <summary>クリップボードの PNG 画像を添付として取り込む</summary>
    /// <param name="pngData">PNG エンコード済みのバイト列</param>
    /// <param name="timestamp">ファイル名に埋め込む時刻</param>
    public void AddClipboardImage(byte[] pngData, DateTime timestamp)
    {
        if (!EnsureEditable())
        {
            return;
        }

        var result = QuickER.Services.Chat.ClipboardImageAttachmentReader.CreateFromPngBytes(
            pngData,
            timestamp,
            _shrinker
        );
        TryAdd(result);
    }

    /// <summary>指定アイテムを送信待ちから除去する（ターン実行中は不可）</summary>
    /// <param name="item">除去する添付アイテム</param>
    public void Remove(PendingAttachmentItem? item)
    {
        if (item is null || IsTurnInProgress)
        {
            return;
        }

        Items.Remove(item);
        OnPropertyChanged(nameof(HasAttachments));
    }

    /// <summary>送信待ちの添付をすべてクリアする（送信完了・非対応切替時に呼ぶ）</summary>
    public void Clear()
    {
        if (Items.Count == 0)
        {
            return;
        }

        Items.Clear();
        OnPropertyChanged(nameof(HasAttachments));
    }

    /// <summary>送信用の中立添付リストを取り出す（送信の直前に呼ぶ）</summary>
    public IReadOnlyList<ChatAttachment> BuildAttachments() =>
        Items.Select(i => i.Attachment).ToArray();

    /// <summary>ユーザー吹き出しに表示する添付要約（「📎 name」を連結・添付なしなら null）</summary>
    public string? BuildSummary()
    {
        if (Items.Count == 0)
        {
            return null;
        }

        return string.Join("\n", Items.Select(i => $"📎 {i.FileName}"));
    }

    /// <summary>検証結果を送信待ちへ追加する（成功なら追加・失敗はステータス通知）</summary>
    private void TryAdd(ChatAttachmentResult result)
    {
        if (!result.Success || result.Attachment is null)
        {
            _reportStatus(result.Message);
            return;
        }

        var attachment = result.Attachment;

        // PDF は対応範囲外なら拒否する（Images では PDF を受け付けない）
        if (attachment.Kind == ChatAttachmentKind.Pdf && !AllowsPdf)
        {
            _reportStatus($"この接続方式は PDF 添付に非対応です: {attachment.FileName}");
            return;
        }

        // 画像は 1 メッセージあたりの枚数上限を超えたら拒否する
        if (
            attachment.Kind == ChatAttachmentKind.Image
            && ImageCount >= ChatAttachmentLimits.MaxImagesPerMessage
        )
        {
            _reportStatus(
                $"画像は 1 メッセージ最大 {ChatAttachmentLimits.MaxImagesPerMessage} 枚までです: {attachment.FileName}"
            );
            return;
        }

        Items.Add(new PendingAttachmentItem(attachment));
        OnPropertyChanged(nameof(HasAttachments));
    }

    /// <summary>添付操作が可能かを確認し、不可ならステータス通知して false を返す</summary>
    private bool EnsureEditable()
    {
        if (Support == AttachmentSupport.None)
        {
            _reportStatus("この接続方式は添付（画像・PDF）に対応していません。");
            return false;
        }

        if (IsTurnInProgress)
        {
            _reportStatus("応答中は添付を変更できません。");
            return false;
        }

        return true;
    }

    /// <summary>送信待ちから PDF をすべて除去する（画像のみ対応への切替時）</summary>
    private void RemovePdfs()
    {
        var pdfs = Items.Where(i => i.Attachment.Kind == ChatAttachmentKind.Pdf).ToArray();

        foreach (var pdf in pdfs)
        {
            Items.Remove(pdf);
        }

        OnPropertyChanged(nameof(HasAttachments));
    }
}
