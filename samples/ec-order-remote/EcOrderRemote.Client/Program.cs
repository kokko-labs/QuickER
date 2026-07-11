using System.Text;
using EcOrderRemoteSample.Generated;
using Microsoft.Extensions.DependencyInjection;

// QuickER の「リモートサービス生成（GenerateRemoteServices）」で図から生成した HTTP クライアント実装だけを使い、
// 別プロセスのサーバー（EcOrderRemote.Server）を HTTP + JSON 越しに呼び出すサンプルクライアント。
// 呼び出しコードは DB 直結時とまったく同じインターフェイス（I{Entity}RemoteRepository）で書けており、
// 違いは DI 登録が AddGeneratedHttpRemoteRepositories 1 行に変わっているだけ、という点が見どころ。

// 日本語出力の文字化けを避けるため標準出力を UTF-8 にする（リダイレクト時の失敗は無視）
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // 出力がリダイレクトされている場合などは設定できないが、致命的ではないため無視する
}

// サーバーのベース URL は第 1 引数で差し替え可能（サーバーと同じ値を渡すこと）。既定はサーバー側と同じ固定ポート。
var baseUrl = args.FirstOrDefault() ?? "http://127.0.0.1:5210";

// 生成された HTTP クライアント実装を DI へ登録する。baseAddress にはサーバーの prefix（/quicker）まで含める。
// ここだけを AddGeneratedRepositories（DB 直結）へ差し替えれば、同じ呼び出しコードがローカル実行になる。
using var provider = new ServiceCollection()
    .AddGeneratedHttpRemoteRepositories($"{baseUrl}/quicker")
    .BuildServiceProvider();

var customers = provider.GetRequiredService<ICustomerRemoteRepository>();
var products = provider.GetRequiredService<IProductRemoteRepository>();
var orders = provider.GetRequiredService<IOrderRemoteRepository>();

// サーバーの起動を待つ（別プロセスで立ち上がるため、接続確立まで数百 ms かかることがある）。
// 最大 30 回×500ms リトライし、その間の接続失敗（HttpRequestException）は吸収する。
await WaitForServerAsync(customers);

// ---- 1. 顧客・商品のマスタ登録（InsertAsync が HTTP 越しに機能する） ----
await customers.InsertAsync(
    new CustomerEntity
    {
        CustomerId = 1,
        Name = "山田 太郎",
        Email = "taro@example.com",
    }
);
await customers.InsertAsync(
    new CustomerEntity
    {
        CustomerId = 2,
        Name = "鈴木 花子",
        Email = null,
    }
);

await products.InsertAsync(
    new ProductEntity
    {
        ProductId = 100,
        Name = "コーヒー豆 200g",
        UnitPrice = 980m,
    }
);
await products.InsertAsync(
    new ProductEntity
    {
        ProductId = 101,
        Name = "マグカップ",
        UnitPrice = 1500m,
    }
);

Console.WriteLine("[1] 顧客 2 件・商品 2 件を HTTP 越しに登録しました。");
Check((await customers.GetAllAsync()).Count, 2, "顧客件数");
Check((await products.GetAllAsync()).Count, 2, "商品件数");
Console.WriteLine();

// ---- 2. 注文グラフのまとめて保存（MarkAdded → SaveAsync）と、保存後の RowState 確定 ----
// 注文 1000 は注文明細 2 行を持つグラフ、注文 1001 は明細なし。どちらも顧客 1 の注文。
var order1000 = new OrderEntity
{
    OrderId = 1000,
    CustomerId = 1,
    OrderedAt = new DateTime(2026, 7, 7, 13, 47, 9, DateTimeKind.Unspecified),
    Memo = "初回注文",
};
order1000.MarkAdded();

var line1 = new OrderLineEntity
{
    OrderLineId = 5000,
    OrderId = 1000,
    ProductId = 100,
    Quantity = 2,
    UnitPrice = 980m,
};
line1.MarkAdded();

var line2 = new OrderLineEntity
{
    OrderLineId = 5001,
    OrderId = 1000,
    ProductId = 101,
    Quantity = 1,
    UnitPrice = 1500m,
};
line2.MarkAdded();

order1000.OrderLines.Add(line1);
order1000.OrderLines.Add(line2);

var order1001 = new OrderEntity
{
    OrderId = 1001,
    CustomerId = 1,
    OrderedAt = new DateTime(2026, 7, 8, 9, 12, 0, DateTimeKind.Unspecified),
    Memo = "明細なしの注文",
};
order1001.MarkAdded();

// 複数の集約ルートを 1 度のリクエストでまとめて保存する（注文 1000＝3 レコード＋注文 1001＝1 レコード）
var savedCount = await orders.SaveAsync(new[] { order1000, order1001 });
Console.WriteLine(
    $"[2] 注文 2 件（うち 1 件は明細 2 行のグラフ）を HTTP 越しにまとめて保存しました（保存レコード数: {savedCount}）。"
);
Check(savedCount, 4, "グラフ保存レコード数");

// 直結（EntityGraphSaver.AcceptChanges）と同じく、保存成功後はローカルの RowState が Unchanged に確定する。
// この確定が HTTP 越しでも成立するのが本サンプルの要点（呼び出し側の意味論が直結時と変わらない）。
Check(order1000.HasChanges, false, "保存後の注文 1000 の RowState 確定");
Check(order1001.HasChanges, false, "保存後の注文 1001 の RowState 確定");
Console.WriteLine();

