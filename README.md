# 🌳 FamilyTree  

A modern, full‑stack family tree application built with **Blazor Server (.NET 10)**, **ASP.NET Core Web API**, **SQL Server**, and **MudBlazor** — featuring a custom layout engine, smooth pan/zoom canvas, Azure‑backed media uploads, and a clean, token‑driven dark/light theme.

---

## 🎨 Screenshots

> A visual tour of FamilyTree’s interactive UI and design system.

### 1. Hero — Full Family Tree Canvas

![FamilyTree Hero](docs/screenshots/familytree-hero.png)

A medium‑sized tree with visible connectors, focus node, and the glass hero overlay in light mode.

---

### 2. Dark Mode Tree

![FamilyTree Dark Mode](docs/screenshots/familytree-hero__dark.png)

The same tree in dark mode, showing the token‑driven theme, glass surfaces, and contrast tuning.

---

### 3. Person Detail Drawer

![Person Detail Drawer](docs/screenshots/familytree-detaildrawer.png)

Right‑side detail drawer with actions (Edit, Delete, Focus) while the tree remains visible behind it.

---

### 4. Edit Person Form

![Edit Person Form](docs/screenshots/familytree-edit.png)

A clean, reusable form with validation, date pickers, and relationship selectors.

---

### 5. Add Person (Dark Mode)

![Add Person Dark](docs/screenshots/familytree-add__dark.png)

Dark‑mode variant of the person form, demonstrating consistent theming and accessibility.

---

### 6. Media Upload Zone

![Media Upload Zone](docs/screenshots/familytree-mediazone.png)

Drag‑and‑drop upload zone with hover animation and file list, backed by Azure Blob Storage.

---

### 7. People List View

![People List View](docs/screenshots/familytree-personlist.png)

Sortable, searchable MudBlazor table for managing family members — the “business app” side of the project.

---

## ⏱️ Development Time
This project was designed and built in 3 days — from initial concept to a fully interactive, themed, data‑driven application.
That includes:

Custom SVG layout engine

Pan/zoom canvas with physics

Dark/light design token system

CRUD forms + validation

Drawer‑based detail UI

Azure Blob media uploads

Full API + SQL backend

CI/CD pipeline

The rapid turnaround reflects my experience with .NET, Blazor, UI architecture, and full‑stack delivery.

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
FamilyTree.Core  (ASP.NET Core Web API — Azure App Service F1)  
│  EF Core, business logic  
▼  
Azure SQL Database  (free tier — 100k vCore‑sec/mo, 32 GB)

**Local development:**

- SQL Server runs in Docker  
- Both .NET projects run with `dotnet watch`  
- Hot reload + debugging supported  
- Connect to DB via `localhost,1433`

---

## 📁 Project Structure

```text
FamilyTree/
├── src/
│   ├── FamilyTree.Shared/        # Shared DTOs, enums
│   ├── FamilyTree.Core/           # REST API, EF Core, business logic
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