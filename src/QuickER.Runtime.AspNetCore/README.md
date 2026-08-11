# QuickER.Runtime.AspNetCore

The **fixed server-side engine** for the remote endpoints, part of the runtime for the C# code that QuickER (an ER diagram designer) generates. It provides the engine behind the generated `MapGeneratedRemoteEndpoints` — request reading and Minimal API mapping, classification of failures into 400 / 409 / 500, the error-detail exposure policy with correlation ids, and the binary streaming helpers — implementing the dialect-neutral remote contracts of `QuickER.Runtime` on ASP.NET Core. It has **no NuGet dependencies**; it references the `Microsoft.AspNetCore.App` shared framework instead.

## When you need it

By default, QuickER's generated code is self-contained with the runtime inlined into the output, so **this package is not required**. When you generate with `--use-runtime-packages` (CLI) or the option that switches the runtime to package references (GUI) and enable remote service generation (`GenerateRemoteServices`), reference it together with `QuickER.Runtime`. The required `PackageReference`s are shown in the generated code header and the CLI output.

Reference it from the **project that hosts the server file** (`{base name}.RemoteServer.g.cs`), which must be a web project that can resolve the ASP.NET Core shared framework — either one whose SDK is `Microsoft.NET.Sdk.Web`, or one that declares `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. The client side (`Http{Entity}RemoteRepository`) needs only `QuickER.Runtime`, so a client project does not reference this package.

The per-entity endpoints (`GeneratedRemoteEndpoints`, including `MapGeneratedRemoteEndpoints` and the `OnServerError` partial hook) are schema-dependent, so they are not included in this package and are always emitted on the generated-code side.

## Version compatibility

The package version is published in lockstep with the QuickER tool version (identical version) — reference the same version as the tool that generated your code. During 0.x, minor version bumps may include breaking changes (see the versioning policy in the repository's CONTRIBUTING.md).

## License

MIT License (the package itself). The code that QuickER generates for you is your own work product, with no license obligations at all.

Details: https://github.com/kokko-labs/QuickER
