# Changelog

*English | [日本語](CHANGELOG.ja.md)*

This file records changes that affect QuickER users. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (see [CONTRIBUTING.md](CONTRIBUTING.md) for the versioning rules during 0.x and the release procedure).

## [Unreleased]

### Added

- Initial public release
  - Visual ER design (crow's foot notation, one-to-one / one-to-many / many-to-many, composite primary keys, FK referential actions, comprehensive undo/redo, and a large-diagram canvas UX with zoom / pan / search / minimap)
  - Multi-DB support (schema import, diff sync, DDL generation, and automatic type conversion on dialect switch across the five dialects: SQL Server / PostgreSQL / MySQL / Oracle / SQLite)
  - C# code generation (Entity / EditModel / Mapper plus a 3-way data-access choice: none / Repository (QuickER) / EF Core — same interfaces, swappable with a single DI registration line; optional runtime NuGet package reference mode)
  - AI chat for generating and editing diagrams (OpenAI / Anthropic / Ollama / Codex / Claude Code), and mockup generation from ER diagrams
  - Import/export (DBML / Mermaid / Excel definition sheets / PNG / SVG / vector printing) and the CLI (`quicker generate` / `quicker scaffold`)
  - A working sample `samples/ec-order` (SQLite, no external DB required)
  - License structure: core = MIT; the AI features and code generation (7 projects) = PolyForm Noncommercial 1.0.0 with Additional Grants (currently free for everyone including commercial use; commercial use of the basic code generation is granted permanently; codified as the "Additional Grants" section of LICENSE-NC.md — see the "License" section of the README for the provisioning policy and the notice about possible future paid licensing)
