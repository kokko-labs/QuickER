# QuickER.Runtime.EntityFrameworkCore

The **shared components of the EF Core Repository**, part of the runtime for the C# code that QuickER (an ER diagram designer) generates. It provides the `TContext : DbContext` generic EF Repository base and value-object translation plugins, implementing the dialect-neutral contracts of `QuickER.Runtime` on EF Core. Its only dependency is `Microsoft.EntityFrameworkCore.Relational` (no ADO or DI dependencies).

## When you need it

By default, QuickER's generated code is self-contained with the runtime inlined into the output, so **this package is not required**. When you generate with `--runtime-packages` (CLI) or the option that switches the runtime to package references (GUI) and enable EF Core generation (GenerateEfCore), reference it together with `QuickER.Runtime`. The required `PackageReference`s are shown in the generated code header and the CLI output.

The concrete `QuickErDbContext`, the Fluent configuration, the per-entity `EfCore{Entity}Repository`, and the DI registration extension (`AddGeneratedEfCoreRepositories`) are schema-dependent, so they are not included in this package and are always emitted on the generated-code side.

## Version compatibility

The package version is published in lockstep with the QuickER tool version (identical version) and is compatible within the same major version.

## License

MIT License. Together with the code that QuickER generates, you may use, modify, and distribute it with no restrictions.

Details: https://github.com/kokko-labs/QuickER
