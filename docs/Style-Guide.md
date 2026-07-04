🧱 CSS Strategy & Naming Guide for FamilyTree
This guide defines how CSS is organized, named, and maintained across the FamilyTree project. It ensures clarity, scalability, and discipline as the project grows.

🧱 Recommended CSS Strategy
✔ Keep design-tokens.css
Your design system lives here — it defines the language of your UI.

Purpose

Color palette (--ft-green-600, --ft-surface-card, etc.)

Spacing and sizing tokens

Typography scales

Border radii, shadows, transitions

Guideline

Never reference raw hex values or pixel sizes in component CSS.

Always use tokens — they’re your single source of truth.

✔ Keep app.css for global utilities
This file contains universal helpers, not feature‑specific styles.

Examples

Typography classes (.ft-muted, .ft-subtitle, .ft-caption)

Spacing utilities (.ft-mt-2, .ft-mb-4, .ft-px-3)

Flex/grid helpers (.ft-flex-center, .ft-grid-2)

Global resets and scrollbar styling

Shared animations or transitions

Guideline

Keep it generic and reusable.

Avoid selectors tied to specific components or pages.

If a rule only applies to one module, move it out.

✔ Create feature‑level CSS files for large modules
Each major module (Admin, Tree, People, etc.) should have its own layout and rhythm.

Examples

Code
Modules/Admin/Pages/Admin.razor.css
Modules/Tree/Pages/Home.razor.css
Modules/People/Pages/PersonEdit.razor.css
Purpose

Define layout containers (.admin-container, .admin-card)

Control spacing and alignment for that feature

Handle responsive behavior specific to that module

Style feature‑specific patterns (tab headers, dashboards, etc.)

Guideline

Treat these as “mini design systems” for each vertical slice.

Keep them cohesive — one rhythm, one spacing scale.

Don’t leak styles outside the module (use scoped selectors).

✔ Use .razor.css only for truly isolated components
These are self‑contained UI elements that appear across features.

Examples

Code
PersonNode.razor.css
HeroOverlayComponent.razor.css
CustomToolbar.razor.css
Purpose

Encapsulate styles that belong only to that component.

Prevent leakage into other modules.

Keep markup + styles together for portability.

Guideline

Keep these files small and focused.

Avoid global selectors or overrides.

Prefer component‑scoped variables and classes.

🧱 Naming Conventions
1. Prefix by domain
Every selector begins with a prefix that reflects its scope.

Scope	Prefix	Example
Global utilities	.ft-*	.ft-flex-center, .ft-muted
Feature‑level	.admin-*, .tree-*, .person-*	.admin-card, .tree-node, .person-label
Component‑level	.hero-*, .toolbar-*, .auth-*	.hero-glass, .toolbar-container, .auth-card


This prevents collisions and makes searching easy.

2. Use BEM for complex components
Block–Element–Modifier:

css
.hero-glass { ... }          /* Block */
.hero-glass__title { ... }   /* Element */
.hero-glass--expanded { ... }/* Modifier */
This keeps relationships clear and avoids ambiguous class chains.

3. Semantic naming, not visual naming
Avoid .green-box, .left-column.
Prefer .auth-error, .admin-header, .tree-connector.

Semantics survive refactors — visuals change constantly.

4. Consistent separators
Hyphens (-) for multi‑word names: .admin-card

Double underscores (__) for elements: .hero-glass__title

Double hyphens (--) for modifiers: .hero-glass--expanded

5. Global utilities stay short
Utilities should be one‑liners:

css
.ft-flex-center { display: flex; align-items: center; justify-content: center; }
.ft-ellipsis { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.ft-muted { color: var(--ft-text-muted); }
Utilities are composable — not feature‑specific.

🧠 Structural Discipline Guidelines
1. Think in layers
Tokens → universal constants

Utilities → reusable helpers

Feature CSS → layout and rhythm

Component CSS → isolated visuals

Each layer builds on the one below it — never sideways.

2. Keep naming consistent
Use predictable prefixes:

.admin-* for Admin layout

.tree-* for Tree visualization

.person-* for People pages

This prevents collisions and makes searching easy.

3. Avoid inline styles
Inline styles are fine for prototypes but break maintainability.
If you find yourself repeating a style twice, move it into CSS.

4. Document your CSS decisions
Add a short comment block at the top of each feature CSS file:

css
/* ────────────────────────────────────────────────
   Admin.razor.css
   Purpose: Layout and spacing for Admin module.
   Scope: Modules/Admin only.
   Dependencies: design-tokens.css, app.css
──────────────────────────────────────────────── */
This helps future you (or collaborators) understand intent.

5. Audit CSS quarterly
Every few months:

Check for unused selectors.

Consolidate duplicate rules.

Move feature‑specific styles out of app.css.

This keeps your CSS ecosystem healthy.

🧩 Why This Hybrid Approach Works
Avoids the global CSS monolith problem  
app.css stops growing uncontrollably — global rules stay global.

Prevents duplication across components  
Shared Admin or Tree styles live in one place.

Keeps vertical slices clean  
Each feature owns its layout and styling, mirroring your backend architecture.

Keeps components isolated  
Small components get scoped styles automatically.

Preserves your design system  
Tokens remain global and reusable, ensuring visual consistency.

This is how large Blazor, React, and Angular apps maintain scalable front‑end discipline.

✅ Summary
Layer	Purpose	Example
Design tokens	Global constants	design-tokens.css
Global utilities	Reusable helpers	app.css
Feature CSS	Layout + rhythm	Admin.razor.css
Component CSS	Isolated visuals	PersonNode.razor.css


This structure gives you clarity, scalability, and discipline — exactly what your FamilyTree project needs as it matures.