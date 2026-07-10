using System.Text.Json.Serialization;
using QuickER.Model;

namespace QuickER.Documents;

/// <summary>
/// ER 図の保存単位。意味モデル（<see cref="Schema"/>）と視覚情報（<see cref="Layout"/>）を分離して保持する。
/// </summary>
/// <remarks>
/// 保存 JSON は <c>{ version, schema, layout }</c> 形式。<see cref="Schema"/> は CLI・生成器・
/// エクスポータが消費する意味モデルそのもので、<see cref="Layout"/> はアプリのキャンバス表示専用。
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
    public Dictionary<Guid, EntityLayout> Layout { get; set; } = new();
}
