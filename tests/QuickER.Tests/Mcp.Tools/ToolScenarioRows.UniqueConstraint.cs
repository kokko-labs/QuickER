using QuickER.Model;
using QuickER.Tests.TestSupport;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>
    /// 一意制約ツール（set_unique_constraint / remove_unique_constraint）と、remove_column の一意制約のカスケード削除、
    /// get_diagram_summary の一意制約の表示。同じ列集合の upsert が制約の Id を温存することと、Undo の操作は 2 ホストの表では
    /// 書けないので、元のクラスのメソッドが残る
    /// </summary>
    private static IEnumerable<ToolRow> UniqueConstraintRows()
    {
        yield return CustomerUq(
                Row(
                    "unique-constraint-set-adds-single-column-constraint-sqlite",
                    "set_unique_constraint は単一列の一意制約を追加する"
                ),
                "sqlite"
            )
            .Call(
                "set_unique_constraint",
                new { table_name = "Customer", columns = new[] { "Email" } },
                e => e.Ok().Result.Contains("Email").Schema.Kind("uq", "uq Customer.- (Email)")
            );

        foreach (var dialect in new[] { "sqlserver", "sqlite" })
        {
            yield return CustomerUq(
                    Row(
                        $"unique-constraint-set-adds-composite-constraint-in-declared-order-{dialect}",
                        "set_unique_constraint は複合制約を宣言順どおり追加する"
                    ),
                    dialect
                )
                .Call(
                    "set_unique_constraint",
                    new
                    {
                        table_name = "Customer",
                        columns = new[] { "TenantId", "Code" },
                        name = "UQ_Customer_Tenant",
                    },
                    e => e.Ok().Schema.Kind("uq", "uq Customer.UQ_Customer_Tenant (TenantId,Code)")
                );

            // 列順を入れ替え・大小も変えて名前つきで再定義する（制約の Id の温存は写しに無いので元のクラスのメソッドが見る）
            yield return CustomerUq(
                    Row(
                        $"unique-constraint-set-same-column-set-upserts-{dialect}",
                        "set_unique_constraint は同じ列集合（順序・大小無視）を upsert して同一制約を保つ"
                    ),
                    dialect
                )
                .Arrange(
                    "set_unique_constraint",
                    new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
                )
                .Call(
                    "set_unique_constraint",
                    new
                    {
                        table_name = "Customer",
                        columns = new[] { "code", "tenantid" },
                        name = "UQ_Renamed",
                    },
                    e =>
                        e.Ok()
                            .Result.NotEmpty()
                            .ForHost(
                                ToolHostKind.File,
                                "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                                file => file.Result.Contains("Updated")
                            )
                            // 列順は最後の呼び出しの宣言順に従う
                            .Schema.Kind("uq", "uq Customer.UQ_Renamed (Code,TenantId)")
                );

            yield return CustomerUq(
                    Row(
                        $"unique-constraint-set-unknown-column-fails-{dialect}",
                        "set_unique_constraint は存在しない列名をエラーにし図を変更しない"
                    ),
                    dialect
                )
                .Call(
                    "set_unique_constraint",
                    new
                    {
                        table_name = "Customer",
                        columns = dialect == "sqlite"
                            ? new[] { "Email", "NoSuchColumn" }
                            : new[] { "NoSuchColumn" },
                    },
                    e => e.Fails().Result.Contains("NoSuchColumn").Schema.Kind("uq")
                );

            // GUI 由来（sqlserver）は Email 単独の制約を小文字の列名で、File 由来（sqlite）は 2 本のうち複合を逆順で特定する
            var removeRow = CustomerUq(
                    Row(
                        $"unique-constraint-remove-matching-column-set-{dialect}",
                        "remove_unique_constraint は列集合で特定した制約を削除する"
                    ),
                    dialect
                )
                .Arrange(
                    "set_unique_constraint",
                    new { table_name = "Customer", columns = new[] { "Email" } }
                );

            if (dialect == "sqlite")
            {
                removeRow
                    .Arrange(
                        "set_unique_constraint",
                        new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
                    )
                    .Call(
                        "remove_unique_constraint",
                        new { table_name = "Customer", columns = new[] { "Code", "TenantId" } },
                        e => e.Ok().Schema.Kind("uq", "uq Customer.- (Email)")
                    );
            }
            else
            {
                removeRow.Call(
                    "remove_unique_constraint",
                    new { table_name = "Customer", columns = new[] { "email" } },
                    e => e.Ok().Schema.Kind("uq")
                );
            }

            yield return removeRow;

            var unmatched = dialect == "sqlite" ? "TenantId" : "Code";

            yield return CustomerUq(
                    Row(
                        $"unique-constraint-remove-without-exact-match-fails-{dialect}",
                        "remove_unique_constraint は列集合が一致しなければエラーにする"
                    ),
                    dialect
                )
                .Arrange(
                    "set_unique_constraint",
                    new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
                )
                .Call(
                    "remove_unique_constraint",
                    new { table_name = "Customer", columns = new[] { unmatched } },
                    e =>
                        e.Fails()
                            .ForHost(
                                ToolHostKind.Gui,
                                "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                                gui =>
                                    gui.Result.Exact(
                                        string.Format(
                                            GuiStrings.Tool_UniqueConstraintNotFound,
                                            "Customer",
                                            unmatched
                                        )
                                    )
                            )
                            .ForHost(
                                ToolHostKind.File,
                                "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                                file =>
                                    file.Result.Exact(
                                        $"Table 'Customer' has no unique constraint over exactly these columns: {unmatched}."
                                    )
                            )
                            .Schema.Kind("uq", "uq Customer.- (TenantId,Code)")
                );

            yield return CustomerUq(
                    Row(
                        $"unique-constraint-remove-column-cascades-to-constraints-{dialect}",
                        "remove_column は削除列を含む一意制約を制約ごと削除する"
                    ),
                    dialect
                )
                .Arrange(
                    "set_unique_constraint",
                    new { table_name = "Customer", columns = new[] { "Email" } }
                )
                .Arrange(
                    "set_unique_constraint",
                    new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
                )
                .Call(
                    "remove_column",
                    new { table_name = "Customer", column_name = "TenantId" },
                    // TenantId を含む複合制約は消え、Email 単独の制約だけが残る（列を 1 つ失った制約を残さない）
                    e => e.Ok().Schema.Kind("uq", "uq Customer.- (Email)")
                );

            yield return CustomerUq(
                    Row(
                        $"unique-constraint-summary-lists-constraints-{dialect}",
                        "get_diagram_summary は一意制約（名前 or 合成名・構成列）を出力する"
                    ),
                    dialect
                )
                .Arrange(
                    "set_unique_constraint",
                    new { table_name = "Customer", columns = new[] { "Email" } }
                )
                .Arrange(
                    "set_unique_constraint",
                    new
                    {
                        table_name = "Customer",
                        columns = new[] { "TenantId", "Code" },
                        name = "UQ_Tenant_Code",
                    }
                )
                .Call(
                    "get_diagram_summary",
                    new { },
                    e =>
                        e.Ok()
                            .ForHost(
                                ToolHostKind.Gui,
                                "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                                gui =>
                                    gui.Result.Contains(
                                        GuiStrings.Tool_Summary_UniqueConstraintsHeader
                                    )
                            )
                            .ForHost(
                                ToolHostKind.File,
                                "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                                file => file.Result.Contains("Unique constraints:")
                            )
                            // 名前なしは DDL 生成と同じ合成名で表示する
                            .Result.Contains(
                                "UQ_Customer_Email (Email)",
                                "UQ_Tenant_Code (TenantId, Code)"
                            )
                );
        }

        yield return CustomerUq(
                Row(
                    "unique-constraint-set-omitted-name-clears-existing-name-sqlite",
                    "set_unique_constraint は名前の省略で既存の名前を解除する"
                ),
                "sqlite"
            )
            .Arrange(
                "set_unique_constraint",
                new
                {
                    table_name = "Customer",
                    columns = new[] { "Email" },
                    name = "UQ_Named",
                }
            )
            .Call(
                "set_unique_constraint",
                new { table_name = "Customer", columns = new[] { "Email" } },
                e => e.Ok().Schema.Kind("uq", "uq Customer.- (Email)")
            );

        // 付け替える名前の重複は自分以外の制約から集めて判定する（ConstraintNames.IsTaken）。同じ名前の一意制約が既に 2 つある
        // 手書きの図で、片方を同じ名前のまま付け直すと、もう片方がその名前を持つので断る。空いた名前へなら付け直せる（直す道がある）
        yield return CustomerUq(
                Row(
                    "unique-constraint-set-name-shared-with-another-constraint-fails-sqlserver",
                    "set_unique_constraint は同じ名前の一意制約がほかにもある図で、その名前のまま付け直すのを断り、空いた名前へは付け直せる"
                ),
                "sqlserver"
            )
            .Seed(
                "Customer に同じ名前 UQ_Dup の一意制約を 2 つ作る（Email と Code。ツールでも画面でも作れない手書きの図）",
                session =>
                    session.EditSchema(diagram =>
                    {
                        var customer = diagram.Entities.Single(entity =>
                            entity.TableName == "Customer"
                        );

                        foreach (var columnName in new[] { "Email", "Code" })
                        {
                            customer.UniqueConstraints.Add(
                                new UniqueConstraint
                                {
                                    Name = "UQ_Dup",
                                    ColumnIds =
                                    [
                                        customer
                                            .Columns.Single(column => column.Name == columnName)
                                            .Id,
                                    ],
                                }
                            );
                        }
                    })
            )
            .Call(
                "set_unique_constraint",
                new
                {
                    table_name = "Customer",
                    columns = new[] { "Email" },
                    name = "UQ_Dup",
                },
                e =>
                    e.Fails()
                        .Unchanged()
                        .ForHost(
                            ToolHostKind.Gui,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            gui =>
                                gui.Result.Exact(
                                    string.Format(
                                        GuiStrings.Tool_UniqueConstraintNameTaken,
                                        "UQ_Dup"
                                    )
                                )
                        )
                        .ForHost(
                            ToolHostKind.File,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            file =>
                                file.Result.Exact(
                                    "The constraint name 'UQ_Dup' is already used in this diagram (constraint names must be unique across foreign keys, primary keys and unique constraints, ignoring case). Choose another name, or omit name to get a unique one automatically."
                                )
                        )
            )
            .Call(
                "set_unique_constraint",
                new
                {
                    table_name = "Customer",
                    columns = new[] { "Email" },
                    name = "UQ_Free",
                },
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "uq",
                            "uq Customer.UQ_Free (Email)",
                            "uq Customer.UQ_Dup (Code)"
                        )
            );

        yield return CustomerUq(
                Row(
                    "unique-constraint-set-invalid-columns-fail-sqlite",
                    "set_unique_constraint は列の重複指定・空配列をエラーにする"
                ),
                "sqlite"
            )
            .Call(
                "set_unique_constraint",
                new { table_name = "Customer", columns = new[] { "Email", "email" } },
                e => e.Fails().Schema.Kind("uq")
            )
            .Call(
                "set_unique_constraint",
                new { table_name = "Customer", columns = Array.Empty<string>() },
                e => e.Fails().Schema.Kind("uq")
            )
            .Call(
                "set_unique_constraint",
                new { table_name = "Customer" },
                e => e.Fails().Schema.Kind("uq")
            );
    }

    /// <summary>Customer(CustomerId PK / Email / TenantId / Code) の図（Email 以降は nvarchar(100)・NOT NULL）</summary>
    private static ToolRow CustomerUq(ToolRow row, string dialect)
    {
        row.Dialect(dialect)
            .Arrange("add_entity", new { table_name = "Customer" })
            .Arrange(
                "add_column",
                ColumnArgs("Customer", "CustomerId", "int", pk: true, nullable: false)
            );

        foreach (var column in new[] { "Email", "TenantId", "Code" })
        {
            row.Arrange(
                "add_column",
                ColumnArgs("Customer", column, "nvarchar(100)", nullable: false)
            );
        }

        return row;
    }
}
