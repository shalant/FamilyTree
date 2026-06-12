# Mobile Layout — Brainstorming V1

> Status: pre-decision. No implementation started. This doc records thinking, not commitments.

---

## The core tension

The family tree canvas is a large-surface pan/zoom interaction — the same class of problem as Google Maps or Strava. The right model is:

- **Default mobile view**: similar to desktop, adapted (not replaced) — focus person centered, immediate family visible, pan/zoom working via touch
- **Zoom all the way out**: entire tree visible as tiny nodes, same as desktop at min zoom
- **Not a card-based drill-down** (the original alternative considered and rejected): loses the "whole tree" mental model, which is the point of the app

Forms, lists, the admin panel, and the dashboard are a separate problem — they just need `@media` breakpoints and are straightforward.

---

## JS crash prevention (do this before anything else)

### `passive: false` on touch listeners
Chrome/Safari register `touchstart` and `touchmove` as passive by default. Calling `e.preventDefault()` inside a passive listener throws a console error and silently does nothing — the page scrolls AND the canvas pans simultaneously.

```js
canvas.addEventListener('touchmove', onTouchMove, { passive: false });
canvas.addEventListener('touchstart', onTouchStart, { passive: false });
```

### Guard `touches` array access
When all fingers lift, `e.touches` is empty. Always guard:
```js
if (e.touches.length === 0) return;
```

### `touch-action: none` on the canvas element
Tells the browser to hand all touch gestures to JS and not compete. Without it, the browser and your JS fight even with `passive: false`.

```css
#ft-canvas { touch-action: none; }
```

### `overscroll-behavior: none` on the canvas
Prevents iOS rubber-banding the whole page when the user hits the canvas edge.

```css
#ft-canvas { overscroll-behavior: none; }
```

### Double-tap zoom (iOS)
iOS double-taps zoom the page. `touch-action: manipulation` on the canvas disables this while preserving pinch-to-zoom.

---

## Pinch-to-zoom
Follows naturally from the passive listener fix. Track two touch points, compute distance delta per frame, map to the existing scale factor in `canvas-interaction.js`. ~20 lines, slots into the existing wheel-zoom logic.

---

## Floating widgets: hero overlay, toolbar, nav

Draggable floating widgets don't work on mobile — dragging conflicts with scroll, tap targets are too small. Standard pattern (Maps, Strava, Apple Maps):

| Widget | Desktop | Mobile |
|---|---|---|
| **Toolbar** | Floating, draggable | Fixed bottom bar, icon-only, full width, large tap targets |
| **Hero overlay** | Floating card, draggable | Bottom sheet: collapsed (name + drag handle), slides up on tap to show full stats |
| **Nav** | Top header | Collapse to logo + hamburger, or bottom tab bar (tree / people / import / dashboard) |

Principle: anything draggable on desktop → anchored on mobile. Anything floating → bottom sheet or bottom bar. Canvas gets full screen.

---

## Soft keyboard and viewport instability

When a form input focuses on mobile, the soft keyboard shrinks `window.innerHeight`. On iOS, `100vh` does not shrink to match — fixed/absolute elements (bottom bar, sheets) slide under the keyboard.

**Fix:** use `100dvh` (dynamic viewport height) for any full-screen containers instead of `100vh`.

```css
.ft-canvas-wrapper { height: 100dvh; }
```

Fallback for older iOS: `min-height: -webkit-fill-available`.

Also affects: the address bar on mobile Chrome/Safari shrinks and expands as the user scrolls, eating into `100vh`. Any full-screen surface (canvas, login overlay) should use `dvh`.

---

## Form drawers on small screens

`PersonDetailDrawer` likely has a fixed pixel width designed for desktop. On a 390px screen a 60%-width drawer looks broken. On mobile it should be `width: 100%` or close. MudBlazor's drawer has a `Width` parameter — swap it at a breakpoint.

---

## Blazor Server + mobile network switching

Blazor Server runs over a persistent WebSocket (SignalR). When a phone switches WiFi → cellular, or the screen locks, the WebSocket drops.

- The reconnect modal already handles the visual side
- Default `DisconnectedCircuitRetentionPeriod` is ~3 minutes
- A phone screen locked for 5 minutes loses the circuit → full reload
- Consider bumping to 10 minutes:

```csharp
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
});
```

### Is Blazor Server OK for mobile?

Yes, for this app. The structural weakness is the WebSocket dependency — on flaky mobile connections (subway, rural LTE) latency is noticeable and dropped connections freeze the UI until reconnect. But:

- Family tree browsing is low-frequency interaction, not real-time
- Users are likely on home WiFi or good LTE
- The reconnect modal handles the worst case

Blazor Server is a fine choice for a small invited-user family app. It would be the wrong architecture for a consumer app with millions of mobile users on unpredictable networks. Commercial Blazor Server apps on mobile are mostly internal enterprise tooling (dashboards, field service tablets on managed networks) — consumer-facing mobile apps tend to use Blazor WASM instead.

---

## Testing

- **iOS Safari** is where mobile bugs live: strictest passive listener rules, most unpredictable `vh` behavior, oldest WebKit
- **Android Chrome** mostly just works
- Browser devtools device emulation catches layout issues but does **not** replicate touch event behavior — real device testing matters for gesture code
- If you only test one platform, test iPhone Safari

---

## What to document when implementation starts

Replace this file (or add V2) with:
- Which gesture library or raw touch implementation was chosen
- Confirmed breakpoints used across components
- Bottom sheet implementation details
- Any iOS-specific workarounds discovered during testing
