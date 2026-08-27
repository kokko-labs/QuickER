# Tutorial (from design to running code)

*English | [日本語](getting-started.ja.md)*

Using the bundled EC order sample, this tutorial walks through one full loop: edit the diagram → output the DDL → generate the code → run the application. It uses a SQLite file database, so no external database is required.

## Prerequisites

- Windows
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (needed to run the sample and the CLI)

## 1. Install QuickER

Get the Setup.exe (installer) or the Portable zip (extract and run `QuickER.exe`) from [GitHub Releases](https://github.com/kokko-labs/QuickER/releases). For the difference between the channels, see [the Install section of the README](../README.md#install).

To run from source:

```powershell
git clone https://github.com/kokko-labs/QuickER.git
cd QuickER
dotnet run --project src/QuickER.Gui
```

## 2. Run the sample as-is first

The sample lives in the repository, so clone it first (you need this even if you installed with the installer).

```powershell
git clone https://github.com/kokko-labs/QuickER.git
cd QuickER
```

The repository contains the DDL and the C# code generated from the diagram (`EcOrder.json`), already checked in. Run it without changing anything.

```powershell
dotnet run --project samples/ec-order/EcOrderSample
```

At startup, the SQLite file DB is recreated from the DDL, and scenarios such as registration, graph save, and querying run in order. If it ends with "All scenarios succeeded.", it worked.

## 3. Open the diagram

Launch QuickER and open `samples/ec-order/EcOrder.json` with Ctrl+O. You will see an ER diagram of four tables: customers, products, orders, and order_lines.

## 4. Edit the diagram

As an experiment, add one column to the products table.

1. Click `products` on the canvas to select it
2. Add a column with the "+" button at the top right of the "Columns" grid in the properties panel
3. Name the column `stock`, set the type to `INTEGER`, and check NULL (keeping it nullable avoids affecting the existing sample code)
4. Save the diagram with Ctrl+S

For the editing operations in detail, see [ER diagram editing](er-editor.md).

## 5. Output the DDL

Choose the DDL from "Export" on the toolbar and save it over `samples/ec-order/EcOrder.sql`. The sample app recreates the database from this DDL every time it starts, so this alone propagates the schema change to the DB.

## 6. Generate the code

Regenerate the C# code from the diagram with the CLI.

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order/EcOrder.json `
  --out samples/ec-order/EcOrderSample/Generated `
  --provider sqlite `
  --config samples/ec-order/quicker.json
```

`Generated/EcOrder.g.cs` is updated, and you can confirm that the `products` Entity and EditModel gained a property for `stock`. For generating from the GUI's code-generation dialog and for the generation options in detail, see the [CLI reference](cli.md) and [Using the generated code](code-generation.md).

## 7. Run it again

```powershell
dotnet run --project samples/ec-order/EcOrderSample
```

With the schema and the code now including the new column, the same scenarios succeed as before. You fixed the diagram in one place, and the DDL, the Entity, and the EditModel all followed.

## Next steps

- [ER diagram editing](er-editor.md) — the editor feature reference
- [Database round-tripping](database.md) — importing from an existing DB and diff sync
- [Import and export](import-export.md) — interop with DBML / Mermaid / definition documents
- [Configuring AI chat](ai-chat.md) — generating diagrams in conversation
- [Why QuickER uses the ER model as the source of truth](overview.md) — the background of this workflow
