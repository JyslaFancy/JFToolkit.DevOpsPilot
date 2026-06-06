# JFToolkit.DevOpsPilot

**Zero-dependency .NET library + CLI for managing Azure DevOps projects
with LLM assistance. Analyze workflow patterns, manage
work items, and get AI-powered suggestions — all from the terminal.**

Supports **Ollama** (local), **OpenAI**, **DeepSeek**, **Groq**, **xAI**,
**LM Studio**, and any **OpenAI-compatible endpoint**. Cross-session memory
with SQLite-backed MemPalace.

[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Target](https://img.shields.io/badge/targets-.NET%208%20%7C%20.NET%209%20%7C%20.NET%2010-512bd4)]()

## The problem

You have several Azure DevOps projects at work. Each has its own
iteration cadence, work item types, and conventions. Switching between
them means context-switching in the browser, remembering which project
uses Scrum vs Kanban, and manually tracking what you should work on next.

This tool analyzes each project, understands the workflow, and helps
you manage tasks from a CLI — with a local LLM providing smart suggestions.

## Install

```bash
dotnet add package JFToolkit.DevOpsPilot
```

Zero dependencies. No Azure DevOps SDK, no OpenAI client libraries.
Pure `HttpClient` + `System.Text.Json` for the REST API, local Ollama
for the LLM.

## Quick start

```bash
# One-time setup
devops-pilot setup
# → prompts for Azure DevOps PAT + organization
# → checks Ollama, pulls qwen2.5:7b if needed

# Analyze a project
devops-pilot scan MyProject
# → detects Scrum with 2-week sprints, 45 active items

# List your tasks in the current sprint
devops-pilot mine MyProject "Sprint 12"

# Create a new task
devops-pilot add MyProject Task "Fix login timeout on mobile"

# Mark as done
devops-pilot done 12345

# Get LLM suggestions
devops-pilot suggest MyProject
# → "Focus on #12347 first — it's blocking 3 other items..."
```

## Programmatic API

```csharp
using JFToolkit.DevOpsPilot;

// Create from saved config
var pilot = DevOpsPilot.Create();

// Analyze project workflow
var report = await pilot.AnalyzeAsync("MyProject");
Console.WriteLine($"Workflow: {report.WorkflowType}");

// List active tasks
var items = await pilot.ListTasksAsync("MyProject");
foreach (var item in items)
    Console.WriteLine($"  #{item.Id}: {item.Title} [{item.State}]");

// Create a new work item
await pilot.AddTaskAsync("MyProject", "Task", "Deploy v2.1 to staging");

// Get LLM recommendations
var suggestions = await pilot.SuggestAsync("MyProject");
Console.WriteLine(suggestions);
```

## Commands

| Command | Description |
|---|---|
| `setup` | Configure PAT, organization, Ollama model |
| `scan <project>` | Analyze workflow (Scrum/Kanban/etc.) |
| `list projects` | List all projects in the org |
| `list <project> [iteration]` | List active work items |
| `mine <project> <iteration>` | List my work items |
| `add <project> <type> <title>` | Create a work item |
| `done <id>` | Close a work item |
| `suggest <project>` | LLM suggests what to work on |

## Default LLM

**qwen2.5:7b** (Ollama) — ~4 GB RAM, runs on any office laptop. Good at text
analysis and structured JSON output.

To use a different model or provider: `devops-pilot setup` and follow the prompts,
or edit `~/.jftoolkit/config.json`.

```json
{
  "LlmProvider": "openai",
  "OpenAiKey": "sk-...",
  "OpenAiModel": "gpt-4o-mini"
}
```

Supported providers: `ollama`, `openai`, `deepseek`, `groq`, `xai`, `lmstudio`, `custom`.

## Cross-session memory (MemPalace)

Chat sessions and project facts are persisted in `~/.jftoolkit/mempalace.db`
using SQLite with FTS5 full-text search.

### Chat commands

In `devops-pilot chat <project>`:

```
> /memory           Show all saved facts about this project
> /remember CI uses GitHub Actions
> /forget CI        Delete a fact
> /history 20       Show last 20 messages from past sessions
> /sessions         List past chat sessions for this project
```

### CLI commands

```bash
devops-pilot memory MyProject             # Show saved facts
devops-pilot remember MyProject CI "GitHub Actions"  # Save a fact
devops-pilot forget MyProject CI          # Delete a fact
devops-pilot sessions MyProject           # List past chat sessions
devops-pilot recall "deployment error"    # Full-text search in chat history
```

The agent automatically loads relevant project memory when starting a chat
session, and recalls conversations from past sessions.

## What it does NOT do (by design)

- No git operations (use `git`/`gh` CLI for that)
- No build/release pipeline management
- No dashboard or GUI — terminal-first
- No cloud LLM dependency — fully local with Ollama

## Requirements

- .NET 8, .NET 9, or .NET 10
- Azure DevOps organization with PAT (Work Items: Read & Write)
- LLM provider (Ollama, OpenAI, DeepSeek, Groq, xAI, or LM Studio)

## License

MIT — use it anywhere, commercial or personal.
