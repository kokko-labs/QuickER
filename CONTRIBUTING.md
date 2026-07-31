# Contributing to QuickER

*English | [日本語](CONTRIBUTING.ja.md)*

QuickER is a solo-developed OSS project. Issues and pull requests are welcome, but support is **best-effort** (no promised response times) and covers **the latest version only**.

## Issues (bug reports / feature requests)

- Please use the issue templates (bug report / feature request). For questions or discussions that don't fit the templates, a blank issue is fine
- Japanese and English are both welcome
- For bug reports, environment information (version, how you installed it, OS, .NET runtime) and reproduction steps are prerequisites for investigation
- **Do not report vulnerabilities in public Issues.** Follow the private reporting procedure in [SECURITY.md](SECURITY.md)

## Pull requests

- **Before opening a PR, please discuss your plan in an Issue first** (discussion-first policy). Small fixes such as typo corrections don't need prior discussion
- Large PRs without prior discussion may be closed if they don't fit the project direction

### Development conventions

- Development environment: Windows + .NET 10 SDK (the tests depend on WPF). With Docker running, the real-DB integration tests also run (they are skipped automatically otherwise)
- Comments and commit messages are written in Japanese
- Run `csharpier format .` after code changes (global tool)
- Make sure `dotnet test QuickER.slnx` is green
- If you change the generation templates (`Templates/CSharpRuntime/*.scriban`), the checked-in fixtures etc. must be regenerated. The following script performs regenerate → verify → show diff:

  ```powershell
  ./scripts/regen-fixtures.ps1
  ```

  Without the script, regenerate with the following one-liner, then run the same tests again without the environment variable and confirm they are green:

  ```powershell
  $env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
  ```

- For changes that affect users, add a one-line entry to the Unreleased section of the changelog — **both** [CHANGELOG.md](CHANGELOG.md) (English) and [CHANGELOG.ja.md](CHANGELOG.ja.md) (Japanese). Not needed for internal refactoring or test-only changes

The architecture and the invariants that break silently (not caught by the build or the type checker) are documented in [CLAUDE.md](CLAUDE.md).

## License and rights handling for contributions

- This repository uses **MIT** and **PolyForm Noncommercial 1.0.0** on a per-project basis (see [LICENSE-NC.md](LICENSE-NC.md) for the covered projects, and [LICENSING.md](LICENSING.md) for the provisioning policy)
- By submitting code, you agree that it will be published under the current license of the project it is merged into
- For contributions to the PolyForm NC projects, you additionally grant the author (the repository owner) the right to offer commercial licenses for software containing your code, and to change its license in the future (including making it free of charge) — this arrangement keeps external contributions from blocking future changes to the provisioning policy

## Versioning

- We follow [Semantic Versioning](https://semver.org/). All distributables (the GUI, the CLI, and the 4 runtime packages) are versioned in lockstep, managed via `VersionPrefix` in `Directory.Build.props`
- Version bump rules during 0.x:
  - **minor** (0.2.0 → 0.3.0): new features and breaking changes (changes to the Repository API or to the signatures/structure of the generated code, package dependency changes)
  - **patch** (0.2.0 → 0.2.1): bug fixes only. A fix that doesn't break calling code counts as a patch, even if the internals of the generated code change
- 1.0.0 will be declared when we judge that compatibility of the generated code and the Repository API can be promised

## Release procedure (for maintainers)

Releases always ship **all distributables together** (the 5 NuGet packages, the GUI distributables (Velopack: full / lite × Setup.exe / Portable zip), and the git tag `v{version}`). Timing is discretionary; no cadence is promised.

1. Review the Unreleased section of the changelog, decide the version number (minor / patch per the rules above), and finalize the entry with a date — in **both [CHANGELOG.md](CHANGELOG.md) and [CHANGELOG.ja.md](CHANGELOG.ja.md)**
2. Update `VersionPrefix` in `Directory.Build.props` and commit it together with the changelog finalization as a single commit
3. Run publish.yml (the 5 NuGet packages) via workflow_dispatch (confirm with dry_run first, then run for real)
4. Run release.yml (publishes the GUI distributables and creates the git tag) via workflow_dispatch
5. Copy the changelog content for the version into the GitHub Release notes
