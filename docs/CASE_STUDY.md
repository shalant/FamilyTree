# ArborKin — Family Tree Web App
### Portfolio Case Study

**Stack:** .NET 10 · C# 13 · Blazor Server · EF Core · MudBlazor · Azure App Service · Azure Blob Storage · SQL Server  
**Repo:** https://github.com/shalant/FamilyTree  
**Live:** Azure App Service (invite-only)

---

## Overview

ArborKin is a private, multi-user family tree application built for a real family. Members sign in, explore an interactive canvas showing their branch of the tree, add or edit relatives, attach photos, and read each other's biographies and life notes. The goal was a tool that felt personal and beautiful — not like a spreadsheet with lines between boxes.

I built the entire stack solo: data model, service layer, auth system, tree layout algorithm, and UI. It's been in use with real users and real feedback.

---

## The Problem with Existing Tools

Commercial genealogy apps (Ancestry, MyHeritage) are either subscription-locked, ad-heavy, or designed for document-scanning workflows. What the family wanted was simpler: a private, shareable tree where anyone in the family could browse and contribute — with no account wall between them and their own relatives' stories.

The two things that had to work well for this to feel right: **the tree canvas** (it has to be beautiful and navigable) and **the edit form** (it has to be fast and not frustrating to fill in dates and relationships).

---

## Architecture

There is no HTTP boundary between the UI and the service layer. Blazor Server runs on the same process as the business logic:

```
FamilyTree.Web    (Blazor Server + MudBlazor)
      │  direct C# service injection — no REST, no HTTP client
      ▼
FamilyTree.Core   (service layer + EF Core)
      │  EF Core
      ▼
Azure SQL Database
```

This trades the conventional separation-of-concerns benefit of a REST API for dramatically simpler code: no serialization, no HTTP client configuration, no version mismatch between client and server DTOs. For a private family app with one developer, that's the right tradeoff.

CI/CD runs on GitHub Actions — build + test on every push to `master`, deploy to Azure App Service on `workflow_dispatch`.

---

## Screenshots

![Tree canvas — light mode](./screenshots/familytree-hero.png)
*The main tree canvas. The focus person is centered; ancestors flow upward, descendants downward. The Y-axis is a real birth-year timeline.*

![Tree canvas — dark mode](./screenshots/familytree-hero__dark.png)
*Dark mode, toggled via a single CSS variable swap. Persisted to localStorage.*

![Person detail drawer](./screenshots/familytree-detaildrawer.png)
*Clicking any node opens a read-only drawer with vitals, relationships, biography, and a photo strip — without leaving the tree.*

![Edit person form](./screenshots/familytree-edit.png)
*The edit form. Relationships (parents, spouses, former spouses, siblings, children) are all autocomplete pickers into the same people list.*

![People list](./screenshots/familytree-personlist.png)
*The people list — sortable by name, birth year, or status; filterable by living/deceased.*

![Media upload zone](./screenshots/familytree-mediazone.png)
*The media page. Photos are uploaded to Azure Blob Storage; the primary photo appears as the node avatar on the canvas.*

---

## Hard Problems

### 1. JS-Free Tree Layout

The standard approach to a dynamic canvas like this is: render nodes in normal document flow, then measure their positions with `getBoundingClientRect()` in a `useEffect`-equivalent, then draw connectors after the fact. This leads to flicker, timing bugs, and tight coupling to the DOM.

Instead, I compute every node's `(X, Y)` pixel position in C# before anything renders. The layout engine (`FamilyTreeLayoutEngine.cs`) does a bottom-up subtree width measurement pass, then a top-down coordinate placement pass. Each `PersonNode` receives its pre-calculated `Left`, `Top`, and `Size` as Blazor parameters and renders with `position: absolute`. The SVG connector layer uses the same numbers — so it is always aligned, with no post-render step.

The Y-axis is a **real birth-year timeline** (`6.5px/year`), not a fixed row height. This means a 30-year generation gap and a 15-year gap render proportionally — you can see the actual shape of family history in the vertical spacing.

**Cross-root couple problem:** When a child of family A marries a child of family B (e.g. siblings-in-law sharing grandchildren), naively placing them breaks the tree — each partner wants to sit under a different parent group, but the children need to render between them. The layout engine detects these pairs before the placement loop runs, excludes them from recursive placement, places each partner as a leaf at the inner edge of their own family group, then places the children at the couple's midpoint in a post-pass. This prevents connector tangles in a data structure that's fundamentally a graph, not a tree.

---

### 2. Blazor Server Upload Latency

