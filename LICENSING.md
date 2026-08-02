# QuickER Licensing Guide

This page explains QuickER's license structure in plain language, with examples. It is an explanation, not the terms themselves: if anything here conflicts with [LICENSE](LICENSE) or [LICENSE-NC.md](LICENSE-NC.md), the license files control.

日本語版は [LICENSING.ja.md](LICENSING.ja.md) を参照してください。

## The short version

- QuickER is a **mixed-license repository**: most of it is MIT, and eight projects (the AI features, the code generation, the CLI, and the MCP tool-execution host) are PolyForm Noncommercial 1.0.0 **plus additional grants**.
- Thanks to those grants, the **current releases are free for everyone, including commercial use**.
- **Code that QuickER generates is yours** — no restrictions, no attribution required.
- What the grants do **not** cover: commercially **modifying** the NC-covered source code, or **redistributing** modified versions.
- Future versions may introduce paid licensing for some features; four standing commitments limit what can change (see [The future](#the-future)).

## Which license applies where

| Projects | License |
| --- | --- |
| Everything not listed below — the ER designer, import/export, DDL generation, DB import/sync, the runtime packages, and so on | [MIT](LICENSE) |
| `src/QuickER.AI`, `src/QuickER.AI.UI`, `src/QuickER.AI.Chat`, `src/QuickER.AI.Mock` — AI chat and AI mock generation | [PolyForm NC 1.0.0 + additional grants](LICENSE-NC.md) |
| `src/QuickER.CodeGen.CSharp`, `src/QuickER.CodeGen.UI` — C# code generation | [PolyForm NC 1.0.0 + additional grants](LICENSE-NC.md) |
| `src/QuickER.Cli` — the CLI | [PolyForm NC 1.0.0 + additional grants](LICENSE-NC.md) |
| `src/QuickER.Mcp.Tools` — the file-based tool-execution host for the external MCP server | [PolyForm NC 1.0.0 + additional grants](LICENSE-NC.md) |

The MCP tool-definition and stdio-hosting project `src/QuickER.Mcp` is not in the list — it is MIT.

GitHub's automatic license label can show only one license and displays "MIT"; the table above is the actual structure.

Mapped to what you actually download:

| Distribution | What applies |
| --- | --- |
| GUI (Setup.exe / Portable zip) | MIT parts + the NC-covered feature assemblies (the license files ship inside the distribution) |
| CLI (NuGet package `QuickER.Cli`) | PolyForm NC + additional grants (the license file is bundled in the package) |
| Runtime NuGet packages (`QuickER.Runtime` / `.SqlServer` / `.Sqlite` / `.EntityFrameworkCore`) | MIT |
| Code, DDL, and documents QuickER generates for you | Yours — not covered by QuickER's licenses at all |

The GUI and CLI distributions also bundle third-party components (database drivers, the template engine, and so on). Their attributions and license texts are collected in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), which ships inside those distributions.

## What you can and cannot do

| You want to... | Answer | Why |
| --- | --- | --- |
| Use the GUI or CLI at your company, for commercial development | **Yes** | Additional grants (current releases) |
| Ship generated code — including the inlined runtime — in a commercial, closed-source product | **Yes** | Generated-output grant; no attribution required |
| Reference the runtime NuGet packages from a commercial application | **Yes** | MIT |
| Modify and redistribute the MIT-covered parts, commercially | **Yes** | MIT |
| Modify the NC-covered projects for noncommercial purposes | **Yes** | PolyForm NC |
| Modify the NC-covered projects — or redistribute modified versions — for commercial purposes | **No** | The additional grants cover *use* only |
| Sell QuickER itself, or a commercial derivative built on the NC-covered projects | **No** | PolyForm NC |

## Generated code

Everything QuickER produces from your diagrams — C# code (including the inlined runtime portions), DDL scripts, documents, configuration — is your work product. [LICENSE-NC.md](LICENSE-NC.md) grants everyone a perpetual, irrevocable license to use, copy, modify, distribute, sublicense, and sell generated output for any purpose. You do not need to mention QuickER anywhere.

## The future

Future versions may introduce paid licensing for some features (for example, separately licensed Pro features). Whatever changes, four commitments stand:

1. The basic generation of Entity / EditModel / Mapper remains free permanently, including commercial use.
2. Personal and non-commercial use of the existing features remains free.
3. Rights granted for a released version are never withdrawn retroactively — the version you already use keeps its grants forever.
4. Any move to paid licensing will be announced in advance, with a transition period for existing users.

## FAQ

**GitHub shows "MIT License" for this repository. Is everything MIT?**
No. GitHub's automatic detection reads the root `LICENSE` file only. Eight projects are covered by PolyForm NC + additional grants, as listed above.

**Our legal team needs the authoritative terms. Where are they?**
[LICENSE](LICENSE) (the MIT License, verbatim) and [LICENSE-NC.md](LICENSE-NC.md) (the PolyForm Noncommercial License 1.0.0, verbatim, preceded by the QuickER-specific scope, definitions, and additional grants). Nothing else is normative — including this page.

**If a future version becomes paid, can we keep using the version we already have?**
Yes. Grants apply per released version and are never withdrawn retroactively.

**Does generated code impose any obligations on our product?**
None. No license text, no notices, no attribution.
