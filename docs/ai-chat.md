# Configuring AI chat

*[日本語](ai-chat.ja.md) | English*

From "AI Chat" on the toolbar, you can generate and edit ER diagrams through conversation (e.g., "Design the tables needed for order management on an e-commerce site", "Add a shipping address to `orders`"). With the same connection settings you can also use "AI Mock Generation", which generates a web mockup screen (HTML) from the current ER diagram.

This in-app chat edits the diagram currently open in the GUI. To instead let an external AI agent (Claude Code, Codex, and so on) drive QuickER as part of its own workflow, use the [MCP server](mcp.md).

## Connection methods

On the connection tab of the chat window, you can choose from the following three methods.

### 1. API key

| Provider | What you need |
|---|---|
| OpenAI | An OpenAI API key |
| Claude (Anthropic) | An Anthropic API key |
| Ollama | A local Ollama endpoint (no API key required) |

Select a provider and model, then enter the API key. When you enable the "Save" checkbox (for the API key), the key is **encrypted with Windows DPAPI (CurrentUser scope)** and stored under your user profile, and it is filled in automatically the next time you start (it is never stored in plain text).

### 2. Codex

Uses the account authentication of an installed Codex CLI (no API key required). Providers defined in `config.toml` are also shown as candidates.

### 3. Claude Code

Uses the account authentication of an installed Claude Code (no API key required). The model is selected from an alias (such as `sonnet`).

The connection tab you used last is remembered and selected automatically the next time you start.

## What you can do

- **Generate a diagram** — describe your requirements and it creates tables, columns, and relationships
- **Edit a diagram** — add to, change, or delete from an existing diagram (operations go onto the Undo/Redo history, so you can always revert)
- **Attachments** — attach files to pass existing design materials and the like as context
- **AI mock generation** — generate a web mockup screen (HTML) from the current ER diagram

## Notes

- With the API key method, Codex, and Claude Code alike, the diagram contents (table definitions, etc.) are sent to the AI provider you selected. When handling sensitive schemas, follow your organization's policy
- If you do not use the AI features, no API key or other configuration is required at all (the ER diagram designer and code generation work completely offline)

## License note

The AI feature set (chat and mock generation = `QuickER.AI` / `AI.UI` / `AI.Chat` / `AI.Mock`) is licensed under [PolyForm Noncommercial 1.0.0](../LICENSE-NC.md). **It is currently free for everyone, including commercial use.** For the future provisioning policy (the possibility of paid licensing for commercial use only, permanently free personal/non-commercial use, and advance notice and a transition period if paid licensing is introduced), see the ["License" section of the README](../README.md#license).