A test user reported that photo uploads were taking **five minutes**. The root cause: Blazor Server renders on the server and communicates with the browser over SignalR (a persistent WebSocket). When a user selects a file, `IBrowserFile.OpenReadStream()` streams every byte across that WebSocket to the server before they reach Azure Blob Storage. At 1–2 Mbps effective throughput, a 12 MB phone photo takes 60–120 seconds in transit alone — and then has to travel again from server to Azure.

The fix: **compress client-side before any bytes cross the wire.** I added a JavaScript function (`ftCompressImage`) that uses the browser's Canvas API to resize the image to a 1400px maximum dimension and re-encode it as JPEG at 82% quality — entirely in the browser, before Blazor touches it. A 12 MB phone photo becomes ~400 KB. The C# upload handler calls `JS.InvokeAsync<string?>("ftCompressImage", inputId, fileIndex, 1400, 0.82)`, receives base64, and passes a `MemoryStream` to the blob service. Upload time dropped from minutes to seconds.

The implementation required a clean boundary: the JS function reads from the `<input type="file">` DOM element by ID and index (the file reference lives in the browser), does the canvas compression, and returns pure base64. Blazor never needs to touch `OpenReadStream` for images.

---

### 3. Drag-State Ownership Split

Two floating widgets — a draggable toolbar and a hero info card — needed to survive Blazor re-renders without fighting the browser. The core conflict: if Blazor owns the `style` of a dragged element, every re-render resets it to wherever Blazor thinks it should be; if JS owns it, the position is invisible to the server and can't be persisted.

The solution splits ownership cleanly:

- **JS owns the gesture**: `ftDrag.js` handles `mousedown` / `mousemove` / `mouseup` and updates the element's `style.left` / `style.top` directly during the drag. Blazor never interrupts.
- **Blazor owns the stored position**: on `mouseup`, JS calls `[JSInvokable] OnDragEnd(key, left, top)`. Blazor stores the value, persists it to `localStorage`, and uses it on next render.
- **No conflicts**: Blazor only sets the position on initial mount and after a deliberate reset. During a live drag, the element is entirely under JS control.

---

### 4. Auth Architecture

The app uses ASP.NET Core Identity with cookie authentication. Several constraints shaped the design:

- **Invite-only registration** (`Auth:RegistrationMode = InviteOnly`): the admin generates a URL-safe token via `IAuthService.CreateInviteAsync(email)`, which stores a 7-day TTL token in `UserInvites`. The registration page reads `?invite=<token>` and validates it on submit.
- **Google OAuth** registered conditionally — only when both `Google:ClientId` and `Google:ClientSecret` are non-empty, so the app starts cleanly in environments without credentials.
- **Rate limiting**: `/auth/do-login` is protected by a fixed-window limiter (5 requests / 15 min / IP). Identity lockout fires after 5 failed attempts.
- **Super-user bootstrap**: on startup, if `SuperUser:Email` config is set, that user is idempotently promoted — no manual DB edit needed to get the first admin into the system.
- **Family scoping**: every write stamps `CreatedBy`, `UpdatedBy`, `DeletedBy` from a `FamilyId` claim baked into the auth cookie at login. All queries are scoped to the user's family.

---

### 5. SSR Flash Prevention

Blazor Server pre-renders HTML on the server before the SignalR connection establishes. The tree canvas needs to know *which person to center on* — but that answer lives in `localStorage` (or the database), neither of which is available during pre-render.

Without a fix, the tree would briefly render centered on the alphabetically-first person, then visibly jump to the correct one once JS loaded. The fix: a `_ready` boolean that starts `false`. The tree doesn't render at all until `OnAfterRenderAsync` fires (SignalR connected), reads the focus from DB / localStorage, sets the correct person, flips `_ready = true`, and calls `StateHasChanged()`. Users see a spinner for ~200ms instead of a flash of wrong content.

---

## What I'd Do Differently

**Persist drag positions server-side sooner.** Currently toolbar and hero positions are in `localStorage` only, so they're per-device. The user model already exists; this would be a one-field DB update.

**Virtualize large trees.** The layout engine currently renders every node. At 200+ people, the canvas gets crowded and the initial render is slow. A viewport-culling pass — only rendering nodes within the current pan/zoom window — would scale better.

**GEDCOM import from the start.** Many families already have a `.ged` file from Ancestry or similar. Adding import early would have seeded the tree with real data faster and surfaced edge cases in the data model sooner.

---

## By the Numbers

| Metric | Value |
|--------|-------|
| Lines of C# | ~8,000 |
| Lines of Razor/HTML | ~5,000 |
| EF Core migrations | 12 |
| xUnit tests | 11 (service layer integration tests) |
| Azure services used | App Service, SQL Database, Blob Storage |
| Time to first working tree | ~3 weeks |
| Auth implementation | ~1 week |
| Active users | Family invite-only |
