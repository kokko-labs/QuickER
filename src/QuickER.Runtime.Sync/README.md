# QuickER.Runtime.Sync

The **bidirectional sync engine** for the C# code that QuickER (an ER diagram designer) generates. It keeps a local SQLite database in step with a SQL Server one, with the server as the source of truth: offline edits recorded in a single local journal table are replayed against the server, and the server's changes are pulled down differentially by scanning its `rowversion` columns. It works entirely through the dialect-neutral contracts of `QuickER.Runtime` (`IRepository`, `ISqlExecutor`, `ConcurrencyMode`), so it touches neither ADO nor EF Core, and it has **no NuGet dependencies at all** (BCL only).

The server gets no extra schema. The resume point is derived from the highest mirrored version among the local rows rather than stored, so there is no bookkeeping row that can disagree with the data; only the local database gets one shared journal table, created on first use.

Conflicts are never resolved silently: by default a local change that collides with the server stays in the journal and comes back in the result with both sides of the disagreement attached.

## When you need it

By default, QuickER's generated code is self-contained with the runtime inlined into the output, so **this package is not required**. When you generate with `--use-runtime-packages` (CLI) or the option that switches the runtime to package references (GUI) and enable sync support (`GenerateSyncSupport`), reference it together with `QuickER.Runtime`, `QuickER.Runtime.SqlServer`, and `QuickER.Runtime.Sqlite`. The required `PackageReference`s are shown in the generated code header and the CLI output.

The per-table descriptors, the journaling decorators, the direct differential sources, and the DI registration (`AddGeneratedSyncSupport`) are schema-dependent, so they are not included in this package and are always emitted on the generated-code side.

## Version compatibility

The package version is published in lockstep with the QuickER tool version (identical version) — reference the same version as the tool that generated your code. During 0.x, minor version bumps may include breaking changes (see the versioning policy in the repository's CONTRIBUTING.md).

## License

MIT License (the package itself). The code that QuickER generates for you is your own work product, with no license obligations at all.

Details: https://github.com/kokko-labs/QuickER
