# Configuring AI chat

*English | [日本語](ai-chat.ja.md)*

From "AI Chat" on the toolbar, you can generate and edit ER diagrams through conversation (e.g., "Design the tables needed for order management on an e-commerce site", "Add a shipping address to `orders`"). With the same connection settings you can also use "AI Mock Generation", which generates web mockup screens from the current ER diagram (see [AI mock generation](#ai-mock-generation) below).

This in-app chat edits the diagram currently open in the GUI. To instead let an external AI agent (Claude Code, Codex, and so on) drive QuickER as part of its own workflow, use the [MCP server](mcp.md).

## Connection methods

The chat window has four connection tabs, covering six connection methods in total: the API Key tab (OpenAI API / Anthropic API / an OpenAI-compatible local LLM), the Codex tab, the Claude Code tab, and the Copilot tab.

### 1. API key (the "API Key" tab)

| Provider | What you need |
|---|---|
| OpenAI | An OpenAI API key |
| Claude (Anthropic) | An Anthropic API key |
| Local LLM | An OpenAI-compatible local LLM (Ollama, LM Studio, llama.cpp, vLLM, and so on) |

Select a provider and model, then enter the API key. When you enable the "Save" checkbox (for the API key), the key is **encrypted with Windows DPAPI (CurrentUser scope)** and stored under your user profile, and it is filled in automatically the next time you start (it is never stored in plain text; DPAPI CurrentUser protection is tied to your Windows user account, so how well the key is protected depends on that account staying secure).

For "Local LLM", give the endpoint URL of your server in the endpoint field (including the `/v1` part — the default is Ollama's `http://localhost:11434/v1`). The API key is optional there: leave it empty for a server that requires no authentication, or enter one for a server that does. The provider and endpoint you chose are remembered and restored the next time you start.

### 2. Codex (the "Codex" tab)

Reuses the sign-in of an installed Codex CLI, so no API key is needed. Providers defined in `config.toml` are also shown as candidates.

### 3. Claude Code (the "Claude Code" tab)

Reuses the sign-in of an installed Claude Code, so no API key is needed. The model is selected from an alias (such as `sonnet`).

### 4. Copilot (the "Copilot" tab)

Reuses the sign-in of an installed GitHub Copilot CLI, so no API key is needed (a GitHub Copilot subscription is required). The model candidates are enumerated at runtime after connecting — which models you can pick depends on your Copilot plan — and leaving the model empty uses the CLI's default.

The connection tab you used last is remembered and selected automatically the next time you start.

## What you can do

- **Generate a diagram** — describe your requirements and it creates tables, columns, and relationships
- **Edit a diagram** — add to, change, or delete from an existing diagram (most operations — tables, columns, relationships — go onto the Undo/Redo history, so you can revert them; named-query edits are applied directly and are not part of that history)
- **Attachments** — attach files to pass existing design materials and the like as context. The kinds of files you can attach depend on the connection method — OpenAI-compatible: images and text; the Anthropic API: images, PDFs, and text; Claude Code: images, PDFs, text, and other binary files as well; Copilot: images only; Codex: attachments are not supported — and each kind has its own size limit
- **AI mock generation** — generate web mockup screens from the current ER diagram, saved as a mock folder (see below)

## AI mock generation

"AI Mock Generation" turns the current ER diagram into web mockup screens. The result is saved as a **mock folder** — a `mock.json` manifest plus one HTML file per screen and a shared `style.css` — and it is written live as you converse. Point the tool at an empty folder to start fresh, or at an existing mock folder to resume (the conversation starts anew, and resuming works regardless of which backend you use, since it restores from the folder contents rather than a chat log). The screen-list sidebar lets you click through the screens, and the preview follows the links between them. You can export the whole mock as a single self-contained HTML file. You can also export a screen design document (`README.md` — a screen list, a Mermaid transition diagram, a screen-by-entity CRUD table, and a per-screen item table) generated deterministically from the mock folder without any AI; once exported it is rewritten automatically whenever a screen is saved or removed, and opening the folder on GitHub shows it as the folder's front page. The AI declares each screen's entity usage (CRUD) as it saves screens, and those declarations render as a screen × entity table (the table is omitted when nothing is declared).

As an optional second step, you can generate a runnable **mock project** from the mock folder with any of the four backends. Pick a target — **Blazor Web App** (the default; globally interactive with InteractiveServer, which reproduces the mock natively for the web by porting the screens' HTML and shared `style.css` as faithfully as possible) or **WPF (.NET)**. QuickER generates the solution, the csproj files, and the data-layer code (Entity / EditModel / Mapper / an in-memory Repository, plus the QuickER Repository when the diagram's dialect supports it) deterministically from the ER model, so the AI only has to write the UI layer. The mock folder is bundled under `design/mock/`, and the AI implements the UI from it. With Claude Code, Codex, or Copilot, the agent edits files and iterates on `dotnet build` until it passes or the run hits its overall time limit; with an API key, the model submits the files in a single deterministic pass (no self-correction — plus one fix round if the build fails), so it may fail more often. Either way, QuickER verifies the result with its own final `dotnet build`. An additional-instructions field lets you pass extra guidance for that implementation.

This second step is an aid for PoCs and prototyping. Depending on the AI model and the connection method, build errors may remain; when the final build fails, the generated files and the log are left in the output folder so you can fix them yourself.

## Notes

- With the API key method, Codex, Claude Code, and Copilot alike, the diagram contents (table definitions, etc.) are sent to the AI provider you selected. When handling sensitive schemas, follow your organization's policy
- If you do not use the AI features, no API key or other configuration is required (the ER diagram designer and code generation work without connecting to the network)
- The second step (mock project generation) **builds AI-written code on your machine with your own privileges**. What the AI is allowed to do differs by backend

  | Backend | File writes | Command execution |
  |---|---|---|
  | API key | Submission through `emit_file` only. Limited to the output folder and to UI-layer source extensions (`.xaml` / `.cs` for WPF, `.razor` / `.css` / `.cs` for Blazor); build configuration files (`.csproj`, `Directory.Build.props` and the like) and anything under `Generated/`, `design/`, `obj/` or `bin/` are rejected | None |
  | Copilot | Auto-approved only under the output folder | Auto-approved only when the command's paths resolve under the output folder |
  | Codex | Inside the sandbox (`workspace-write`) | Runs without approval inside the sandbox |
  | Claude Code | Edits files from the working folder (`Edit` / `Write` / `MultiEdit`) | `Bash` is unrestricted (it can run any command) |

- The API key method is designed so that QuickER hands the model a restricted tool set, and the build itself runs with MSBuild's automatic imports (`Directory.Build.props` and friends) disabled. The agent backends (Codex / Claude Code / Copilot) run the CLI you installed under that CLI's own permission model, so QuickER does not restrict them. If you generate a mock from an untrusted schema or from input you do not control, choose the API key method or run it in an isolated environment

## License note

The AI feature set (chat and mock generation = `QuickER.AI` / `AI.UI` / `AI.Chat` / `AI.Mock`) is covered by [PolyForm Noncommercial 1.0.0 plus additional grants](../LICENSE-NC.md), and those grants make the current releases **free for everyone, including commercial use**. For a plain-language guide see [LICENSING.md](../LICENSING.md); the formal terms are [LICENSE](../LICENSE) and [LICENSE-NC.md](../LICENSE-NC.md).