// ---- 3. 名前付きクエリ（DSL 条件＋ページング）のリモート転送 ----
// GetByCustomer は「顧客IDで注文を注文ID降順に検索（ページング付き）」の DSL クエリ。
// 顧客 1 の注文は 1001, 1000（降順）。take:1, skip:0 → 先頭 1001、skip:1 → 2 件目 1000。
var firstPage = await orders.GetByCustomerAsync(1, take: 1, skip: 0);
Check(firstPage.Count, 1, "GetByCustomer(take:1, skip:0) の件数");
Check(firstPage[0].OrderId, 1001, "注文ID降順の先頭");

var secondPage = await orders.GetByCustomerAsync(1, take: 1, skip: 1);
Check(secondPage.Count, 1, "GetByCustomer(take:1, skip:1) の件数");
Check(secondPage[0].OrderId, 1000, "注文ID降順の 2 件目");

Console.WriteLine("[3] 名前付きクエリ（DSL＋ページング）を HTTP 越しに実行しました:");
Console.WriteLine(
    $"    先頭ページ 注文ID={firstPage[0].OrderId} / 2 ページ目 注文ID={secondPage[0].OrderId}"
);
Console.WriteLine();

// ---- 4. 名前付きクエリ（射影 DTO）のリモート転送 ----
// GetSummaries は注文サマリー（注文ID・注文日時・備考）を注文ID降順で返す射影クエリ。
// 射影 DTO（OrderSummaryRow）が JSON でクライアントまで届くことを確認する。
var summaries = await orders.GetSummariesAsync(1);
Check(summaries.Count, 2, "注文サマリーの件数");
Check(summaries[0].OrderId, 1001, "サマリー先頭の注文ID（降順）");
Check(summaries[1].OrderId, 1000, "サマリー 2 件目の注文ID（降順）");
Check(summaries[0].Memo, "明細なしの注文", "サマリー先頭の備考");
Check(summaries[1].Memo, "初回注文", "サマリー 2 件目の備考");

Console.WriteLine("[4] 名前付きクエリ（射影 DTO）を HTTP 越しに取得しました:");

foreach (var row in summaries)
{
    Console.WriteLine(
        $"    注文ID={row.OrderId} 注文日時={row.OrderedAt:yyyy-MM-dd HH:mm:ss} 備考={row.Memo}"
    );
}

Console.WriteLine();

// ---- 5. SaveConflictException の HTTP 409 経由の型復元 ----
// 存在しない注文を更新保存（insertWhenUpdateMissing=false）すると、サーバー側で楽観的競合となり
// SaveConflictException が投げられる。これは HTTP 409＋構造化 JSON を経て、クライアント側でも
// 同じ SaveConflictException として復元される＝直結時とまったく同じ catch が書ける、という実演。
var missing = new OrderEntity
{
    OrderId = 9999,
    CustomerId = 1,
    OrderedAt = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Unspecified),
    Memo = "存在しない注文の更新",
};
missing.MarkUpdated();

var conflictCaught = false;

try
{
    await orders.SaveAsync(missing);
}
catch (SaveConflictException)
{
    // サーバー側の例外型が HTTP 越しに復元された（直結時と同じ catch）
    conflictCaught = true;
}

Check(conflictCaught, true, "SaveConflictException の HTTP 409 経由の型復元");
Console.WriteLine(
    "[5] 存在しない注文の更新保存で SaveConflictException を HTTP 越しに捕捉しました。"
);
Console.WriteLine();

// ---- 6. 更新（UpdateAsync）と削除（DeleteAsync）が HTTP 越しに機能する ----
var toUpdate = await customers.GetByIdAsync(1);
toUpdate!.Name = "山田 太郎（改名）";
var updated = await customers.UpdateAsync(toUpdate);
Check(updated, true, "顧客の更新");

var reloaded = await customers.GetByIdAsync(1);
Console.WriteLine($"[6-a] 顧客 1 を HTTP 越しに更新しました: {reloaded!.Name}");
Check(reloaded.Name, "山田 太郎（改名）", "更新後の顧客名");

// 注文を持たない顧客 2 を削除する（主キー指定の DeleteAsync）
var deleted = await customers.DeleteAsync(2);
Check(deleted, true, "顧客 2 の削除");

Console.WriteLine("[6-b] 顧客 2 を HTTP 越しに削除しました。");
Check((await customers.GetAllAsync()).Count, 1, "削除後の顧客件数");
Console.WriteLine();

Console.WriteLine("すべてのシナリオが成功しました。");
return 0;

// サーバーが応答するまで GetAllAsync を試行し、接続失敗（HttpRequestException）は吸収してリトライする。
// 別プロセスのサーバー起動待ち（CI 含む）を想定した小さなヘルパー。
static async Task WaitForServerAsync(ICustomerRemoteRepository customers)
{
    const int maxAttempts = 30;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await customers.GetAllAsync();
            return;
        }
        catch (HttpRequestException)
        {
            // まだサーバーが受け付けていない。少し待って再試行する
            await Task.Delay(500);
        }
    }

    throw new InvalidOperationException(
        $"サーバーへ接続できませんでした（{maxAttempts} 回試行）。サーバー（EcOrderRemote.Server）が起動しているか確認してください。"
    );
}

// 期待値と実測値が一致しなければ例外を投げて終了コードを非 0 にする（CI で検知できるようにする）小さなヘルパー。
static void Check<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        throw new InvalidOperationException(
            $"検証失敗（{label}）: 期待値={expected} 実測値={actual}"
        );
    }
}
