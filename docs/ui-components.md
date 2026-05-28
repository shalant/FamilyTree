# UI components

This document describes the component structure of the Blazor web app for anyone
contributing design feedback or visual changes.

## How the tree works

The tree view on `/people` renders an SVG canvas computed entirely in C#.
Each person is placed at a pre-calculated `(x, y)` coordinate based on their
generation depth relative to the focused person. Bezier curves connect parents
to children using those same coordinates.

**To change the visual style of the tree**, the relevant files are:

- `PersonNode.razor` — the circle, initials, name label, and year beneath each node.
  Change sizes, colors, border styles, and fonts here.
- `FamilyTreeCanvas.razor` — the SVG connector curves and canvas padding.
  Change connector color, stroke width, and curve style here.
- Layout constants at the top of `FamilyTreeCanvas.razor`:
  - `NodeSpacingX` — horizontal gap between node centers (default 100px)
  - `GenerationH` — vertical gap between generations (default 110px)
  - `FocusSize` — diameter of the focused user's node (default 64px)
  - `DefaultSize` — diameter of all other nodes (default 44px)

## Running locally for design review

See the Quick Start in the README. Once running, `/people` shows the tree view by default.
Click any node to open the detail drawer. Use the icon button in the toolbar to toggle
between tree and list views.

## Color system

The app uses MudBlazor's theme system. Colors are referenced via CSS variables like
`var(--mud-palette-primary)` — not hardcoded hex values. To change the palette globally,
update the `MudThemeProvider` in `App.razor` or `MainLayout.razor`.