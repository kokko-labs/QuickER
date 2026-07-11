-- QuickER によって自動生成された DDL
-- 生成日時: 2026-07-07 10:23:50

-- customers: 顧客。注文の発注元となる購入者マスタ
CREATE TABLE "customers" (
    "customer_id" int NOT NULL,
    -- customer_id: 顧客ID（主キー。アプリ側で採番）
    "name" nvarchar(50) NOT NULL,
    -- name: 顧客名
    "email" nvarchar(100) NULL,
    -- email: 連絡先メールアドレス（任意）
    CONSTRAINT "PK_customers" PRIMARY KEY ("customer_id")
);

-- products: 商品マスタ。販売対象となる商品の定義
CREATE TABLE "products" (
    "product_id" int NOT NULL,
    -- product_id: 商品ID（主キー。アプリ側で採番）
    "name" nvarchar(50) NOT NULL,
    -- name: 商品名
    "unit_price" decimal(10,2) NOT NULL,
    -- unit_price: 商品マスタ上の販売単価
    CONSTRAINT "PK_products" PRIMARY KEY ("product_id")
);

-- orders: 注文ヘッダ。1 顧客の 1 回の注文を表す
CREATE TABLE "orders" (
    "order_id" int NOT NULL,
    -- order_id: 注文ID（主キー。アプリ側で採番）
    "customer_id" int NOT NULL,
    -- customer_id: 発注した顧客ID（customers への外部キー）
    "ordered_at" datetime2 NOT NULL,
    -- ordered_at: 注文日時
    "memo" nvarchar(100) NULL,
    -- memo: 注文に添える備考（任意）
    CONSTRAINT "PK_orders" PRIMARY KEY ("order_id"),
    CONSTRAINT "FK_orders_customers" FOREIGN KEY ("customer_id") REFERENCES "customers" ("customer_id") ON DELETE CASCADE
);

-- order_lines: 注文明細。注文と商品を多対多で結ぶ明細行
CREATE TABLE "order_lines" (
    "order_line_id" int NOT NULL,
    -- order_line_id: 注文明細ID（主キー。アプリ側で採番）
    "order_id" int NOT NULL,
    -- order_id: 所属する注文ID（orders への外部キー）
    "product_id" int NOT NULL,
    -- product_id: 対象の商品ID（products への外部キー）
    "quantity" int NOT NULL,
    -- quantity: 注文数量
    "unit_price" decimal(10,2) NOT NULL,
    -- unit_price: 注文時単価（商品マスタの改定に影響されないよう注文行に保持）
    CONSTRAINT "PK_order_lines" PRIMARY KEY ("order_line_id"),
    CONSTRAINT "FK_order_lines_orders" FOREIGN KEY ("order_id") REFERENCES "orders" ("order_id") ON DELETE CASCADE,
    CONSTRAINT "FK_order_lines_products" FOREIGN KEY ("product_id") REFERENCES "products" ("product_id")
);

