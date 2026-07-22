using System.Text.Json.Serialization;
using QuickER.Model;

namespace QuickER.Documents;

/// <summary>
/// ER 図の保存単位。意味モデル（<see cref="Schema"/>）と視覚情報（<see cref="Layout"/>）を分離して保持する。
/// </summary>
/// <remarks>
/// 保存 JSON は <c>{ version, schema, layout }</c> 形式。<see cref="Schema"/> は CLI・生成器・
/// エクスポータが消費する意味モデルそのもので、<see cref="Layout"/> はアプリのキャンバス表示専用。
/// <see cref="Layout"/> を <c>null</c> にすると配置情報を持たない「スキーマのみ文書」（エクスポート用）となり、
/// 直列化設定（WhenWritingNull）により layout キー自体が JSON へ出力されない。読み込み側は layout の
/// 欠落・空を検知して全体を自動整列するため、この形式のファイルもそのまま開ける（可逆）。
/// </remarks>
public sealed class DiagramDocument
{
    /// <summary>現在の保存フォーマットバージョン（初回リリースを 1 とする）</summary>
    public const int CurrentVersion = 1;

    /// <summary>保存フォーマットのバージョン</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>このバージョンが対応するより新しいフォーマットで保存された文書かどうか</summary>
    /// <remarks>
    /// 未知のプロパティはデシリアライズで黙って無視されるため、新しいフォーマットの文書を
    /// そのまま読み書きすると未対応のデータが静かに失われる。読み込み側はこのフラグを見て
    /// ユーザーへ警告すること（GUI は続行確認、CLI は標準エラーへの警告出力）。
    /// </remarks>
    [JsonIgnore]
    public bool IsNewerFormat => Version > CurrentVersion;

    /// <summary>意味モデル（エンティティ・リレーション）</summary>
    public ErDiagram Schema { get; set; } = new();

    /// <summary>エンティティ ID → レイアウト（視覚情報）のサイドカー</summary>
    /// <remarks>
    /// <c>null</c> はスキーマのみ文書（エクスポート用）を表し、直列化設定（WhenWritingNull）により
    /// layout キー自体が JSON へ出力されない。初期化子（<c>new()</c>）は維持するため、layout キーを
    /// 欠いた JSON をデシリアライズしても非 null（空辞書）になる（＝null になるのは明示代入時のみ）。
    /// </remarks>
    public Dictionary<Guid, EntityLayout>? Layout { get; set; } = new();
}
