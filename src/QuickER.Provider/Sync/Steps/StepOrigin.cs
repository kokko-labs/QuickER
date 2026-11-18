using System.Collections.Generic;

namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 計画の文（<see cref="SyncStep"/>）がどこから来たか
/// </summary>
internal abstract record StepOrigin;

/// <summary>
/// 利用者が選んだ項目から来た文
/// </summary>
/// <param name="Item">
/// 元の選んだ項目。外部キー・一意制約の作り直しを展開した削除と追加は、どちらも元の作り直しの項目を持つ。主キーの変更の解除と付与は、
/// どちらも元の主キーの変更の項目を持つ
/// </param>
internal sealed record SelectedOrigin(SchemaDiffItem Item) : StepOrigin;

/// <summary>
/// 利用者の選んでいない、計画器が足した文
/// </summary>
/// <param name="Reason">足した理由</param>
/// <param name="Description">文の説明（計画を読み取り用の形にしたときの項目の説明。足した側が組み立てる）</param>
internal sealed record ImplicitOrigin(ImplicitStepReason Reason, string Description) : StepOrigin;

/// <summary>
/// 複数の項目をまとめて 1 つにした文（テーブル再構築）
/// </summary>
/// <param name="Items">まとめた項目（利用者が選んだ項目と、計画器が足した項目）</param>
internal sealed record MergedOrigin(IReadOnlyList<SchemaDiffItem> Items) : StepOrigin;
