# JFToolkit.DevOpsPilot

**Zero-dependency .NET library + CLI for managing Azure DevOps projects
with local LLM assistance (Ollama). Analyze workflow patterns, manage
work items, and get AI-powered suggestions — all from the terminal.**

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

**qwen2.5:7b** — ~4 GB RAM, runs on any office laptop. Good at text
analysis and structured JSON output.

To use a different model: `devops-pilot setup` and change the model name,
or edit `~/.jftoolkit/config.json`.

## What it does NOT do (by design)

- No git operations (use `git`/`gh` CLI for that)
- No build/release pipeline management
- No dashboard or GUI — terminal-first
- No cloud LLM dependency — fully local with Ollama

## Requirements

- .NET 8, .NET 9, or .NET 10
- Azure DevOps organization with PAT (Work Items: Read & Write)
- Ollama (optional — only needed for `scan` and `suggest`)

## License

MIT — use it anywhere, commercial or personal.
