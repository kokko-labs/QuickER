# QuickER.Runtime

The shared runtime foundation for the C# code that QuickER (an ER diagram designer) generates. It has **zero package dependencies** (BCL only) and provides the dialect-neutral shared Repository contracts (`IRepository<TEntity, TKey>`, `ISqlExecutor`, the expression-tree query infrastructure, etc.).

## When you need it

By default, QuickER's generated code is self-contained with the runtime inlined into the output, so **this package is not required**. When you generate with `--runtime-packages` (CLI) or the option that switches the runtime to package references (GUI), the schema-independent fixed code is provided by a reference to this package instead. The required `PackageReference`s are shown in the generated code header and the CLI output.

Use it together with a dialect engine (`QuickER.Runtime.SqlServer` / `QuickER.Runtime.Sqlite`) or the EF Core components (`QuickER.Runtime.EntityFrameworkCore`). The DI registration extensions (`AddGenerated*Repositories`) are schema-dependent, so they are not included in this package and are always emitted on the generated-code side.

## Version compatibility

The package version is published in lockstep with the QuickER tool version (identical version) and is compatible within the same major version.

## License

MIT License. Together with the code that QuickER generates, you may use, modify, and distribute it with no restrictions.

<!-- TODO: 公開時に実リポジトリ URL へ差し替える -->
Details: https://github.com/QuickER/QuickER
