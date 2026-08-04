# QuickER.Runtime.Sqlite

The **SQLite dialect engine** for the QuickER Repository, part of the runtime for the C# code that QuickER (an ER diagram designer) generates. It implements the dialect-neutral contracts of `QuickER.Runtime` for SQLite (multi-query IncludeLoader, `LIMIT/OFFSET`, `strftime`, etc.). Its only dependencies are `Microsoft.Data.Sqlite` (and the pinned SQLitePCLRaw).

## When you need it

By default, QuickER's generated code is self-contained with the runtime inlined into the output, so **this package is not required**. When you generate with `--use-runtime-packages` (CLI) or the option that switches the runtime to package references (GUI) and include SQLite among the target DBs for the QuickER Repository, reference it together with `QuickER.Runtime`. The required `PackageReference`s are shown in the generated code header and the CLI output.

The DI registration extensions (`AddGeneratedSqliteRepositories`, etc.) are schema-dependent, so they are not included in this package and are always emitted on the generated-code side.

## Version compatibility

The package version is published in lockstep with the QuickER tool version (identical version) — reference the same version as the tool that generated your code. During 0.x, minor version bumps may include breaking changes (see the versioning policy in the repository's CONTRIBUTING.md).

## License

MIT License (the package itself). The code that QuickER generates for you is your own work product, with no license obligations at all.

Details: https://github.com/kokko-labs/QuickER
