# QuickER.Runtime

The shared runtime for the C# code that [QuickER](https://github.com/kokko-labs/QuickER) generates — an ER diagram designer for .NET. It holds the dialect-neutral Repository contracts and the foundation they build on. Targets .NET 10, with no package dependencies of its own.

## When you need it

By default QuickER inlines the runtime into the code it generates, so the output is self-contained and **this package is not required**. Generating with `--use-runtime-packages` leaves the runtime out of the output, and your project references this package instead:

```sh
dotnet add package QuickER.Runtime
```

The generated code header and the CLI output list every reference a given output needs. Package versions are published in lockstep with the QuickER tool, so reference the same version as the tool that generated your code.

## Documentation and feedback

- [Using the generated code](https://github.com/kokko-labs/QuickER/blob/main/docs/code-generation.md)
- [Report an issue](https://github.com/kokko-labs/QuickER/issues)

## License

MIT. The code QuickER generates is your own work product and carries no license obligations.
