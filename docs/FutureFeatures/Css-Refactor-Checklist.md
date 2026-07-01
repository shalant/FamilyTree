🧱 CSS Refactor Checklist for FamilyTree
Phase 1 — Inventory & Classification
Before moving anything, classify every rule in app.css into one of four buckets:

1. Tokens (should stay in design-tokens.css)
Colors

Spacing

Radii

Shadows

Typography

Transitions

Action:  
If you find raw hex values or pixel sizes in app.css, convert them into tokens.

2. Global utilities (should stay in app.css)
These are one‑liner helpers used across the app:

.ft-flex-center

.ft-muted

.ft-ellipsis

.ft-grid-2

spacing utilities (.ft-mt-2, .ft-px-3)

global resets

scrollbar styling

animations used across modules

Action:  
Keep these in app.css.
Rename them with .ft-* if needed.

3. Feature‑level styles (should move to feature CSS)
These belong to a specific module:

Admin layout

Tree canvas layout

People page layout

Auth page layout

Form page layout

Action:  
Move these into:

Code
Modules/Admin/Pages/Admin.razor.css
Modules/Tree/Pages/Home.razor.css
Modules/People/Pages/PersonEdit.razor.css
Modules/Auth/Pages/Login.razor.css
Rename classes using prefixes:

.admin-*

.tree-*

.person-*

.auth-*

4. Component‑level styles (should move to .razor.css)
These belong to a single reusable component:

Hero overlay

Toolbar

Person node

Auth card

Facts banner

Intro overlay

Action:  
Move these into:

Code
HeroOverlayComponent.razor.css
ToolbarComponent.razor.css
PersonNode.razor.css
AuthCard.razor.css
FactsBanner.razor.css
Rename classes using prefixes:

.hero-*

.toolbar-*

.person-node-*

.auth-*

.facts-*

🧱 Phase 2 — Extraction Rules
✔ Rule 1 — If it controls layout, it belongs in feature CSS
Examples:

.ft-auth-page

.ft-form-page

.ft-viewport

.ft-band

.people-table

Move these into their respective module CSS files.

✔ Rule 2 — If it controls visuals of a reusable component, it belongs in .razor.css
Examples:

.hero-glass

.hero-overlay

.toolbar-container

.person-node

.auth-card

Move these into component‑level CSS.

✔ Rule 3 — If it’s a utility, keep it global
Examples:

.ft-flex-center

.ft-muted

.ft-ellipsis

.ft-search-wrap

Keep these in app.css.

✔ Rule 4 — If it uses raw values, convert to tokens
Replace:

css
color: #888;
padding: 12px;
border-radius: 8px;
With:

css
color: var(--ft-gray-400);
padding: var(--ft-pad-sm);
border-radius: var(--ft-radius-sm);
(If tokens don’t exist yet, add them.)

🧱 Phase 3 — Naming Cleanup
✔ Apply prefixes consistently
.admin-* for Admin

.tree-* for Tree

.person-* for People

.auth-* for Auth

.hero-* for Hero overlay

.toolbar-* for Toolbar

.ft-* for global utilities

✔ Convert ambiguous names
Rename:

.page-header → .admin-header (if used only in Admin)

.search-field → .people-search-field

.intro-overlay → .auth-intro-overlay (if auth‑specific)

✔ Convert visual names to semantic names
Rename:

.green-box → .admin-summary-card

.left-column → .admin-nav

.right-panel → .admin-content

🧱 Phase 4 — File Organization
✔ Create missing feature CSS files
If a module doesn’t have a CSS file yet, create:

Code
Modules/<Feature>/Pages/<Page>.razor.css
✔ Create missing component CSS files
For reusable components:

Code
Modules/<Feature>/Components/<Component>.razor.css
✔ Add header comments to each CSS file
css
/* ────────────────────────────────────────────────
   Admin.razor.css
   Purpose: Layout and spacing for Admin module.
   Scope: Modules/Admin only.
   Dependencies: design-tokens.css, app.css
──────────────────────────────────────────────── */
🧱 Phase 5 — Verification
✔ Check for broken selectors
Search for classes used in Razor files but missing in CSS.

✔ Check for unused selectors
Search for classes in CSS not used anywhere in Razor.

✔ Check for token usage
Ensure all colors, spacing, radii, shadows use var(--ft-*).

✔ Check for prefix consistency
Ensure no unprefixed classes remain.

✔ Check for leakage
Ensure .admin-* styles don’t affect .tree-* pages.

🧱 Phase 6 — Final Cleanup
✔ Remove migrated styles from app.css
Delete everything that now lives in:

feature CSS

component CSS

✔ Re-run the audit
Make sure app.css contains only:

tokens (if any)

utilities

resets

global animations

✔ Commit with a clear message
Example:

Code
refactor(css): migrate feature and component styles out of app.css
🧱 Phase 7 — Ongoing Maintenance
✔ Quarterly CSS audit
Remove unused selectors

Consolidate duplicate rules

Add missing tokens

Ensure naming consistency

✔ Add new styles in the correct layer
Never add new styles to app.css unless they are utilities.