# Copilot Instructions

## プロジェクト ガイドライン
- コードには他の既存コードと同様、分かりやすい日本語コメント（特にビヘイビアや主要メソッド）を付ける。
- コード修正時は、共通化・シンプル化を優先し、特殊なパターンの条件分岐や処理をむやみに増やさない。
- C# コードの `if` / `for` / `foreach` / `while` / `switch` / `try-catch-finally` ブロックの前後には、CSharpier が許容する空行（1行）を入れて人間が読みやすくする。
  - ブロック直前が `{`・空行・コメント行・属性行（`[`）・`} else` / `} catch` / `} finally` の場合は挿入不要。
  - ブロック直後が `}`・空行・`)`・`]`・`else`・`catch`・`finally`・`;` の場合は挿入不要。
- `switch` 文の `case` ブロック間（`return` / `break` / `throw` で終わる case の後、次の `case` の前）にも空行（1行）を入れる。
- コード修正後は必ず `csharpier format` を実行して整形する（空行の最終判断は CSharpier の挙動を優先する）。
- CSharpier はグローバルにインストールされており、ターミナルから `csharpier format .` を実行できる。
- ビルド警告は可能な限り解消する。
- 実装後に各機能のコードレビューとテストを行い、DB接続テストが必要な場合は localhost / TestDB を使用する。