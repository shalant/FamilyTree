# 🌳 FamilyTree  
A modern, full‑stack family tree application built with **Blazor Server (.NET 10)**, **ASP.NET Core Web API**, **SQL Server**, and **MudBlazor** — featuring a custom layout engine, smooth pan/zoom canvas, Azure‑backed media uploads, and a clean, token‑driven dark/light theme.

---

## ✨ Features

- **Interactive family tree canvas**  
  - Smooth pan + zoom  
  - Physics‑based clamping  
  - SVG connectors for parents, spouses, and children  
  - Focus mode for exploring branches

- **Full CRUD for people & relationships**  
  - Add/edit forms with shared components  
  - Drawer‑based detail view  
  - Reusable confirmation dialogs

- **Azure‑backed media uploads**  
  - Drag‑and‑drop upload zone  
  - Animated borders, file validation, and progress UI  
  - Blob Storage integration

- **Modern UI/UX**  
  - MudBlazor components  
  - Custom design tokens  
  - Full dark/light mode with smooth transitions  
  - Glass‑blur app bar and floating toolbar

- **Robust backend**  
  - ASP.NET Core Web API  
  - EF Core with migrations  
  - SQL Server (local via Docker, Azure in production)

---

## 🏗️ Architecture Overview

FamilyTree.Web  (Blazor Server — Azure App Service F1)
│  Typed HTTP client calls
▼
FamilyTree.Api  (ASP.NET Core Web API — Azure App Service F1)
│  EF Core, business logic
▼
Azure SQL Database  (free tier — 100k vCore-sec/mo, 32 GB)

Code

**Local development:**  
- SQL Server runs in Docker  
- Both .NET projects run with `dotnet watch`  
- Hot reload + debugging supported  
- Connect to DB via `localhost,1433`

---

## 📁 Project Structure

FamilyTree/
├── src/
│   ├── FamilyTree.Shared/        # Shared DTOs, enums
│   ├── FamilyTree.Api/           # REST API, EF Core, business logic
│   └── FamilyTree.Web/           # Blazor Server UI
│       ├── Modules/
│       │   ├── Components/       # Reusable UI components
│       │   └── Pages/            # Add/Edit/List/Tree pages
│       ├── Services/             # Typed HTTP clients
│       └── wwwroot/              # CSS, JS, static assets
├── tests/                        # API + Web test projects
├── database/                     # Seed scripts, queries
├── docs/                         # Deployment, ADRs, UI docs
└── .github/workflows/            # CI/CD pipelines

Code

---

## 🚀 Quick Start

### 1. Start SQL Server (Docker)

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Dev!Password123" \
  -p 1433:1433 --name familytree-db --restart unless-stopped -d \
  mcr.microsoft.com/mssql/server:2022-latest
2. Apply migrations
bash
cd src/FamilyTree.Api
dotnet ef database update
3. Run the API
bash
cd src/FamilyTree.Api
dotnet watch
# Swagger at https://localhost:7001/swagger
4. Run the Web App
bash
cd src/FamilyTree.Web
dotnet watch
# App at https://localhost:7000
🧩 UI Architecture
Two-view pattern
Tree View — SVG-based, interactive, layout computed in C#

List View — searchable, sortable MudBlazor table

Component Responsibilities
Component	Responsibility
FamilyTreeCanvas	Layout + SVG connectors, emits events
PersonNode	Presentational node
PersonDetailDrawer	Read-only detail view
PersonForm	Shared add/edit form
ConfirmDialog	Reusable destructive-action dialog
People.razor	Orchestrator: navigation + dialogs


🧪 Testing
API tests using xUnit + WebApplicationFactory

UI tests for Blazor components

CI/CD runs tests on every push to master

☁️ Deployment
GitHub Actions builds + deploys both Web and API

Azure App Service (free tier)

Azure SQL Database (free tier)

Full instructions in [Looks like the result wasn't safe to show. Let's switch things up and try something else!]

🧠 What I Learned
Designing a custom layout engine for hierarchical data

Managing pan/zoom state in Blazor with JS interop

Building a token-driven design system for dark/light mode

Structuring a clean API + shared DTO layer

Handling secure media uploads to Azure Blob Storage

Creating reusable, idiomatic MudBlazor components

Implementing a multi-project CI/CD pipeline

📜 License
MIT License — free to use, modify, and build upon.

👋 About the Author
Built by Doug Rosenberg — full‑stack .NET engineer, musician, and UI/UX enthusiast.
I love building tools that blend clean architecture with expressive, modern interfaces.