# ADR 001 — JS-free family tree layout

**Date:** 2026-05  
**Status:** Accepted

## Context

The family tree canvas needs to draw SVG bezier curves connecting parent and child nodes.
The typical approach is to render nodes in normal document flow, then use JavaScript
(`getBoundingClientRect`) after render to measure positions and draw connectors.

## Decision

Compute all node positions in C# before rendering. Each node receives pre-calculated
`X` and `Y` coordinates and is placed with `position:absolute`. The SVG connector layer
uses the same coordinate values, so it is always aligned without any post-render step.

## Consequences

- No `IJSRuntime` dependency in the tree component.
- No `OnAfterRenderAsync` timing issues.
- Layout constants (`NodeSpacingX`, `GenerationH`, `PaddingX`) are plain C# fields —
  easy to tune or make into parameters.
- Tradeoff: node label text overflow must be managed manually (fixed `LabelWidth`).
  A JS approach would let text size drive layout; this approach does not.