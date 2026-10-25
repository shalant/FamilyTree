# Family App — Todo List

---

## Outreach readiness (planned 2026-10-24)

Two goals: (1) ready to start inviting family; (2) robust enough that a casual user's
first doubtful entry gets a reasonable outcome, not invented data or a silent failure.
Work A in order before the first invite; B and C can follow.

**A. Before the first invite**
- [x] **A1: stop inventing data** (2026-10-24, `fix/a1-no-invented-end-dates`). Marking a
      marriage as ended gave it an `EndDate` of the earlier partner's death date, else
      *today*, because `EndDate != null` was the only way to say "ended" and the form has no
      date field. That's how Lillian and Louis came to have ended on 2026-09-18. Added
      `Relationship.IsEnded`, so a marriage can be ended with no known date. An `EndDate`
      still counts as ended, so existing rows and test fixtures read the same. The
      death-date default is kept; the today fallback is gone, from the person form and from
      `ClaudeImportService` too. Migration `ClearInventedMarriageEndDates` sets `IsEnded`
      on every dated marriage, then recomputes each `EndDate` under the new rule (no screen
      ever let anyone type one, so every stored date was a default): Lillian and Louis
      become 1922-12-04 if her death date is on file. Down puts a placeholder date back on
      undated ended marriages so they don't revert to active. Checked on a scratch SQL Server
      database (dated by death, by entry day, undated active; then Down). GEDCOM export writes
      `DIV Y` for an ended marriage with or without a date. Tests:
      `PersonServiceTests.UpdateAsync_MovingActiveSpouseToFormer_LeavesEndDateUnknownWhenNeitherPartnerHasDied`
      (regression, fails against the old fallback),
      `CreateAsync_PersonWhoseOnlyMarriageEndedWithNoDate_CanStillMarryAgain` (fails if
      the active-marriage checks ignore `IsEnded`),
      `UpdateAsync_MovingFormerSpouseBackToActive_ClearsTheEndedFlag`. Still open: whether a
      divorced couple where one partner later died should get that death date (the default
      can't tell divorce from widowhood; only a date field in the form can).
- [x] **A2: silent failures** (2026-10-25, `fix/a2-silent-failures`). Two bugs from
      "Non-security bugs" under Pre-invite hardening, plus two found beside them.
      (1) After creating a person, PersonAdd set the profile photo and linked extra
      siblings through `PersonService.UpdateAsync`, which Members can't call, ignored the
      result and toasted success anyway, so a Member's new person silently lost both.
      Added `PersonService.SetProfilePhotoAsync(personId, mediumId)`, open to admins and
      to whoever created the person, taking one of that person's own media rather than a
      URL; sibling links now go one by one through `RelationshipService.CreateAsync`,
      which Members may already use. Each failure gets its own error toast (the add
      itself still succeeded). `PersonForm.UploadPendingAsync` used to drop a rejected
      photo without a word; it now returns a message per failed file. PersonEdit's
      sibling-link step also toasted success without checking (admins only, so rarer);
      it now shows the result. (2) `RelationshipService.UpdateAsync` counted the row
      being updated as the "other" active marriage, so every edit to an active spouse
      row failed; the check now skips that row, and only applies when the marriage being
      saved is itself active (recording an ended marriage beside a current one was
      refused too). Tests: `UpdateAsync_ActiveSpouse_DoesNotCountItselfAsTheOtherMarriage`
      and `CreateAsync_EndedMarriage_IsAllowedBesideAnActiveOne` (both fail against the
      old code), `UpdateAsync_ReopeningAnEndedMarriage_StillFailsWhenAPartnerHasRemarried`,
      three `SetProfilePhotoAsync_*` tests (creator allowed; another Member refused;
      another person's photo refused), and a `CrossTenantMatrixTests` row plus positive
      control for the new method. Not covered: a bUnit test of PersonAdd's sibling
      dialog as a Member (the page has no dialog-driving test yet); the service tests
      pin the permission each step now relies on.
- [x] **A3: entry guardrails** (2026-10-25, `feat/a3-entry-guardrails`). Added
      `PlausibilityRules` (Core), one set of rules for both the server and the form.
      Errors (the server refuses): a parent born on or after the child; a child born after
      their mother's death. Warnings (the form asks first; "Save anyway" saves): a parent
      under 12 or over 80, a mother over 55, a child born more than a year after a
      father's (or unknown-gender parent's) death, anyone over 110 with no death date or
      who died over 110. Ages use whole dates, not just years. Until now the parent-age
      and posthumous-birth limits were hard errors, duplicated in `PersonService` and
      `RelationshipService` with year-only arithmetic, and `PersonService` checked only a
      person's parents, never their children: a mother's death date could be moved to
      before her child's birth, or a child older than the parent added from the parent's
      form. Both now check both directions, but only the links a save adds (or every link
      when the person's own dates or gender change), so someone whose saved data already
      breaks a rule can still be edited; the form shows those as warnings. Adoptive links
      aren't checked. **Loosened on purpose:** a parent under 12 or over 80 and a father
      dying 9-12 months before the birth used to be refused and are now allowed after a
      warning. Checked in the running app (local, dev auth): a 1900 birth date with no
      death date shows the warning and "Save anyway". Tests: `PlausibilityRulesTests`,
      `EntryGuardrailServiceTests` (all six fail against the old services;
      `UpdateAsync_MothersDeathMovedBeforeHerChildsBirth_IsRefused` is tagged
      Regression), `PersonFormPlausibilityTests`. Next: B8's "Data check" list can run the
      same rules over the whole tree to find what was saved before this.
- [ ] **A4: invite-path security.** `ReturnUrl` ignored on `/auth/do-login`;
      `Auth:RegistrationMode` fails open (S9); S7 session revocation; S8 blob privacy;
      check Google sign-in works on prod.
- [ ] **A5: Louis's sister drawn on top of Louis** in the archived engine the family sees.
      Doug to decide: fix it in the frozen engine, or switch the family to the prototype
      once it's ready.
- [ ] **A6: walk the invitee journey on prod**, invite email to first tree view, on a phone
      as well as desktop.

**B. Battle-testing**
- [ ] **B7: write-path fuzz harness.** Random casual-user edit sequences through the real
      services: no crash, nothing invented, impossible input rejected with a message, the
      layout still valid afterwards.
- [ ] **B8: "Data check" admin list.** See "Detect likely data-entry slips".

**C. Polish:** the tree loading state, the broken photo tile, and deploying at quiet hours
(about 2.5 minutes of 500s per deploy). Each has its own entry below.

---

## Layout engine direction (ADR 004, 2026-10-17)

Context: after R0-R13 every fuzz invariant passes, but the live tree still looks loose
(2026-10-16 screenshot: Ellen + Marc ~830 screen px apart while other couples sit at the
normal spacing). [ADR 004](architecture-decisions/004-layout-engine-direction.md) proposes
stopping placement patches and replacing the placement algorithm, measured first.

- [x] **Step 1a: quality scores, fixtures and a gallery** (2026-10-17,
      `feat/layout-quality-metrics`). Added `LayoutQualityMetrics` (partner gap, crossings,
      lines through nodes, overlaps, connector length, family off-centre), `LayoutFixture`
      (JSON Person + Relationship rows through `PersonMapper`), the checked-in
      `two-bridges.json` reconstruction, `scripts/export-layout-fixture.sql`, and the local
      gallery (`FAMILYTREE_LAYOUT_GALLERY=1`). Baseline and how-to:
      `docs/Testing/layout-quality-gallery.md`. First finding: on `master`, `two-bridges`
      has Ellen + Marc at 200 but Ray 1000 off-centre from Louis + Lillian; before PR #63
      it was Ellen + Marc at 360.
- [x] **Step 1b: export the live tree** (2026-10-18). Doug exported it through the portal
      Query editor into `Fixtures/private/live.json` (gitignored; 30 people, 8 couples).
      With Doug's own person as the anchor (what the site passes when he's signed in), the
      archived engine on `master` reproduces the 2026-10-18 live screenshot exactly: Ellen +
      Marc 680 apart, Fannie and Max between them. PR #63 is deployed (8b18c70, 2026-10-17
      03:17 UTC) and doesn't close the gap for this anchor. A first pass without the anchor
      wrongly blamed pre-#63 code; `LayoutFixture` now takes an optional `anchor`. Scores,
      archived vs prototype: couples over 200 1 (680) vs 0, width 1900 vs 1400, worst family
      off-centre 287 vs 300, node overlaps 1 vs 0, crossings 1 vs 1, lines through nodes 1
      vs 6. Details: `docs/Testing/layout-quality-gallery.md`, "The live tree".
- [ ] **Collapse ancestors' siblings** (decided 2026-10-18, not started). Siblings of the
      focus person's ancestors (Fannie and Max beside Rose) collapse by default into a "+2"
      marker beside the ancestor that opens on click, the way most genealogy apps handle
      the conflict described in ADR 004, "Collapsing ancestors' siblings". Open questions:
      whose ancestors (the focus person's or the signed-in user's), whether expanded
      branches are remembered (per viewer, localStorage), and how the layout engine takes
      a collapsed person (left out of the input, plus a marker node).
- [ ] **Archived engine draws Louis's sister on top of Louis** (found 2026-10-18 in the live
      export and visible on the live site; on `master` and on pre-#63 code, with or without
      an anchor): a sibling-only person with no parents on the tree lands 10 units from Louis
      on the same row. ADR 004 freezes the archived engine
      except for production breakage; decide whether this counts, or whether switching to
      `TimelineLayoutEngine` (no overlap on the live tree) is the fix.
- [x] **Step 2: decide birth-year timeline vs generation rows** (2026-10-17). Doug keeps the
      timeline; option 1 (exact birth-year Y, generation layers only for ordering, X solved
      with separation only where blocks overlap in height). See ADR 004, "Step 2 decision".
- [x] **Archive the current engine** (2026-10-17). `FamilyTreeLayoutEngine` frozen behind
      `IFamilyTreeLayoutEngine`, still the default; `Layout:Engine=Timeline` switches.
- [ ] **Step 3: prototype** — first version done 2026-10-17 (`TimelineLayoutEngine`,
      `TimelineLayoutEngineTests`). MSAGL wasn't evaluated: option 1 needs every person at
      a fixed Y (their birth year), which a stock layered layout doesn't assume, so a small
      hand-written solver came first. Revisit MSAGL (or VPSC) if the solver becomes the
      bottleneck. On 500 generated families: 0 overlaps
      (archived 15), 107 families with a loose couple (164), 14% narrower, 12% less
      connector, but 13% more crossings. On `two-bridges`: 1620 → 1022 wide, every couple
      at 200, worst off-centre 1000 → 127. Next, in order: crossings (dummy nodes for long
      edges, transpose pass), speed (~500 ms at 500 people), 3+ partner blocks, connectors
      as obstacles, the anchor. Then Doug tries it on the live tree (Step 1b first).
  Second round 2026-10-18 (Doug: "go ahead with step 2" of the plan to try it live): uses
      the anchor; orphan siblings share their sibling's layer and stay beside them (on the
      quieter end when the sibling stands between two partners); dateless years held between
      parents and children, partners sharing one; a family's line down to its children is
      kept clear of other families; a child pulls their own parents' end of a block; a loose
      extra partner stays on the right end. Live tree: 1240 wide (archived 1900), every
      couple at 200, 0 crossings, 4 lines through people (all dashed sibling links), worst
      off-centre 274. 500 generated families: crossings 894 → 683, lines through people
      672 → 582, still 0 overlaps, 107 loose couples. Still open: 3+ partner blocks (worst
      partner gap 1600), speed not re-measured, Kat past her uncle Sam (ordering ignores
      free space), dashed sibling links crossing a block.
- [x] **Step 4: run both engines side by side** (2026-10-18). Both engines sit behind
      `IFamilyTreeLayoutEngine` (`Layout:Engine`), and `TimelineEngineSuiteTests` reruns
      every fuzz invariant and transition pair against the prototype through
      `LayoutEngineOverride`: all pass, including the no-overlap invariant on the full
      domain that the archived engine still skips, except three listed with reasons
      (`Invariant4` full domain; Kat past Sam, both GUID orders) and one test judged not
      applicable (the prototype turns Bill + Gish so newly linked brothers sit together).
- [x] **Step 5: measure the prototype against common practice** (2026-10-23,
      `feat/layout-baseline-msagl`). Added `LayeredBaselineEngine` (tests only): MSAGL's
      standard layered layout on the same families, in generation rows and with birth-year
      Y. Over 500 generated families the prototype has fewer crossings (683 vs 1010), is
      narrower (1197 vs 1472) and centres families better (207 vs 219); it trails on lines
      through nodes (582 vs 529). MSAGL with birth-year Y has 33 overlaps, so the
      prototype's height-aware X phase is the step that pays for keeping the timeline. The
      107 loose couples are identical under both, so they come from drawing 3+ partners in
      a line, not from placement. Details: ADR 004, "Step 5".
- [x] **Quality ratchet** (2026-10-23, same branch). `LayoutRatchetTests` fails if any of
      the prototype's scores (500 generated families, every fixture) gets worse than
      `LayoutQuality/ratchet.json`; `FAMILYTREE_RATCHET_UPDATE=1` tightens, never loosens.
      Verified by widening node spacing 120 → 130: 13 scores failed. Each layout round from
      here: one gap, ratchet, gallery. Next gaps, in order: dashed sibling links (most of
      the 582 lines through people), Rebecca's nephews 274 off-centre on the live tree,
      people with 3+ partners (the 107 loose couples).
- [x] **Score lines drawn on top of each other** (2026-10-23). Doug saw Ray drawn as a
      brother of Morry and Isador on the prototype's live tree: their mother's line ran along
      Ray's. Added the "lines on top of another family's" score (sibling links sharing a
      person excluded) and its ratchet limits: prototype 646 over 500 generated families and
      1 on the live tree; archived engine 0 on the live tree. See ADR 004, "Lines on top of
      each other".
- [x] **Get the prototype's merged lines to 0 on the live tree** (fixed 2026-10-24,
      `fix/timeline-orphan-sibling-side`). Rebecca's orphan sister was glued to Rebecca's
      inner side, so the sister's sons landed past Ray and her line ran along his. The
      prototype now tries turning each couple that has an orphan sibling glued to it and
      keeps the turn if the order crosses less. Live: merged lines 1 → 0, worst off-centre
      274 → 92. 500 generated families: merged lines 646 → 641, but crossings 683 → 685 and
      connector length 2780 → 2784; Doug accepted that trade and both ratchet limits were
      raised by hand. Regression test:
      `TimelineLayoutEngineTests.OrphanSiblingsChildren_DoNotShareALineWithTheNextFamily`.
- [ ] **Judge the orphan-sibling turn by the drawn layout** (found 2026-10-24). The turn is
      judged by straight parent-child lines in row order, not by the finished drawing, so
      it makes 4 of the 500 generated families worse (seed 20261010 case 379: 10 → 19
      crossings; case 7: 4 → 7) while 7 improve. Scoring each candidate after `SolveX`
      (crossings and merged lines from the real connectors) would drop the bad turns; it
      costs a position solve per candidate.
- [x] **Tell ended marriages and sibling links apart** (2026-10-24,
      `feat/distinct-relationship-lines`, both engines). On the live tree four grey dashed
      lines around Louis looked alike: his ended marriage to Lillian, Lillian and Libby's
      sister link, and his own links to Tilly and David. Ray hung from what read as a
      sibling line, so Ray and William didn't read as half-brothers. An ended marriage now
      has long dashes and a 💔; a sibling link is light grey dots. The SVG export matches
      and now draws sibling links at all. Test: `RelationshipLineStyleTests`.
- [x] **Route sibling links around people** (2026-10-24, `feat/route-sibling-links`, PR #73,
      prototype only). Straight links ran through anyone between two siblings (Lillian to
      Libby through Louis; Louis to David through Libby and Tilly, on top of Louis to
      Tilly). Each set of siblings now hangs from one dotted bar, drawn as a chain left to
      right, routed first so it lies along no other line within 10, then through the fewest
      people, crossings, length; a bar breaks (a line jump) where another line crosses it,
      so Louis's line through his wives' sister bar doesn't read as him hanging from it.
      This reworks the 2026-10-23 bracket attempt (merged lines 646 → 827), which lacked
      the along-first rule and drew each recorded pair separately. Live: lines through
      people 4 → 0. Generated: lines through people 579 → 405, merged lines 641 → 628,
      connector length 2784 → 2752. Doug accepted: generated crossings 685 → 697, live
      crossings 0 → 1 (the Louis jump, unavoidable while both sets are drawn), live
      connector length 5434 → 5446. Tests:
      `TimelineLayoutEngineTests.SiblingSets_AroundAManWithTwoWives_EachGetOneClearBar`.
- [x] **Keep lines out of people's names** (2026-10-25, `fix/lines-through-names`,
      prototype only). Seen on the live tree through the picker: couple lines ran through
      the name of a partner born a few years earlier (Joseph, Louis, Marc, Stephanie,
      Lauren), and David's stem to Esther through his own name. New score "lines through
      names" (live 10 -> 0, generated 2072 -> 233); the higher partner's line now starts at
      their own circle and steps down beside their name, and a single parent's stem starts
      below it. Generated crossings 697 -> 659, merged lines 628 -> 683 (loosened by hand,
      for Doug to accept: a person with two marriages on the same side shares a short stub).
      ADR 004, "Lines through names". Test:
      `TimelineLayoutEngineTests.PartnersBornAFewYearsApart_AndASingleParent_NoLineThroughAName`.
- [ ] **A couple line stops short of a partner born much earlier** (found 2026-10-25). When
      the line passes below the higher partner's name it ends there, short of them. Rising
      to just under the name was tried and raised generated merged lines to 864.
- [ ] **Fewer crossings from sibling bars** (found 2026-10-24). Routes are scored against
      the lines drawn so far, set by set, narrowest first, and only bars above the row are
      tried (below the row was tried: crossings 696, and it sits among couple lines and
      stems). Ordering siblings next to each other where the layout allows, or choosing
      all sets' heights together, should give back some of the 12 crossings.
- [ ] **Data check for Doug:** Lillian and Louis's marriage has an end date of 2026-09-18,
      which looks like the day it was entered. It's the only reason it's drawn as ended.
      Doug confirmed it's a slip (2026-10-24). The app made it up, not a typo: fixed by
      Outreach readiness A1, whose migration sets it to Lillian's death date (1922-12-04) on the
      next deploy. Tick this once that's checked on the live tree.
- [ ] **Detect likely data-entry slips** (asked 2026-10-24, not started). A "Data check"
      list beside `DataIntegrityService`'s existing checks, each finding shown as "probably
      wrong, please check" with a "this is correct" dismiss, never fixed automatically.
      Candidate rules: a date equal to the day the record was entered (the Lillian and
      Louis case turned out to be the app's doing and was fixed by A1, but a date typed in
      a future end-date field could still slip this way); an event at an impossible age for
      someone with no death date (over 110); death before birth, a birth in the future, a
      marriage ending before it started or after a partner died; a child born when a
      parent was under 12, a mother over 55, or a father dead over a year; a surname one or
      two letters off a close relative's ("Schumevith" vs "Schmulevith"); possible
      duplicates (`PersonNameDuplicateMatcher`: William vs "Bill"). Later: the same rules
      as warnings in the Add/Edit Person form.
- [ ] **Try the prototype live**: since 2026-10-24 (`feat/layout-engine-picker`) this no
      longer means switching the whole site. The super-user picks "Timeline prototype"
      from the layout menu in the floating toolbar; everyone else stays on `Legacy`
      (`LayoutEngineSelector`, ADR 004, "Trying the prototype on live data"). Merge #65
      and the picker, deploy, then try it. Setting `Layout__Engine=Timeline` still
      switches everyone, for when the prototype is ready.
- [ ] **Layout picker follow-ups** (2026-10-24): a picker on mobile (the toolbar is a mini
      pill there); remembering the choice per account instead of per browser; opening
      engines to every viewer once there's more than one worth choosing.

---

## Tree loading state (found 2026-10-24)

- [ ] **The tree's loading state is a lone spinner on a blank white page.** Seen live
      2026-10-24 (Doug's screenshot): the AppBar, then an empty white viewport with a small
      green `MudProgressCircular` in the middle, before the green tree canvas paints. It
      comes from `Home.razor` (`_loading || !_ready`, and the auth-resolving branch above
      it), after the `#ft-splash` spinner in `App.razor` has already gone, so there are
      two spinners in a row. Room to improve: paint the canvas background (the green
      surface) straight away so it doesn't flash white to green, show something shaped
      like the tree (a faint skeleton of nodes) or a short message rather than a bare
      spinner, and make the two loading phases look like one. Check what actually takes
      the time first (circuit connect, people query, focus from `localStorage`/DB) before
      designing around it.

---

## Photo gallery: broken images (found 2026-10-24)

- [ ] **The person photo gallery shows a broken photo as a blank tile with the browser's
      broken-image icon.** Seen live 2026-10-24 on Doug's own Photos tab
      (`PersonMedia.razor`): one of three photos rendered as an empty box in the top-left
      corner, still offering "Use as profile". It was there before that day's deploy (in a
      screenshot taken before the upload), so it isn't from the image-format change in #66.
      `PersonNode` and `PersonDetailDrawer` already handle this (`@onerror` -> initials, or
      a broken-image badge with "Photo couldn't be loaded", 2026-10-12); the gallery tiles
      have no `@onerror` at all. Do: give gallery tiles the same treatment (a clear "Photo
      couldn't be loaded" tile, with Delete available but not "Use as profile"), and find
      out why that photo is broken: missing blob, an unconverted HEIC, or something else.
- [ ] **Upload hint mentions HEIC, but the server doesn't accept it.** `PersonMedia.razor`
      says "JPG, PNG, HEIC, WebP". HEIC only works when the browser converts it to JPEG
      first; if that fails, the original's `image/heic` MIME type is rejected by
      `MediumService`'s allow-list. Either drop HEIC from the hint or say what happens.
      Related to the broken tile above if that photo turns out to be a HEIC.

---

## Deploy/outage experience (2026-10-16)

Context: after a manual `deploy-web.yml` run on 2026-10-16, arborkin.com showed Chrome's
bare "This page isn't working / HTTP ERROR 502" page. The App Service (Linux, B1, single
instance) has no backend to route to while it restarts (startup includes EF migrations and
possibly a serverless SQL resume), so Azure's front end returns 502 before the app ever sees
the request. Goal: a branded, calm page during deploys that also covers outages and server
errors. Note that `app_offline.htm` doesn't help here: it's an IIS/ANCM feature and this app
runs on Linux.

- [ ] **Hold open tabs on the reconnect card until the server is healthy (in-repo, $0).**
      The branded reconnect modal (`App.razor`) already exists, but on
      `components-reconnect-rejected` `ftUtils.js` counts down 3s and calls
      `location.reload()` blindly. Mid-deploy, that reload lands on the raw 502, so the nice
      card hands people straight to Chrome's error page. Fix: add a cheap anonymous
      `/healthz` endpoint (add it to `PageAuthorizationTests`' allow-list if it's a page, or
      map it as a minimal endpoint), keep the card up with "tending the tree" copy, poll
      `/healthz` every ~2s, and reload only on a 200. Add a test for the endpoint, and check
      the polling in a real browser by stopping/starting the local app.
- [ ] **Put a Cloudflare Worker in front of arborkin.com to cover fresh visits during
      outages (infra, $0).** Needed because a new visit or hard refresh during an outage
      never reaches the app, and GoDaddy DNS can't serve a fallback. Plan: move DNS from
      GoDaddy to Cloudflare's free plan, then route arborkin.com through a Worker that
      passes requests to Azure and, on a 502/503/504 or timeout, serves an inline branded
      ArborKin page that polls in the background and reloads itself once the site is
      back. That covers deploys, crashes, a failed startup migration (the app deliberately
      refuses to start) and Azure incidents. Risks to handle: `/_blazor` WebSocket
      traffic must pass through untouched, and the App Service custom-domain/managed-cert
      setup has to change under a proxy. Do the DNS cutover on a weekend, and record it in
      `career-development/docs/SITE_OPS_REGISTRY.md` and `docs/deployment.md`.
      Alternatives considered: a service worker (returning visitors only, so it misses
      first-time invitees), Azure Front Door (~$35+/mo) and deployment slots (need S1,
      ~$70/mo). Both paid options undo the July 2026 cost cuts.
- [ ] **Shorten the restart window.** Measure cold-start time after a deploy (EF
      migrations plus serverless SQL auto-resume) and trim what's avoidable, so whatever
      page covers the gap shows for as short a time as possible.

Already covered, so not a gap: errors while the app is running render the app's own pages
via `UseExceptionHandler("/Error")` and `UseStatusCodePagesWithReExecute` (`Program.cs`).

---

## Pre-invite hardening and testing (2026-10-10)

Context: deciding whether arborkin.com is ready to invite more selected family members.
Assessment: fine for a small, hand-picked first group; not yet for a broad rollout. Plan
doc for the testing legs: `docs/Testing/fuzz-and-adversarial-review-plan.md` (merged in
PR #43).

**Check before sending invites**
- [x] **Deploy the Leg 3 scoping/auth fixes** (`security/scoping-adversarial-review`):
      until then any production Admin can take over the super-user account and logged-out
      visitors can open most pages. See "Leg 3 follow-ups" below. *Done 2026-10-12:* Doug
      ran `deploy-web.yml` after PR #52 merged, which deployed `master` with Leg 3, R0 and
      the layout fixes (R11, R13, R6, R12).
- [x] **Fix R0 (parent-cycle crash) first.** Fixed 2026-10-11 on `fix/r0-parent-cycle`
      (code + tests done; **not deployed yet**, since `deploy-web.yml` is manual-only, so
      production still has the crash until this merges and is deployed). Also run
      `scripts/check-parent-cycles.sql` against production before or right after
      deploying, to see whether a loop is already saved there. See
      `[LAYOUT FUZZ 2026-10-10] R0` below. *2026-10-12:* deployed (see above), and
      `check-parent-cycles.sql` returned zero rows against production: no saved loops.
- [x] **Confirm PR #39's fixes (and everything merged since) are actually deployed.**
      `deploy-web.yml` is manual-only, so merging to `master` does not deploy.
      *Done 2026-10-12:* the deploy above shipped `master` as of PR #52, which includes
      PR #39 and everything since.
- [ ] **Confirm every production user has a `UserFamily` row.** The 2026-07-04 entry below
      says production "almost certainly" lacked them (a user without one logs in fine but
      sees a permanently empty tree). The docs don't record whether the backfill was ever
      done in prod. Check the existing ~5 users, and that a new registration creates the row.
      *2026-10-12:* all 5 production accounts have exactly one row, all in "My Family"
      (two Admin plus the super-user as Admin, two Member), via the new read-only
      `scripts/check-user-families.sql`. Not yet confirmed: that a NEW registration
      creates the row. Rerun the script after the next real or test sign-up.
- [x] **Test on a real phone** (touch, mobile Safari/Chrome). Prior mobile testing was
      emulated via viewport resize or desktop-width CSS tricks. *First pass 2026-10-12
      (Doug, on production):* "looks good so far", with two dings, fixed on
      `fix/phone-test-polish` (not deployed yet): (1) the mobile control panel (opened
      by the AppBar identity caret) dropped in with an animation but vanished instantly
      on close. It now plays the same `ft-nav-close` animation as the nav drawer before
      unmounting (`MobileControlPanelTests.Closing_PlaysCloseAnimation_ThenUnmounts`).
      (2) A profile photo whose URL no longer loads showed the browser's alt text squeezed
      into the avatar circle. The detail drawer now shows the initials with a
      broken-image badge and "Photo couldn't be loaded", broken gallery thumbnails show a
      broken-image tile, and the canvas node falls back to initials
      (`BrokenPhotoFallbackTests`). Both are covered by bUnit tests, verified failing
      against the old components. Neither has been looked at in a real browser yet; check
      both on the phone after the next deploy. Why the photo URL is broken in the first
      place hasn't been investigated (a deleted blob? an expired or external link?).

**Fixed 2026-10-11 (smoke test): signed out, `/faq` -> "Go to dashboard" froze the page behind a red banner**
- [x] Found during `docs/Testing/production-smoke-test-checklist.md` section 1. Two bugs
      stacked. (1) `Routes.razor` wrapped the `NotAuthorized` content in its own
      `<LayoutView Layout=MainLayout>`, but `AuthorizeRouteView` already renders that content
      inside `DefaultLayout` (also `MainLayout`). Navigating from one `MainLayout` page into a
      protected page nested a second `MainLayout`, so two `MudPopoverProvider`s existed and
      MudBlazor threw "already a subscriber ... 'mud-overlay-to-popover-provider'", which
      killed the circuit (URL changed, old page stayed, banner appeared). Removed the inner
      `LayoutView`. Regression test:
      `RoutesAuthorizationNavigationTests` (verified failing against the old `Routes.razor`).
      (2) The banner itself rendered as a tall red slab on the left edge: `MudBlazor.min.css`
      loads after `app.css` and ships its own `#blazor-error-ui` rule (`left:0; bottom:0;
      width:100%`, error-palette red) at equal specificity, so it beat our amber top-centre
      toast. Prefixed our selectors with `html body` so they win regardless of load order.
      No automated test for the CSS (bUnit can't see cascade); checked in a real browser.

**Fixed 2026-10-11 (smoke test): Admin > Users "Generate link" stayed disabled until the email field lost focus**
- [x] `UsersTab.razor`'s invite email `MudTextField` bound on blur, so `_inviteEmail` stayed
      empty while typing and the button's `IsNullOrWhiteSpace` check kept it disabled. Added
      `Immediate="true"` and gated the button on a valid email format (same rule as
      `StoryInviteDialog`) so it enables as soon as a valid address is typed. Not covered by a
      bUnit test: `UsersTab` needs fakes for `IAuthService`, `IAdminUserService` and
      `UserManager<AppUser>`; verified by build and the existing suite only.

**Fixed 2026-10-11 (smoke test): "Link to your tree" button cut off after registering on shorter windows**
- [x] `Register.razor` kept the whole sign-up form on screen after a successful registration
      and appended the "You're all set!" block underneath it, so the only next step
      ("Link to your tree") landed below the fold on shorter windows. The form (heading,
      invite banner, Google button, fields) now renders only while `!_registered`; the card
      shows just the logo plus the success block. Regression test:
      `RegisterSuccessStateTests` (verified failing against the old markup). Not checked in a
      real browser at the reported window size: the app blocks iframing, so I couldn't emulate
      a short viewport.

**Fixed 2026-10-11 (smoke test): "Tip: Drag to explore the tree" toast showed twice**
- [x] `Home.razor`'s `OnAfterRenderAsync` can be re-entered while an earlier call is still
      awaiting `localStorage.getItem("ft-treehint")`; both calls saw "not seen yet" because
      the guard flag was only set after the read. The flag (`_treeHintChecked`) is now set
      before the first await. As a safety net for this recurring class of bug,
      `ToastService.Show` now skips a toast identical (type, message, title) to one still
      showing. Regression test: `ToastServiceTests` (verified failing without the dedup). The
      `Home.razor` race itself has no automated test; `Home` is too heavy to render in bUnit.

**Open (found 2026-10-11, smoke test): signed-out visits to protected pages land on `/?ReturnUrl=...` and the path is dropped after login**
- [ ] Cookie auth challenges redirect to `/?ReturnUrl=%2Fadmin` (the checklist's "redirect to
      login" outcome, so not a security issue). Nothing in the app reads `ReturnUrl`, so after
      signing in the user lands on the tree instead of the page they asked for. Decide whether
      to honour it (local paths only, to avoid an open redirect) or leave it.

**Bug: page flashes and loads twice on arborkin.com (reported 2026-10-10)**
- [x] **Turn off prerendering for the interactive render mode (done 2026-10-11).**
      `App.razor` used `@rendermode="@InteractiveServer"` on both `<HeadOutlet>` and
      `<Routes>`; that built-in mode prerenders, so the browser painted a static server
      render, then the SignalR circuit connected and replaced it with a second, fresh
      render. Confirmed first: `curl` of `/` and `/faq` returned fully rendered page
      content plus `prerenderId` markers. Fix: `new InteractiveServerRenderMode(prerender:
      false)` on both outlets, plus a static `#ft-splash` spinner in the HTML shell that
      `Routes.razor` removes (`ftHideSplash`) on its first render. After: raw HTML holds
      only the splash; the page renders once the circuit connects, title set, splash gone,
      no errors in the app log. Full solution tests pass, including the 9 E2E tests.
      Not done: an automated check that the raw HTML stays prerender-free, and a look at
      it on a real phone or slow connection. Open Graph tags were already gated on
      `_ready`, so they never reached crawlers via prerender; unchanged. Deploy is manual.
- [ ] **Same flash also happens loading the canvas after authenticating (reported
      2026-10-10).** Login is a real form POST to `/auth/do-login` followed by a full-page
      redirect to `/`, so it goes through the same prerender-then-circuit double paint as
      a cold load; the prerender fix above (2026-10-11) should cover that part. But the post-login
      path also has its own sequence on top of it: auth-state spinner -> `_loading`/`_ready`
      spinner (waiting on focus resolution and the `localStorage` read in
      `OnAfterRenderAsync`) -> tree renders -> `canvas-interaction.js` applies the saved
      position and reveals the canvas. Re-test this path separately on production after
      deploying the prerender fix (not verified: no signed-in local session): if a visible jump remains, the cause is that multi-step reveal, not
      prerendering, and needs its own fix. Not investigated yet.

**Google sign-in**
- [ ] **Verify Google sign-in end to end in production, and add automated coverage.**
      Already built (`/auth/google`, callback with its three cases, `EnsureUserFamilyAsync`)
      but only registered when `Google:ClientId`/`Google:ClientSecret` are set, and listed
      under "Not covered this pass" in the 2026-09-11 section. Confirm production
      credentials and redirect URIs for arborkin.com, then test with a real Google
      account: new account (and that it gets a `UserFamily` row), existing-email linking,
      returning user, and behaviour under each `Auth:RegistrationMode`. The E2E suite
      can't use real Google, so add tests around the callback logic instead.
      (Scope assumed from conversation; adjust if the intent was different.)

**Testing legs (see the plan doc for full detail; each on its own branch)**
- [x] **Leg 1: layout fuzz harness** (`testing/layout-fuzz-harness`, PR #44, done
      2026-10-10) — property-based tests over random genealogies with invariants (no
      overlaps, every person once, children below parents, determinism, stability under
      edits, no crashes/NaN). First run found root causes R0-R10, triaged into the
      "Layout Fuzz Harness: first-run findings" section below. Token usage recorded in the
      plan doc.
- [x] **Leg 2: layout transition-bug hunt** (`testing/layout-transition-regressions`,
      done 2026-10-11) — `FamilyTreeLayoutTransitionTests`: 12 before/after pairs, each
      run with ascending and descending GUIDs. 7 pass and now guard their transition; 5
      are skipped, pointing at R1, R4/R7, and the three new entries R11-R13 in the
      "Layout transition hunt" section below. Adoption turned out to be a dormant product
      gap, not a layout bug (entry in the same section). Token usage recorded in the plan doc.
- [x] **Leg 3: adversarial review of family-scoping and auth** (`security/scoping-adversarial-review`,
      done 2026-10-10) — `/code-review xhigh` plus a manual pass found S1-S9 (see
      `docs/security.md`, "Scoping and auth adversarial review"). Fixed S1 (anonymous
      page access and anonymous creates), S2 (Admin → super-user takeover via copyable
      reset links; Users/Audit/Activity now SuperUser-only), S3/S4/S6 (cross-family
      reads, writes and name oracles across Person, Relationship, Medium, Story and
      StoryInvite services, plus tree linking) and most of S5 (story responses and
      Open-mode invites now join the right family). Added
      `CrossTenantMatrixTests` (96 cases incl. positive controls; 57 of the first 90
      failed before the fixes, the 6 added later each failed first too) and
      `PageAuthorizationTests`. E2E suite passes (9/9). Token usage recorded in the plan doc.

**Leg 3 follow-ups (2026-10-10, from the scoping/auth review; see `docs/security.md`)**
- [ ] **Deploy and check before inviting anyone.** The S1/S2 fixes only help once
      deployed (`deploy-web.yml` is manual). Until then, make sure no one but the owner
      holds the Admin role in production (S2 is exploitable by any Admin).
- [ ] **Check how many `Family` rows production has.** S3/S4/S5 only leaked with two or
      more; not checked in the review (no production access).
- [ ] **S7: sessions survive revocation.** Add security-stamp validation to the
      application cookie (`OnValidatePrincipal`) so demoting an Admin or resetting a
      password ends existing sessions; Blazor circuits also need a revalidating
      `AuthenticationStateProvider`. Remove or surface the second Admin source,
      `AppUser.IsAdmin` (the Users tab can't see or clear it).
- [ ] **S8: blob privacy.** Check the live container's public-access level (not done:
      no live Azure access). Media URLs are raw blob URIs with no SAS, one shared
      container, still served after a person is deleted; `security.md` claims SAS tokens.
- [ ] **S5 remainder:** the super-user's own invites, Google sign-up in Open mode and
      plain Open-mode signup still land in the oldest family (documented as a
      single-family assumption). Needs a family picker or per-family invites before a
      second family uses Open mode.
- [ ] **Unscoped admin tabs outside the review's scope:** Messages
      (`UserMessageService.GetAllAsync`, every family's feature requests/contact messages)
      and the Admin Dashboard stats (`DashboardService`, global counts) are visible to
      every family Admin. Decide: SuperUser-only, or scope by family.
- [ ] **S9 lows:** `Auth:RegistrationMode` fails open when missing or mis-cased;
      story-invite `IsUsed` check-then-set race (double submit makes two stories);
      CR/LF in names/places injects GEDCOM records; null MIME type skips the upload
      allow-list (and the size error says 50 MB for an 8 MB limit); `/auth/do-login`
      ignores `?ReturnUrl=`, so deep links land on the tree after sign-in.
- [x] **Package advisories, first pass** (fixed 2026-10-24, `fix/image-format-allowlist`):
      Core's ImageSharp reference was affected, not just the test project's. Bumped
      `SixLabors.ImageSharp` 3.1.11 -> 3.1.12 (still vulnerable) and pinned
      `System.Security.Cryptography.Pkcs` 10.0.13 (was 9.0.4 via `Microsoft.Data.SqlClient`,
      two advisories). `MediumService` now rejects uploads whose actual bytes are a format
      other than JPEG/PNG/WebP/GIF, whatever MIME type the browser sent, and only decodes
      those four. Before, a TIFF labelled `image/jpeg` reached ImageSharp's vulnerable TIFF
      decoder. Regression test:
      `MediumServiceTests.CreateAsync_DisallowedFormatLabelledAsJpeg_IsRejectedAndNotUploaded`.
- [ ] **Package advisories, still open:** ImageSharp's ICC-profile allocation advisory is
      still reachable through ordinary JPEG/PNG uploads; only 4.1.2+ fixes it, and 4.x
      fails the build without a Six Labors license key (Doug to get one from
      sixlabors.com). `AngleSharp` 1.1.2 (moderate, via bUnit) is tests only, never
      deployed. Details: `docs/security.md`, A03.
- [ ] **Non-security bugs found by the same review (not fixed, not verified by test):**
      ~~PersonAdd's post-create `UpdateAsync` (photo back-fill, sibling linking) fails
      silently for Members yet toasts success~~ (fixed 2026-10-25, Outreach A2);
      `RelationshipService` canonical GUID ordering inverts Parent/Adopted rows (latent,
      no UI caller); ~~`RelationshipService.UpdateAsync` always fails on an active spouse
      (counts itself as the other marriage)~~ (fixed 2026-10-25, Outreach A2); `RestoreAsync` revives relationships to a still-deleted
      partner; the unfiltered `(PersonAId, PersonBId, Type)` unique index blocks re-adding
      a soft-deleted pair; `PersonService.CreateAsync` isn't transactional;
      `GetAllAsync` loads every relationship in the database then filters in memory;
      a story-invite respondent who already has an active `UserInvite` gets a broken
      `/register?invite=` link (`Fail` never sets `Data`); `AdminUserService`'s pending
      invites/resets include accepted, cancelled and completed rows.

---

## Layout Fuzz Harness: first-run findings (2026-10-10)

*2026-10-17:* PRs #53-#59 deployed. R5 fixed (PR #60, item 1 below). R8 no longer
reproduces (item 2, `fix/r8-orphan-sibling-partner`). Next: the R7/R6 leftovers (item 3).

*2026-10-16:* PRs #60-#62 merged and deployed. Item 3 partly done on `fix/r7-r6-leftovers`:
the "sibling at the inner edge" block is fixed (see R6). Over 5,000 generated families free
of the R2 shape (base seed 777), families with a split couple went 59 → 47; cross-root
splits 12 → 2. What's left of item 3 is mostly geometry, not placement: 17 of the 47 are
three people all coupled to each other, 11 have someone in three or more couples. Neither
can meet Invariant 4 with couples drawn `SpouseSpacingX` apart and nobody between spouses.
Decision for Doug: give the fuzz invariant the same per-person exemption `AssertSound`
already has. The other ~19 are partners who each head a family of their own (Doug Lee,
centered over his children, can't slide toward his ex-wife), which is item 3's real rest.

**Status as of 2026-10-16.** Fixed and merged: R0, R1, R3, R6, R10, R11, R12, R13, plus
the R2, R4 and R7 parts noted in their entries (PRs up to #58). **Deployed** only through
PR #52 (2026-10-12). PRs #53-#58 (phone polish, R1, R2, R3, R7/R4 part, R7 bridge/R10)
are on `master` but not live until `deploy-web.yml` is run again. Still open, in
suggested order:
1. **R5**: same-named people can swap sides between page loads (`GetAllAsync` has no
   tiebreak after LastName/FirstName). Production-reachable with an ordinary tree; small
   fix (sort by `Id` as the last key, ideally inside the engine). Invariants 5b/5c.
2. **R8**: an orphan sibling with a partner is anchored to the sibling, not the partner.
3. **R7/R6 leftovers**: Invariant 4 still excludes `SpouseAnchoredPartner` (people in
   3+ couples; two partners who each head a family, e.g. Lauren 280px from Marc).
   *2026-10-16:* the sibling-at-the-inner-edge block is fixed; see the note above.
4. **R4 rest** (`AnchorPlacedPerson`: two root groups at one X), **R9**, **R2 (b)**, and
   the shape where two marriages cross between the same two sibling sets (partners 440px
   apart with the other couple between them; found 2026-10-15, no test yet).
5. Housekeeping: bump `SixLabors.ImageSharp` 3.1.11 in the test project (NU1902/NU1903
   advisory warnings on every build). *2026-10-24:* bumped to 3.1.12; the warnings stay
   until 4.x (see "Package advisories, still open" above).

Added `tests/FamilyTree.Web.Tests/ServiceTests/FamilyTreeLayoutFuzzTests.cs` (Leg 1 of
`docs/Testing/fuzz-and-adversarial-review-plan.md`, branch `testing/layout-fuzz-harness`):
CsCheck generates random genealogies, runs them through the production path
(`Person`/`Relationship` rows → `PersonMapper` → LastName/FirstName order →
`CoupleHelper.Derive` → `ComputeLayout`) and checks the plan's 7 invariants on 200
pinned-seed cases per invariant (env vars `FAMILYTREE_FUZZ_ITER` / `FAMILYTREE_FUZZ_SEED`
override; `FAMILYTREE_FUZZ_SOAK=1` runs the local-only soak). Failures shrink to a minimal
family and print a replay seed.

The first run failed 6 of the 8 active invariants. Triage found the root causes below.
None are fixed on the harness branch (each gets its own branch and before/after test).
Every full-domain invariant that fails is `[Fact(Skip = ...)]` pointing here by its R-number. Where a
useful domain is left, an `_ExcludingKnownOpen` twin runs the **same** invariant over
families without the known-open shapes (`KnownOpen` enum in the test file), so the harness
still guards everything else. When fixing one: un-skip its full-domain tests and delete its
`KnownOpen` flag in the same PR. Triage was confirmed by locally patching the engine with a
candidate fix and re-running (patches reverted, never committed): the R1 patch alone makes
full-domain Invariant 6 pass 1,500/1,500, and a vertical-distance collision check removes
most Invariant 1 failures (R4).

Two deliberate interpretations of the plan's wording, both documented in the test file:
Invariant 4 asserts "share a Y" only for couples where **neither** partner has a birth date
(the Y axis is a birth-year timeline, so dated spouses born years apart are meant to differ);
Invariant 6 allows an unrelated component to shift as a **rigid whole** (a wider neighbour
or a new min birth year legitimately moves it) but not to deform.

Re-break check (reverting old fixes one at a time against the active tests): reverting
spouse birth-year inference (2026-07-04) fails the Invariant 6 twin; reverting
"root group must be primary for EVERY parent" (2026-07-07, AND→OR) fails the Invariant 4
twin. Reverting "primary group prefers children" (2026-07-07) or dateless-component
seeding (2026-09-09) is only caught by the skipped full-domain invariants, and reverting
the couple-gap reservation (2026-09-18) or sibling depth propagation is not caught at all
(no invariant covers "nobody between spouses" or node size). So the harness doesn't yet
replace `FamilyTreeLayoutEngineTests`. It covers ground those tests don't.

- [x] **[LAYOUT FUZZ 2026-10-10] R0 — CRITICAL: a Parent cycle crashes the whole web
      process. FIXED 2026-10-11 (`fix/r0-parent-cycle`; fix not deployed yet, see below).**
      `PlaceGroup`/`MeasureGroup`/`MarkDescendants` recurse forever on a cycle
      reachable from a root, and a StackOverflow can't be caught. In Blazor Server it kills
      the App Service process for every family, and anyone loading that tree would
      crash it again. Reachable from the UI as far as the code shows (not yet reproduced in
      a running app): `PersonForm` puts no cap on parent count and doesn't exclude
      descendants from the parent picker, and `PersonService` doesn't validate.
      Minimal repro (`Invariant7b_ParentCycleDoesNotCrash`): Root+Root2 are parents of A,
      A is parent of B, B is also recorded as A's parent (A has 3 parents). With only 2
      parents the cycle is incidentally unreachable (A's cycle parent forms a couple group
      with A's root parent, and that group is never a root). Confirmed: `Test host process
      crashed : Stack overflow`. Fix direction: reject cycles in `PersonService`
      (ancestor check on parent add) **and** make the engine cycle-safe (visited set in
      the three recursions), because bad data may already exist. Fix before inviting more people.

      **What was done (2026-10-11):**
      - *Prevention.* `ParentGraph` (`FamilyTree.Core/Services`) answers "would this link
        close a loop". Checked on every path that can persist a Parent row:
        `PersonService.CreateAsync` and `UpdateAsync` (both the parent and the child lists,
        and only links the save *adds*, so someone already stuck in a loop can still be
        edited and can remove the bad link), `PersonService.RestoreAsync` (restoring
        resurrects cascade-deleted Parent links, which can recreate a loop if the tree
        changed meanwhile), `RelationshipService.CreateAsync`/`UpdateAsync` (judged in both
        the requested and the stored direction, see the canonical-GUID-order note under
        "Non-security bugs found by the same review"), and `ClaudeImportService.CommitAsync`
        (UI deactivated, code still there). `AuthService.LinkUserToTreeAsync` and
        `CreateUnlinkedPersonAsync` need no check: they only link a person they just
        created, who has no other links, so one new edge cannot close a loop (pinned by
        characterization tests that fail if either flow ever starts writing an edge that
        doesn't involve the new person). A rejected save returns `ServiceResponse.Fail("X
        is already Y's descendant, so they can't also be Y's parent. That would create a
        loop in the family tree.")`; the Add/Edit pages already toast any failed save
        (pinned by `ParentLoopRejectionUiTests`). The parent picker in `PersonForm` still
        offers descendants and is not filtered (UX follow-up if wanted; the service is the
        enforcement point).
      - *Defense.* The engine's four recursions that follow the parent-group chain
        (`MarkDescendants`, `MeasureGroup`, `PlaceGroup`, and `ReorderCrossEdgeChildren`,
        which the original entry didn't list) now refuse to re-enter a group already on the
        **current path**. It is a path guard, not a global visited set, on purpose: a
        child with 3 recorded parents sits under more than one group and must still be
        walked both times, so trees without a cycle lay out identically (verified:
        12,000 random acyclic layouts, byte-identical JSON against the pre-fix engine).
        On a cycle the closing link is just not followed: every person is still drawn once
        and the loop is logged as a warning naming the people (once per engine instance).
      - *Existing data.* `DataIntegrityService.FindParentCyclesAsync` (read-only, super-user
        or unattended only) and `scripts/check-parent-cycles.sql` (read-only T-SQL, to run
        against production yourself). Nothing was run against production.
      - *Tests.* `ParentCycleWritePathTests`, `ParentCycleIntegrityTests`,
        `FamilyTreeLayoutEngineCycleTests` (every shape run under 3 people-list orders,
        since the crash depends on list order), `ParentLoopRejectionUiTests`;
        `Invariant7b_ParentCycleDoesNotCrash` un-skipped; new `Invariant7c` (random
        families with 1-2 injected cycles, incl. self-parent) plus a generator sanity test.
        There was no `KnownOpen` flag for R0 (only the skipped test), so nothing to delete.
      - *Left alone, not part of R0.* The latent Parent-direction inversion in
        `RelationshipService` is still open (entry below); the new loop check just
        accounts for it.
- [x] **[LAYOUT FUZZ 2026-10-10] R1 — a person linked only by an ended marriage is laid
      out as a separate tree.** *Fixed 2026-10-11 on `fix/r1-former-spouse-component`:*
      `IdentifyConnectedComponents` now follows `FormerSpouseIds`. Added
      `FamilyTreeLayoutTransitionTests.DatelessMarriageEnds_ExSpouseStaysOnPartnersRowAndChildStaysBelowParent`
      (the Ann/Willa/Max repro as a before/after pair; fails on the old code). Un-skipped
      `Invariant6` (it failed on the old code too) and `ChildlessSecondMarriageEnds_FormerSpouseStaysBesidePartner`
      (passes on the old code as well, so it was never really blocked by R1), deleted the
      `FormerSpouseOnlyLink` flag and the Invariant 6 twin, and dropped R1 from the skip
      reasons of Invariants 1, 3 and 4, which stay skipped for R2–R9. The Doug (1900)/Harvey
      (1908) pair on its own already sat together before the fix, so the 440px gap needed
      more of the family around it; not reproduced separately.
      Original report: `IdentifyConnectedComponents` follows Parent/Child/Spouse/
      Sibling but not `FormerSpouseIds`. A former spouse with no other relatives becomes their own
      component: placed 320px+ to the side, and if dateless, seeded at the current year,
      which then leaks into the ex's inferred year (spouse inference runs before parent
      inference). Repro: Ann is parent of Willa; Max *was* married to Willa; all
      dateless → Willa renders on Ann's row. Also: Doug (1900) was married to Harvey
      (1908) → Doug 440px away. Fix direction: add `FormerSpouseIds` to the component BFS
      (`ComputeLineageSideDistances` already includes it, with a comment explaining why).
      Fails Invariants 3, 4, 6.
- [ ] **[LAYOUT FUZZ 2026-10-10] R2 — couples split apart in blended families.** A couple
      only gets a placement slot if it is the primary group of **both** partners. When one
      partner also has children outside the couple, and the couple has children, or the
      other partner has children elsewhere or recorded parents, the partners land
      240-360px apart. Repros: Rose (child Bud, alone) married to Ellen (child Ellen Jr,
      alone), no shared children → 360px apart. Ann married to Doug (child Willa) but also
      co-parent of Bill with Dora → Doug 360px from Ann. Common in real families (remarriage
      with children from both sides). Fails Invariant 4. *2026-10-12 (R11 fix):* the
      subset where the second partner has no parents on the tree and no other group now
      lays out correctly (placed beside the shared person, children under them). Still
      open when that partner has recorded parents, children by someone else, or the
      person's primary group is a single-parent group (both Rose/Ellen repros above).
      *2026-10-12 (R6/R12 branch `fix/r6-r12-r2-split-couples`):* the Rose/Ellen repro
      is fixed (they were 360px apart only because of the R6 slack; new test
      `TwoSingleParentsMarry_PartnersSitTogetherAboveTheirOwnChildren` fails on the old
      engine at exactly 360px). Over 5,000 generated families, Invariant 4 failures in
      R2-flagged families went 2,005 → 1,826. What's left: (a) a person whose childless
      marriage is their spouse's primary group gets placed under the spouse's parents,
      away from their own former partner and child (fuzz seed 880, `PCG(880, 777)`: Ellen
      Kuh married to Ellen Small, formerly married to Max Small with child Gish, ends
      520px from Max). Likely fix: when a child's primary couple isn't the other
      partner's primary, place only the child there and treat the couple as split. (b)
      Couples `CoupleHelper` derives from a child with three recorded parents, often
      across generations (a great-grandmother "co-parenting" with a grandson), which
      can't all sit together; mostly a fuzz artifact. The `PartnerWithChildrenElsewhere`
      flag stays.
      *2026-10-11 (branch `fix/r2-blended-family-couples`):* the Ann/Doug/Dora repro above
      was already fixed (Ann and Doug 200px apart in both GUID orders). Fixed three more
      pieces, each with a `FamilyTreeLayoutTransitionTests` pair run under both GUID
      orders that fails without that piece:
      1. **Children with no other recorded parent were never placed.** A person whose
         primary group is a couple (Pat and Alex, child Kim) can also have children with
         nobody else recorded (Kit, Kat). That single-parent group was never a root, so
         its children fell to the safety net at the right edge, past Pat's siblings, and
         their spouses landed on top of other nodes. It now hangs on Pat's free side like
         an R11 second marriage with no partner node
         (`ChildrenWithNoOtherRecordedParent_SitUnderThatParentBesideTheirMarriage`). A
         person who already has an attached second marriage keeps it; their
         single-parent children still go the old way.
      2. **A single parent couldn't carry an attached marriage.** Ann has a son with no
         other parent recorded and marries Lauren (no other relatives); Lauren went to the
         spouse safety net and could walk off the left edge. The attachment now also
         works when the primary group is single-parent, on the right
         (`SingleParentMarriesSomeoneWithNoOtherRelatives_PartnerSitsBesideThem`).
      3. **(a) above is fixed** with a different approach from the one suggested: a
         partner with no parents on the tree now belongs, in the cross-root check, to the
         family whose group is their primary one, so Ellen Kuh's remarriage into
         Florence's family is a cross-root couple and gets the existing adjacency and pull
         (`RemarriageIntoAnotherFamily_ParentlessPartnerSitsBetweenBothPartners`).
         Partners who have parents are left alone; claiming them too broke
         `CrossRootCouple_StillDetectedWhenOneSideIsNestedUnderAGrandparent`.
      Also: a couple carrying an attachment turns so it hangs away from a child's
      cross-root marriage (`AttachedFamilyHangsAwayFromACrossRootMarriage`; fails with
      the turn disabled). Over 5,000 generated families (base seed 777), compared with
      the engine before this branch: in families where nobody has more than two partners
      or a child more than two parents, Invariant 4 failures 907 → 896, split couples
      1,078 → 1,052, overlapping nodes 665 → 584, nodes past the left edge 254 → 191,
      children more than 600px from a parent 1,980 → 1,291. Over all families: overlaps
      1,127 → 984, left-edge 397 → 300, far children 4,070 → 3,138, split couples
      3,138 → 3,132. **Worse:** total excess distance of split couples, 276k → 330k px
      (realistic) and 909k → 1,066k px (all). It's mostly a child now placed under their
      parent, in an attached group, whose spouse heads a family elsewhere; before, both
      were dumped at the edge near each other. The descendant walk doesn't follow
      attached groups, so those marriages aren't cross-root couples. Making it follow
      them was tried and made split couples worse (1,054 → 1,135 in realistic families,
      measured before piece 3 above), because of R6's own open limits. Still open: that, and (b).
- [x] **[LAYOUT FUZZ 2026-10-10] R3 — dateless birth-year inference is greedy and
      order-dependent. FIXED 2026-10-15.** Pass 1 takes the first available evidence
      (children, then siblings, then spouses, then parents) in list order and never
      reconciles. Results were: a child on the **same row** as a parent (Marc → Bud, Gish;
      Bud → Bud Jr; Gish → Doug; Bud Jr + Gish co-parent Ray → Ray takes half-sibling
      Doug's year = Bud Jr's year), a child inferred only 3 years after a parent, and
      dateless partners on different rows (Ann from 1 dated child, husband Marc from 2).
      New families start fully dateless, so this hit exactly the people being invited.
      **Fix:** inference is unchanged; `EnforceGenerationConstraints` runs after it. It
      never moves anyone with a recorded birth date. A parent/child gap under 20 years
      moves the undated child down, or, when a dated descendant below the child forbids
      that (its "ceiling"), the undated parent up. Dateless current/former spouses and
      co-parents of one child are then brought to a single year, no higher than their
      dated descendants allow. The loop ends on a parent/child pass, so a child never
      renders level with a parent even for contradictory data (a woman married to both a
      father and his son). `Invariant3` is active with no exclusions, and the
      `DatelessCouple` flag is gone from the fuzz harness. Regression pairs:
      `FamilyTreeLayoutTransitionTests.DatelessChildInferredFromHerOwnChild_...` and
      `DatelessHusbandGainsAnOlderChildByAnother_...`. Not fixed: the `DatelessPerson`
      exclusion on `Invariant1`'s twin stays, because dateless families also reach the
      R4/R9 placement collisions (e.g. a former-spouse partner placed 55px from a dated
      person, found at 3,000 iterations); that is R4/R9's work, not inference.
- [ ] **[LAYOUT FUZZ 2026-10-10] R4 — refinements to the open 2026-09-09 near-miss-Y
      overlap bug (entry below).** The fuzzer confirms it and adds two details: (1) the
      suggested fix `Math.Abs(n.Y - anchor.Y) < diameter` is not enough. The collision has
      to be checked at the **placed person's own** Y, not the anchor's (Sarah 1922, orphan
      sibling of Doug 1917, overlapped Doug's half-brother Bud 1914 even under the
      anchor-Y version). (2) The `FindOpenXAdjacentTo` "last resort: accept overlap" return
      is reachable in generated families. Spouse-anchored partners hit it too: Rose,
      married to Max 1935, overlapped Max's co-parent Dora 1944. Fails Invariant 1.
      *2026-10-15 (with R7):* detail (1) is fixed: `FindOpenXAdjacentTo` checks at the
      placed person's own Y with real node sizes. The `AnchorPlacedPerson` exclusion stays
      on `Invariant1`'s twin: at 3,000 iterations without it, the fuzzer still finds two
      root groups drawn at the same X a few rows apart (Willa Fleishman 1885 on Willa Lee
      1882, with Harvey Lee in three couples and two sibling links), which isn't the
      anchor search.
- [x] **[LAYOUT FUZZ 2026-10-10] R5 — layout depends on the order of the people list.**
      `CoupleSortKey`, `ComputeDepths` (first person), Pass 0.5 seeding (`component[0]`),
      inference order (R3) and component order all read list position. Shuffling the list
      swaps unrelated trees left/right, which is just design. The production-reachable part:
      `PersonService.GetAllAsync` sorts by LastName, FirstName only, so same-named people
      come back from SQL in no guaranteed order and the tree can rearrange between page
      loads. Repro: Doug with two children both named "Gish Kuh" → they swap sides.
      Historically common (a child named after a sibling who died). Fix direction: add
      `.ThenBy(p => p.Id)` in `GetAllAsync`, or sort inside the engine. Fails
      Invariants 5b, 5c.
      **Fixed 2026-10-17** on `fix/r5-same-name-tiebreak` (not deployed yet).
      `ComputeLayout` now starts with `CanonicalOrder`: people sorted by LastName,
      FirstName (invariant culture, case-insensitive, close to SQL Server's collation),
      then `Id`; couples by their partners' positions in that order, children within a
      couple likewise. Inputs aren't mutated. Sorting in the engine rather than in
      `GetAllAsync` also covers whole-list shuffles (5b), not just name ties. Callers
      already pass name order, so a live tree only changes where two people share a full
      name. Invariants 5b and 5c are un-skipped; both failed with the sort disabled.
      Added `FamilyTreeLayoutTransitionTests.SameNamedChildrenReturnedInEitherOrder_NobodyMoves`
      (the Gish Kuh repro), also confirmed failing without the fix.
- [x] **[LAYOUT FUZZ 2026-10-10] R6 — DESIGN QUESTION: cross-root couples always sit
      ~360px apart.** The cross-root step reserves `OrphanBufferX` (240px) between the two
      bridged root groups on purpose (2026-07-07, so Morton-style orphan siblings have
      somewhere to land), which puts the partners about 360px apart, past
      `SpouseSpacingX` (200). Not changing the invariant. Doug to decide: accept the gap
      (then Invariant 4's bound needs an explicit cross-root exception, recorded as a
      decision) or only reserve the buffer when an orphan sibling actually needs it.
      *2026-10-12:* `FamilyTreeLayoutTransitionTests.OrphanSiblingMarries_NextToAFamilyWithACrossRootMarriage`
      skips `AssertSound` only because of this gap (Marc and Ellen 560px apart before and
      after the edit). Add it back in the PR that fixes R6.
      **Fixed 2026-10-12** on `fix/r6-r12-r2-split-couples` (not deployed yet). Doug
      approved "reserve the slack only when an orphan sibling needs it", but that turned
      out to break the R13 transition: an orphan sibling marrying stops being a root
      individual, the slack disappeared, and the right-hand family shifted 240px. What
      shipped instead: (1) the slack is still always reserved, but **after** a run of
      forced-adjacent families (and their R13 sibling followers) instead of between the
      two families the couple joins, so the orphan still has room and nothing moves when
      they marry; (2) after placement, a cross-root partner with nobody placed under them
      (a leaf) slides toward the other partner, never past the far side of their own
      parents (`CrossRootCouple_StillDetectedWhenOneSideIsNestedUnderAGrandparent` pins
      Marc between Bud and Florence), and only if they don't land on anyone, inside
      another couple, or with someone between the two of them. `AssertSound` is back in
      `OrphanSiblingMarries_NextToAFamilyWithACrossRootMarriage` (Marc and Ellen now
      200px apart); it fails on the old engine. Over 5,000 generated families, Invariant 4
      failures in cross-root families went 706 → 571 (all families 2,184 → 1,970) and
      Invariant 1 overlaps 884 → 799. The `CrossRootCouple` exclusion stays: the pull is
      still blocked when a sibling sits at the partner's inner edge, or when both
      partners head families of their own (fuzz seed 0..199 at `PinnedSeed` still
      finds such cases).
      *2026-10-16 (branch `fix/r7-r6-leftovers`):* the sibling-at-the-inner-edge block is
      fixed, in two pieces. (1) Step 3's inner-edge sort only reordered the group holding
      the cross-root partner directly. It now also sorts any child whose own descendants
      hold one, so the whole line down to the partner hugs the edge (Rose, Lauren's
      granddaughter through her middle child Florence, ended 240px from her wife because
      Florence's brother Bill held the edge;
      `GrandchildMarriesIntoTheNextFamily_HerParentsLineMovesToTheInnerEdge`). A subtree
      holding partners for both edges sorts to neither. (2) A single parent's attachment
      always hung on the right; it now hangs left when one of their children needs the
      right edge (Lauren's childless husband Doug sat between her son Max and Max's
      husband from her brother's family, 320px apart;
      `SingleParentsSonMarriesIntoHerBrothersFamily_HerHusbandHangsOnTheFarSide`). Both
      tests fail on the old engine in both GUID orders. Over 5,000 generated families
      (base seed 777): split couples 3,074 → 3,037, excess distance 1,049k → 1,037k px,
      overlaps unchanged at 250 (three families newly overlap, two stop; all three new
      ones are ~61px near-misses involving an orphan sibling or a spouse-anchored partner,
      R4's shapes). Still open: partners who both head families of their own. The
      `CrossRootCouple` flag stays on `Invariant4` for that.
      Also fixed on the same branch, found by the fuzz comparison: the sibling- and
      spouse-anchor passes never advanced the append position, so a person they placed
      past the end of the tree could have the next appended person drawn exactly on top
      of them (seed 154 at `PCG(154, 777)`, two people at distance 0). Latent on
      `master`; this branch's layout changes exposed it. Test:
      `OrphanSiblingPastTheEndOfTheTree_IsNotDrawnUnderAnAppendedPerson`, verified failing
      without the fix.
- [ ] **[LAYOUT FUZZ 2026-10-10] R7 — a spouse-anchored partner lands past an occupied
      slot.** The safety net anchors a partner with no group of their own (the 2026-07-07
      Harvey Fleishman shape, or anyone in 2+ couples) via `FindOpenXAdjacentTo`, which
      always tries rightward first and steps past whatever holds the adjacent slot. The
      Florence/Harvey fix only works when that slot is empty. Repros: Bud married to both
      Bud Lee and Harvey (childless) → Harvey lands past Bud Lee, 360px away, though the
      left slot is free. Ann (sibling Bill beside her) married Rose, and Ann has a child
      alone → Rose 240px away. Fix direction: try both sides and take the nearest free slot.
      Fails Invariant 4. *2026-10-12 (R11 fix):* a person's second partner (no parents,
      no other group) no longer goes through the safety net; they're placed on the shared
      person's free side. The safety net still places a third partner, and the partners
      in the Ann/Rose repro, and still tries rightward first.
      *2026-10-15 (branch `fix/r6-r7-cross-root-pull`):* both repros above already pass on
      `master` (R11 and R2 fixed them); they're now guard tests
      (`SecondChildlessMarriage_NewSpouseTakesTheFreeSideOfTheirPartner`,
      `SingleParentWithASiblingMarries_SpouseSitsBesideThem`). Fixed in
      `FindOpenXAdjacentTo`, used by both the sibling and the spouse anchor: (1) it
      searches both directions and takes the nearer free slot, the right one on a tie
      (`OrphanSiblingOfAMarriedMan_LandsOnHisFreeSide`: Morton was stepped 360px past
      Ray's wife with Ray's other side free; fails with right-first restored); (2) it
      judges a slot occupied at the placed person's own row against every node whose
      circle would come near theirs, not only nodes on exactly the anchor's Y (the R4
      half; Casey, Pat's third wife, was drawn on top of Blair). `ThirdMarriage_...` is
      un-skipped; it fails on the old engine. Its `AssertSound` no longer checks "nobody
      between partners" for a person in more than `maxCouplesPerPerson` couples, the same
      exemption the distance check already had: with both of Pat's sides taken, the third
      partner must have someone between. Fuzz: `SpouseAnchoredPartner` is off
      `Invariant1`'s exclusions (3,000 iterations pass; fails on the old engine). **Still
      open:** a partner bridging two families that each have a group of their own
      (Harvey married to Florence and Dora, `PersonMarriedIntoTwoFamilies_SitsBesideAWife`,
      skipped), which needs the families reordered like the cross-root step, and the
      flag stays on `Invariant4` for that and for 3+-couple people (the fuzz invariant has
      no per-person couple limit; its shrunk counterexample is Willa in three couples).
      *2026-10-15, later (branch `fix/r7-bridge-two-families`):* the Harvey case is fixed.
      Harvey wasn't going through the safety net: he was Dora's R11 attachment, and
      Florence's family sat on the far side of Max+Dora's. A parentless partner whose
      primary couple is someone's attachment now counts, in the cross-root check, as part
      of that partner's family, so his other marriage is a cross-root couple: the
      families are made adjacent and Florence turns to face them; Dora's couple turns so
      Harvey hangs on that side (Bud, Florence, Harvey, Dora, Max; 120px and 200px). The
      test is un-skipped and fails on the old engine in both GUID orders. Still open: the
      flag stays on `Invariant4` for 3+-couple people and for two partners who each head
      a family (Lauren, wife of Bill, formerly of Marc, 280px from Marc).
- [x] **[LAYOUT FUZZ 2026-10-10] R8 — an orphan sibling with a partner is anchored to the
      sibling, not the partner.** A person with no parents but an explicit Sibling link
      **and** a partner is placed by the root-individual sibling loop, which runs before
      the spouse safety net, so they end up 240px+ from their partner. Matches the
      "orphan sibling gains a spouse" classification transition. Fails Invariant 4.
      **No longer reproduced, 2026-10-17** (`fix/r8-orphan-sibling-partner`), fixed along
      the way by R13's sibling-follower sort and the R2/R7 attachments, with no engine
      change in this branch. Evidence: over 5,000 generated families (pinned seed), the
      engine before R13 (`ecb60be`) split 10 of 315 orphan-sibling couples where both
      partners are in one couple and no R2 shape is present; the current engine splits 0.
      Every remaining orphan-sibling split carries `PartnerWithChildrenElsewhere` (R2) or
      someone in 2+ couples (R7). The `SiblingLinkedPartner` flag is retired from
      `Invariant4`'s twin (passes at 5,000 iterations without it), and `Invariant4`'s skip
      reason no longer names R8. Pinned by
      `FamilyTreeLayoutTransitionTests.OrphanSiblingMarriesASingleParent_SitsBesideHisWife`
      (fuzz seed 834, simplified), which fails on `ecb60be` (Bill 440-640px from Rose)
      and passes now.
- [ ] **[LAYOUT FUZZ 2026-10-10] R9 — no vertical clearance check in `PlaceGroup`.** X
      comes from tree structure, Y from birth year, and nothing checks the combination. A
      married-in partner at their spouse's slot ±100px can sit right under the spouse's
      parent. Repro: Willa (1926) married Ann Kuh (1916). Willa's mother Ann Small (1906)
      renders 65px above Ann Kuh at the same X. Rare (about 1 in 3,000 generated families). Fails
      Invariant 1.
- [x] **[LAYOUT FUZZ 2026-10-10] R10 — cross-root couples' children can stack on the same
      X.** The post-placement pass drops each cross-root couple's children at the couple's
      midpoint with no collision check. With two cross-root couples side by side, Ellen Kuh
      (1970) and Ellen Rosenberg (1969) rendered at the same X, 7px apart. Fails
      Invariant 1.
      **Fixed 2026-10-15** on `fix/r7-bridge-two-families`: the pass tries the midpoint,
      then one slot further out at a time, alternating sides (up to 6 slots), undoing any
      try whose children land on an already-placed node; if none is clear the midpoint is
      kept, as before. New test `CrossRootCouplesChildren_DoNotLandOnTheirCousins` (Al and
      Bet's children landed on Bet's brother's children, 40px apart) fails without it.
      `CrossRootCouple` is off `Invariant1`'s exclusions. Over 5,000 generated families
      (base seed 777), compared with the engine before this branch: overlaps 262 → 189,
      split couples 1,742 → 1,735, no family's couples newly split. Three families
      newly overlap (seeds 1800, 2973, 4789): children with three recorded parents and
      people married into their parents' couples, where which marriages count as
      cross-root changed; not chased.
- [ ] **[LAYOUT FUZZ 2026-10-10] Possible extra invariants (not in the plan's 7):**
      "children sit within their parents' subtree span" (a child of a non-primary couple
      group goes through the safety net and can land far from both parents with no
      invariant noticing), and "nobody sits between two spouses" (reverting the
      2026-09-18 couple-gap fix goes unnoticed). Candidates for Leg 2. *2026-10-11:* both
      now asserted, on the small hand-built families in `FamilyTreeLayoutTransitionTests`
      only ("nobody between spouses" in `AssertSound`, "child under parents" in
      `AssertChildUnderParents`). Still not in the fuzz harness: a wide family legitimately
      spreads children past their parents, so the second one needs a real subtree-span
      bound first.

**Harness limitations, so a future reader isn't misled:** shrinking works well on
the full-domain generator (40-person failures came down to 3-5 people) but barely at all on
the `_ExcludingKnownOpen` domains (they're built with `Select`, so CsCheck can't shrink
through the filter). Those failures print the original case instead.

## Layout transition hunt: findings (2026-10-11)

Leg 2 of `docs/Testing/fuzz-and-adversarial-review-plan.md`. Tests:
`tests/FamilyTree.Web.Tests/ServiceTests/FamilyTreeLayoutTransitionTests.cs`. Each test
lays out one family before and after a single user edit, built from `Person` +
`Relationship` rows through `PersonMapper` and `CoupleHelper`. Every test runs with
ascending and descending GUIDs, because GUID order decides which partner of a couple sits
on the left.

**Covered and passing (now guarding the transition):** a marriage with children ends
(nobody moves); a single parent gains a co-parent; deleting a middle generation; merging
two trees with a Parent link; a Sibling link between two married parents; a dateless
family gains its first birth date (expected R3 here, but this shape doesn't trigger it);
focus moved to another tree or to a deleted id (nobody moves).

**Duplicates of open entries (tests skipped with a pointer):** a childless second
marriage ending leaves the ex as a separate tree (R1). A third marriage: the before state
is already wrong, since the second partner lands on the occupied right side (R7) and the
third lands on top of the second (R4).

- [x] **[LAYOUT TRANSITIONS 2026-10-11] R11 — a second couple gaining its first child
      can take over the primary group and move the first family.** `primaryGroup`
      prefers a group with children, then a couple, then list order (`CoupleSortKey`,
      the partners' position in the LastName/FirstName list). While Louis's second
      marriage (Libby) is childless, his first (Lillian, child Ray) wins. Once Libby has
      a child (Sid), both groups have children and "Libby" sorts before "Lillian", so
      Louis+Libby takes the slot. Lillian then goes through the spouse safety net, and
      Ray, whose group is no longer placed, is appended at the right edge. Ray's family
      jumps 320px when a half-brother is added. This is the real Louis/Lillian/Libby family
      from commit 073ae02, and its before state already overlaps (Libby lands on
      Lillian, R4 and R7), so production probably shows that now. Same root cause as R2
      (a couple that isn't primary for both partners is never placed as a group), but
      fixing R2 alone won't stop the flip: the primary choice also needs a tie-break
      that doesn't change when a group gains children (for example, the earliest marriage
      or the oldest child). Test:
      `ChildlessSecondMarriageGainsAChild_FirstMarriagesChildStaysUnderItsParents`.
      **Fixed 2026-10-12** on `fix/r11-second-couple-first-child` (not deployed yet).
      Two changes in `FamilyTreeLayoutEngine`: (1) among a person's couples that have
      children, the one with the oldest child is primary, so a later half-sibling can't
      flip it; (2) a second couple whose other partner has no parents on the tree and no
      better group (Libby, or Florence's Harvey) is placed as part of the primary group:
      the partner sits `SpouseSpacingX` out on the shared person's free side, and its
      children sit under that pair (pushed outward only if they'd hit the first family's
      children). The slot is reserved in `MeasureGroup` even while the second marriage is
      childless, so its first child moves nobody. That also fixes the before-state
      overlap (Libby on Lillian), since she no longer goes through the safety net. Test
      un-skipped and passing for both GUID orders, plus a new
      `SecondMarriageWithChildrenGainsAnother_FirstFamilyMovesOnlyAsABlock`; both
      verified failing against the pre-fix engine. Fuzz check over 2,329 generated
      families where someone is in 2+ couples (5,000 seeds): Invariant 1 failures
      850 → 666, Invariant 4 failures 2,100 → 1,826. 137 cases fail that passed before,
      all in still-open shapes: 130 are R2 (a partner with parents or children by
      someone else), 5 also carry R1/R6/R8, and 2 are a third marriage whose partner
      still goes through the safety net (R4/R7, `ThirdMarriage_...`). No KnownOpen flag
      changed: R2 and R7 are only partly fixed (see their notes).
- [x] **[LAYOUT TRANSITIONS 2026-10-11] R12 — a root parent can't turn toward a co-parent
      in another tree.** Pat (married to Alex, child Kim) has a child, Kit, with Blair,
      who is unmarried to Pat and is the daughter of a separate root couple. The
      cross-root step correctly puts Kit at the Pat/Blair midpoint, but Pat and Blair sit
      460-660px apart. The inner-edge sort only reorders a group's children, and Pat is a
      root group's parent, so which side of Pat+Alex he sits on is decided by
      `CoupleHelper`'s GUID order. With ascending ids, Alex sits between Pat and Blair.
      Related to R2 and R6. Test:
      `HalfSiblingWithAnUnmarriedCoParent_BothChildrenStayUnderTheirOwnParents`.
      **Fixed 2026-10-12** on `fix/r6-r12-r2-split-couples` (not deployed yet). A couple
      with a member in a cross-root couple is turned so that member faces the other
      family (the left family's member on its right, the right family's on its left), at
      any depth, the same way the existing step already sorts that member's slot to the
      inner edge of their parents' children. Then the R6 pull brings Blair beside Pat.
      Skipped when the person has an R11 second marriage attached (it hangs on their
      free side) or both partners want the same side. Test un-skipped; fails on the old
      engine, passes for both GUID orders.
- [x] **[LAYOUT TRANSITIONS 2026-10-11] R13 — DESIGN QUESTION: an orphan sibling who
      marries jumps to their own alphabetical root slot.** Unmarried, Bill (Sibling-linked
      to Ray, no parents) is a root individual anchored beside Ray. Married to Gish, the
      couple becomes a root group and takes its `CoupleSortKey` slot: "Bill" sorts before
      "Ray", so Bill+Gish move to the far left and Ray's whole family shifts 320px right.
      This follows from two deliberate choices in CLAUDE.md (root groups ordered by their
      own identity, 2026-07-06; never reordered by sibling links), so it's a design call,
      not a plain bug. Options: accept it, or let a root group whose only link into the
      component is a Sibling link take the slot after its sibling's root group (a sort
      key, not a reorder of placed nodes). The test also records a before-state overlap
      with ascending ids (Bill lands on Rose, R4). Test:
      `OrphanSiblingMarries_SpouseLandsBesideHimAndRaysFamilyDoesNotMove`.
      **Decided and fixed 2026-10-12** on `fix/r13-sibling-linked-couple-order` (not
      deployed yet). Decision (Doug, 2026-10-12): keep the couple beside the sibling's
      family rather than accept the jump. A root group whose parents have a Sibling link
      into another root group's family now sorts right after that group (the first such
      group in the current order, if siblings sit in several). It's a sort key applied
      before placement, after the lineage-side sort and before the cross-root step.
      Unmarried Bill already sat just right of Ray, so marrying keeps him on that side
      and moves nobody before him. Known limit: if the sibling sits deep inside a bigger
      family, the couple lands at the right edge of that whole family, not directly
      beside the sibling. Two groups that point at each other keep the first one's own
      slot. The test is un-skipped, and a new one
      (`OrphanSiblingMarries_NextToAFamilyWithACrossRootMarriage`) covers the cross-root
      step running after the new sort. Both fail on the pre-fix engine and pass for both
      GUID orders. The new test leaves out `AssertSound` until R6 is fixed (see R6). An
      ad-hoc fuzz comparison over 3,266 generated families with an orphan-sibling link
      (5,000 seeds) is neutral on the general invariants: Invariant 1 failures 829 → 829,
      Invariant 4 failures 1,751 → 1,753, Invariant 6 failures 6 → 6, with 49 cases fixed
      and 51 newly failing, all in still-open shapes (R4 orphan-sibling Y collisions,
      R2/R6/R8). Those invariants don't measure "a marriage moves nobody", which is what
      this fixes.
      *2026-10-12, follow-up on `fix/r6-r12-r2-split-couples`:* when two root families'
      parents are each other's siblings (Ray's family and Bill's), the shipped rule made
      whichever came first in the list follow the other, and list order changed when Bill
      married (a single parent sorts after couples, a couple by name), so who followed
      whom flipped. Now the younger sibling's family follows: a recorded birth date
      outranks an inferred one, then the earlier year, then list order. Caught by
      `MarryingAnOrphanSiblingRoot_DoesNotJumpTheirGroupPastUnrelatedRootsByAlphabeticalAccident`
      once the R6 slack moved.
- [ ] **Adoption is a dormant product gap, not a layout bug.** `RelationshipType.Adopted`
      exists, `RelationshipService` validates it and `GedcomExportService` exports it, but
      `PersonMapper` ignores it (an `Adopted` row produces no `ParentIds`/`ChildIds`), and
      `ParentGraph`'s loop check only reads `Parent` rows. Nothing writes an `Adopted` row
      today: `PersonService`, the AuthService linking flows and the import only create
      Parent/Spouse/Sibling, and no page injects `IRelationshipService`. So no layout
      test, since there's no reachable state. If adoption is ever added to the UI, the
      mapper, the loop check and the layout all need it in the same change. Until then,
      adoptive parents entered as plain `Parent` render correctly.

---

## Ideas: agents / MCP / RAG exploration (2026-09-18)

Not bugs — forward-looking feature ideas surfaced while diagnosing the layout bugs
below, kept here rather than on that bug-fix branch since they're unrelated scope.
Motivated by wanting real, non-contrived excuses to get hands-on with agents/MCP/RAG
inside an actual project, plus learning to curtail AI-assisted token consumption.

- [ ] **Agent: revive bulk import as a one-person-at-a-time agent.** The bulk-import
      feature (`docs/FutureFeatures/bulk-import-deactivated.md`, `ClaudeImportService`)
      was deactivated specifically because importing many people at once gave no
      per-item feedback loop — a GEDCOM/CSV import can contain dozens of the exact
      layout edge cases this session found one at a time. An agent that imports one
      person at a time, running each addition through `FamilyTreeLayoutEngine` (or a
      lighter simulation of it) as a check before committing and reporting back
      per-item, directly addresses the reason it was shelved rather than being a
      learning exercise bolted onto unrelated code.
- [ ] **RAG: semantic search over `Story.Body` / `Person.BiographyNotes`.** Both are
      free-text fields already in the domain model, full of exactly the kind of
      content keyword search handles badly (e.g. "escaped in Auschwitz," "owned a
      bakery"). Semantic search ("find mentions of Poland," "who might be related
      based on this bio") is a real feature, not a demo.
- [ ] **MCP (and token-reduction lesson): a narrow, purpose-built read tool.** A tool
      that returns just "this person's relationships as structured JSON" instead of
      requiring a full GEDCOM export or grep through the layout engine to reconstruct
      facts by hand — the same technique (small, purpose-built data access instead of
      reading whole files) is also the general token-reduction lesson from this
      session's GEDCOM-archaeology work.

## Production Bug Batch (found 2026-09-17, most fixed 2026-09-18)

8 bugs found live in production while inviting Aunt Linda to write a story and doing
general add/edit testing. 6 fixed and merged same day (PR #39,
`fix/production-bug-batch-sep17` — commits `b775baa` and `d13845e`). The other 2 share
a diagnosis but no confirmed fix yet — see below.

**Fixed (PR #39):**
- [x] **Sibling/relationship-picker dropdowns silently hid results past the 8th match**
      (`PersonForm`, `StoryInviteDialog`, `StoryFormDialog`, admin Stories-linking tab)
      — no indication more existed, easy to hit with a handful of same-surname
      relatives. Raised the cap to 25, ordered by relevance (name-start matches first).
- [x] **Max Small's placement was bad after adding him** — root cause: an orphan
      sibling (no shared parent on the tree) anchoring off another orphan sibling could
      land inside an *unrelated* couple's own connector gap, splitting their connector
      line. `Max` (Rose's & Fannie's orphan sibling) landed squarely between Ray and
      Rose. Fixed in `FamilyTreeLayoutEngine` by having spouse-gaps get reserved the
      same way node positions are. Regression test reproduces this exact family shape.
- [x] **3 toasts appeared ~5 minutes after adding Mo Small** / **double toasts adding 2
      people in quick succession** / **multiple toasts when editing** — all three were
      the same root cause: `ToastContainer` was rendered separately in `MainLayout`,
      `TreeLayout`, and `AuthLayout`. Navigating between pages that use different
      layouts disposed the old container mid-flight; its `Dispose()` cancelled each
      toast's dismiss timer without removing it from the shared `ToastService`, so the
      orphaned toast sat there forever and got "revived" with a fresh countdown by the
      next layout's container — sometimes minutes later. Fixed by rendering one
      `ToastContainer` instance in `Routes.razor`, outside the `Router`, so it survives
      every in-app navigation for the life of the circuit.
- [x] **Edit-person screen: header not actually centered, Delete button misaligned
      from the Cancel/Save row above it** — both genuine CSS bugs (dead flex properties
      with no `display:flex`; Delete button living outside `PersonForm`'s own 680px
      centered container). Fixed.

**Not fixed — same diagnosis, unconfirmed:**
- [ ] **[CRASH] Unhandled circuit exception while sending a story invite with no
      subject selected, and a separate "session expired" reconnect screen with the same
      red error banner.** Re-investigated 2026-09-20: read through
      `StoryInviteDialog.Send()` — the `personId is null` case (exactly "no story
      subject picked") already fails gracefully with an inline error + toast, and
      predates PR #39, so that guard isn't the cause. Live-reproduced the "invite about
      someone not on the tree, leave subject blank, hit Send" flow against a local build
      (fresh throwaway test account, not the real family data) — no crash, clean
      console. So the exact repro steps as described don't reproduce on current code.
      **Leading hypothesis**: the pre-fix `ToastContainer.Dispose()` (see the toast bug
      above) cancelled each toast's *display* timer via its `CancellationTokenSource`,
      but `BeginLeave()`'s own 280ms fade-out delay (`await Task.Delay(280)` before the
      final `StateHasChanged()`) was a *separate*, un-cancelled await — if a toast began
      leaving at the exact moment the user navigated to a different layout, that
      continuation would resume *after* the component was disposed and call
      `StateHasChanged()` on a disposed component, which is a textbook Blazor Server
      "unhandled exception on the circuit, circuit terminated" trigger. That matches the
      console log exactly (JS interop calls immediately afterward failing with "Cannot
      send data if the connection is not in the 'Connected' State"), and a terminated
      (not merely disconnected) circuit is exactly when a reconnect attempt gets
      *rejected* rather than resumed — which is the `.ft-reconnect-body--rejected` UI,
      i.e. the "session expired" screen. If this hypothesis is right, PR #39's move to a
      single circuit-lifetime `ToastContainer` (never disposed mid-session) already
      fixes this too, structurally, as a side effect — but this was **not confirmed
      against a real stack trace**: production has `DetailedErrors` off (the default;
      `Program.cs` never explicitly sets it), so the only signal available was the
      generic client-side message. Worth watching for a repeat after this branch is
      deployed rather than trying to force a live repro again.

## Regression Testing Hardening (2026-09-11)

Motivated by real family (some estranged for years) about to be invited to use the app,
and a recurring pattern this session set out to fix directly: shipping a feature, then
noticing a previously-working one had quietly broken. Branch: `testing/regression-hardening`.
Catalogued all 21 bugs found via manual/live testing across CLAUDE.md and this file — 14
already had regression tests; the 7 gaps split into two tracks. Full technical detail in
CLAUDE.md's "Testing & Quality Strategy" section — this entry is the checklist form.

**Track 1 — bUnit regression tests for already-fixed edge cases (no new infra):**
- [x] `PersonForm`'s tofu-glyph avatar-initials bug (empty name -> `\0` char)
- [x] `PersonForm`'s silent required-field no-op
- [x] `Register.razor`'s false "memory saved" toast on a plain (non-story) invite
- [x] `DeletedTab`'s restore-list-shift misattribution
- [x] `[Trait("Category","Regression")]` tagging convention + CI filter step

**Track 2 — real Playwright E2E harness (`tests/FamilyTree.E2E.Tests`):**
- [x] `E2eAppFixture`: real SQL Server (disposable), real external app process, real
      Playwright browser — not `WebApplicationFactory`/SQLite (see CLAUDE.md for why)
- [x] Wired up the 3 previously-dead Playwright stubs (permanently skipped, one with a
      hardcoded token no data backed) into real, passing tests
- [x] Mobile-viewport regression suite — closes 6 of 7 previously-uncovered mobile-CSS
      bugs from the mobile refinement round (touch targets, drawer height, close
      animation, the `?focus=` caret bug), the exact category bUnit structurally cannot
      see and manual browser-automation testing couldn't reliably reach either
- [x] `GoldenPathTests` — automates the Golden-Path Walkthrough below end to end
- [x] CI: new `e2e-test` job with a disposable SQL Server service container +
      Playwright's own generated install script

**Two small production fixes found via this work** (both real, narrowly scoped, not
test-only hacks — see CLAUDE.md and the branch's commits for detail):
- [x] `Program.cs` loaded user secrets in Development *after* `CreateBuilder`'s own
      environment-variable source, so an env-var override was silently shadowed by a
      developer's real secrets.json for the same key
- [x] `BlobServiceClient`'s dev-storage fallback used `?? "..."` (catches only `null`,
      not an explicitly empty string) — now `IsNullOrWhiteSpace`, matching the pattern
      already used elsewhere in the same file

**Not investigated this pass:**
- [ ] The Willa-scenario (subject-first, story-follow-up) linking flow has no E2E
      coverage yet — only the generic invite/anchor-linking flow does (`GoldenPathTests`)
- [ ] No visual/screenshot-diff testing — this pass is behavioral/computed-style
      assertions only, not pixel-level regression

## Mobile Refinement Round 2 (2026-09-10/11)

Real-phone testing (Android, via the user's own device) of the mobile nav drawer and
related surfaces, following up on the "mobile overhaul" (2026-08-08) and the site-quality
audit (2026-09-07) below. Branch: `fix/mobile-refinement-round2`. Full narrative and code
locations in `CLAUDE.md`'s "Mobile/Tablet Responsive Pattern" section — this entry is the
checklist form of the same work.

**Fixed:**
- [x] **Mobile drawer touch targets** — close button (27×26px) and nav rows (38px tall)
      both under the 44×44 minimum; now exactly compliant.
- [x] **Mobile drawer two-tone banding** — a decorative `::before` gradient sheen faded
      unevenly top-to-bottom across the panel; removed (same bug class as one already
      fixed once before, recurring in a new shape).
- [x] **Mobile drawer excess dead space** — `max-height: 55vh` left a large empty backdrop
      area under a short list; now `calc(100dvh - 76px)`, filling nearly the full screen.
- [x] **Mobile drawer had no closing animation** — instant DOM removal; added a real
      two-phase close (`ft-nav-close` keyframe + delayed unmount), plus `pointer-events:
      none` on the closing state as a safety net against a stuck/leftover overlay
      silently blocking clicks on the rest of the page.
- [x] **[REGRESSION] AppBar toolbar-reveal caret broken on any `?focus=` URL** —
      `CanRevealMobileToolbar`'s route check didn't strip the query string before
      comparing, so a normal shared/persisted-focus link wrongly read as "not home" and
      hid the caret. Found live comparing two browser sessions rendering differently;
      fixed and covered by a regression test verified to fail pre-fix, pass post-fix.
- [x] **People/Stories page header toolbars didn't fit a phone row** — search + filter +
      add-person controls needed ~789px in one non-wrapping row on People; now wraps.
      People's 5-column table also had no mobile treatment — contained to scroll
      horizontally within its own box instead of forcing the whole page to.
- [x] **PersonForm ignored phone width entirely** — every field pair used a fixed
      `MudItem xs="6"` with no `sm` override, so fields stayed half-width even on the
      narrowest phones. This is the shared Add/Edit form — the same flow that caused
      real frustration in an earlier session. All 10 fields now stack full-width below
      `sm` (~600px).

**Not investigated this pass** (surfaced but out of scope for a mobile-focused session):
- [ ] Canvas pinch/pan touch behavior on a real device — CLAUDE.md documents the
      Pointer-Events implementation as deliberate, not re-verified live here.
- [ ] Whether the "Complete your profile" button has any issue independent of the
      stale-session/caret bugs above — worked in every scenario actually reproduced
      this session, but not yet confirmed against a `?focus=` URL specifically.

## Site Quality Checklist Audit (2026-09-07)

Audited against `docs/SITE_QUALITY_CHECKLIST.md` in the sibling `career-development` repo
(the 68-item cross-project checklist built from real practices, not an invented rubric).
Full write-up in the session transcript; summary below. SEO/Analytics categories are
largely N/A — this is an invite-only private app, not a discoverable public site.

**Fixed this pass (low-risk, mechanical):**
- [x] Removed dead `AdminFirstAttemptBAD/` folder (6 files, unreferenced anywhere) — an
      abandoned first draft of the admin panel left in the tree since before this doc's
      earliest entries.
- [x] Removed `AdminOLD.razor` (1687 lines, routed at `/adminOLD` but linked from nowhere) —
      three separate earlier docs (`CODE_AUDIT_2026-07-03.md`, `OVERNIGHT_WORK_SUMMARY.md`,
      ADR 003) had already flagged this as safe-to-delete legacy; it just never happened.
      Confirmed unreferenced via grep before deleting, and updated those three docs to mark
      the item done rather than leaving them stale.
- [x] Added `Permissions-Policy` response header (`geolocation=(), microphone=(), camera=(),
      usb=(), payment=()`) — the app uses none of these; previously unset entirely.

**Deferred — need a scoped PR + manual testing, not safe to auto-fix:**
- [x] **[SECURITY] CSRF gap on `/auth/do-login` and `/auth/do-logout` (fixed 2026-09-15)** —
      `Program.cs`'s two minimal-API endpoints now call `IAntiforgery.ValidateRequestAsync`
      before doing anything else, rejecting a tokenless request with `?loginError=csrf`
      (do-login) or a clean no-op (do-logout) instead of proceeding. Every real caller
      switched from a JS-built, token-less hidden form onto a real `<form>` carrying
      `<AntiforgeryToken />`: `LoginOverlay`'s own sign-in form, `Register.razor`'s
      post-registration auto-login, and `CustomAppBar`'s sign-out (desktop menu + mobile
      drawer). `ftUtils.js`'s `ftSubmitLogin`/`ftSubmitLogout` (which had no way to carry a
      valid token, since it's minted server-side per-response) replaced with one generic
      `ftSubmitFormById` that just submits the already-rendered form. Also removed the
      unprotected `GET /auth/logout` endpoint entirely — the only remaining logout path is
      the protected POST. Verified live (real browser: register → auto-login → sign out →
      sign in) and via raw HTTP (forged requests rejected, valid-token requests pass
      through to real credential/session logic). Two new E2E regression tests
      (`CsrfProtectionTests`) — confirmed to actually fail against the pre-fix code (via a
      temporary revert) before confirming they pass against the fix, per this file's own
      verification discipline.
- [x] **[PERFORMANCE] No image resizing/re-encoding on upload (revisited 2026-09-16)** — this
      entry was stale: `PersonForm` and `PersonMedia` have compressed images client-side to
      1400px/JPEG-0.82 via `ftCompressImage` (a canvas resize) since 2026-06-09, well before
      this line was written; the original audit only checked `FamilyTree.Core` and missed the
      client-side JS interop path. The real residual gap, confirmed by reading
      `CompressOrReadAsync` in both callers: when client-side compression fails (canvas can't
      decode the file — e.g. some HEIC photos in browsers without native HEIC decode), both
      callers silently fall back to uploading the **original, unresized** file. Closed with a
      server-side backstop: `MediumService.ResizeIfOversizedAsync` (using `SixLabors.ImageSharp`
      3.1.11 — pinned below v4, which requires a paid license even for non-commercial use as of
      this pass) decodes any upload wider/taller than 2000px and re-encodes it in its original
      format before it reaches blob storage; anything already within bounds (the normal,
      client-compressed path) passes through untouched, so this only actually does work on the
      fallback-raw-upload case it exists for. Decode failures (corrupt file, or a format that
      slipped past the MIME allow-list) fall back to uploading the original bytes unchanged
      rather than failing the upload. Both real upload paths go through the same
      `MediumService.CreateAsync` chokepoint, so one change covers both. Regression tests:
      `MediumServiceTests` (small image passes through unchanged, oversized image resized to
      ≤2000px preserving aspect ratio, undecodable bytes fall back without throwing).
- [x] **[PERFORMANCE] No response compression (fixed 2026-09-16)** — `Program.cs` now registers
      Brotli + Gzip via `AddResponseCompression`, restricted to a static-asset MIME allow-list
      (CSS/JS/wasm/SVG/fonts) that deliberately excludes `text/html` and JSON: dynamic Razor
      Component responses carry per-request antiforgery tokens next to attacker-reflected query
      values (e.g. `Register.razor`'s `?email=` prefill), and compressing that combination is a
      BREACH-style compression-oracle risk — static assets carry no secrets, so they're safe.
      `/_blazor` is also explicitly excluded from the compression middleware branch
      (defense-in-depth; the MIME allow-list already wouldn't touch its negotiate/JSON traffic).
      Real, measured win, not theoretical: a Lighthouse pass against production before this fix
      (`performance: 62`) showed `MudBlazor.min.css` alone — 608 KiB uncompressed — was the
      single largest render-blocking resource (~3.1s of ~4.85s total render-blocking time,
      against a 1,099 KiB total page weight). A same-branch local Lighthouse run after the fix
      (not a fair head-to-head — localhost vs. production over the real internet — but
      directionally solid): `performance: 90`, total page weight 1,099 KiB → 385 KiB. Verified
      via curl that CSS/JS responses now carry `content-encoding: br` and the home page (`/`)
      does not; full E2E suite (incl. the CSRF regression tests, which exercise `/auth/do-login`
      and `/auth/do-logout`) still green with compression enabled.
- [x] **[POLISH] Theme flash on load (fixed 2026-09-16)** — rather than the cookie/SSR-plumbing
      approach this line originally suggested (bigger change, more lifecycle risk to verify
      solo before a live family demo), added `wwwroot/js/theme-init.js`: a small external
      script placed in `App.razor`'s `<head>` *before* the CSS `<link>` tags. It reads
      `localStorage['ft-theme']` and sets `data-theme` on `<html>` synchronously, before the
      browser loads `app.css`/`MudBlazor.min.css` — so the correct theme's CSS variables are
      already in effect on first paint, instead of only after `ThemeService.InitAsync()`
      resolves via JS interop in `OnAfterRenderAsync` (well after first paint). `ThemeService`
      itself is unchanged; it just re-applies the same value a moment later, a harmless no-op.
      Deliberately external rather than inline, consistent with the CSP hardening below (no new
      inline script to hash-pin). Known residual gap: MudBlazor's own `IsDarkMode`-bound state
      (`_isDark` in `MainLayout`/`TreeLayout`/`AuthLayout`) still only updates in
      `OnAfterRenderAsync`, so any Mud-internal styling driven directly by that C# bool (as
      opposed to the `data-theme` CSS variables, which are what our own custom styling uses)
      could still flash — not moved to `OnInitializedAsync` this pass, since that changes
      component lifecycle timing across three layout files and wasn't confidently verifiable
      live in the time available before tomorrow's family demo.
- [x] **[SECURITY] CSP still uses `script-src 'unsafe-inline'` (fixed 2026-09-16)** — audited
      every inline script/handler in the app first: the only ones were three raw
      `onclick="..."` attributes in `App.razor`'s `#components-reconnect-modal` (Blazor's own
      disconnected-state UI, which can't use `@onclick` since it renders before/outside the
      SignalR circuit `@onclick` depends on). Moved those to `id` attributes with listeners
      wired in `ftUtils.js` instead of hash-pinning them — removes inline script entirely
      rather than allowlisting a specific hash, so `script-src 'self'` now needs no exception at
      all. `style-src 'unsafe-inline'` is unchanged and deliberately out of scope: the app has
      dozens of dynamic, runtime-computed inline `style="..."` attributes (draggable toolbar/
      hero positions, tree node coordinates), which CSP hashing structurally can't cover since
      hashes only match static content. Verified live: no CSP violations in the browser console
      across the full login/register/reconnect-modal flow; full E2E suite green.
- [x] **[QA] Full 4-category Lighthouse audit against the live production URL (run 2026-09-16)**
      — see the response-compression entry above for the actual numbers (performance 62 →
      accessibility 84, best-practices 100, seo 91 at baseline). Re-run against production
      itself still pending an actual deploy of this branch; the 90-score local number above is
      the same code, different network conditions, not a substitute.
- [x] **[A11Y] Zero `:focus-visible` rules in `app.css` (fixed 2026-09-16)** — added one global
      `:focus-visible { outline: 2px solid var(--ft-focus-ring); ... }` rule (reusing the same
      accent already used for the tree canvas's selected-node glow) rather than per-component
      overrides. Needed `!important`: confirmed live via DevTools that `MudBlazor.min.css`
      (loaded after `app.css`) ships its own `outline: none` resets on `button`, `button:focus`,
      `a:focus-visible`, and most `.mud-*` interactive classes, which otherwise win on
      specificity alone. Verified the rule is the only `!important` outline declaration across
      every loaded stylesheet, so it wins regardless of selector specificity or load order —
      confirmed via one genuine real-Tab-key capture (before the `!important` fix, a keyboard-
      focused MudBlazor input correctly matched `:focus-visible` per the browser's own algorithm
      but rendered no outline). Could not get a fully deterministic repeat live screenshot
      through this session's browser-automation tool specifically (Tab-key-driven focus
      movement proved unreliable to simulate here — a tooling limitation, not a CSS one; see the
      Mobile Refinement entries elsewhere in this file for a precedented version of the same
      class of automation gap). **Known residual gap, deliberately not expanded into today**:
      several custom interactive elements across the app (`.ft-drawer-item`, `.custom-avatar`,
      `PersonNode`, etc.) are plain `<div @onclick="...">` with no `tabindex`, so they're not in
      the keyboard tab order at all — a `:focus-visible` outline can't help an element that
      never receives keyboard focus in the first place. That's a larger, structural
      keyboard-navigability pass (add `tabindex="0"` + Enter/Space handling across many
      components), out of scope for what this line asked for.

## Golden-Path Walkthrough (2026-09-07)

Live-tested the exact flow a newly-invited distant family member hits — invite →
register → account creation → tree-linking modal → landing on the tree — ahead of
sending real invites to a new branch of the family. Ran against a throwaway local DB,
DevAuth toggled on only to generate the admin invite, then off to test the real
anonymous registration path.

**Fixed:**
- [x] **False "memory saved" toast on plain account invites** — `Register.razor`'s
      welcome toast ("Welcome! Your memory has been saved...") fired for *any* valid
      invite, not just the "Willa scenario" (an invite following up on a submitted
      story). A brand-new user joining with a plain admin-generated invite — the
      normal case for inviting a new family branch — saw a confident, specific claim
      about "their memory" that was simply false. Gated the toast on
      `_storySubjectName != null` so it only fires when there's an actual pending
      story behind the invite.
- [x] **Broken "tofu" glyph in the Add Person avatar preview** — `PersonForm.razor`'s
      `Initials` computed `_firstName.FirstOrDefault()` / `_lastName.FirstOrDefault()`,
      which returns `'\0'` (not a printable fallback) when a name is still empty —
      visible as a broken-character box in the live avatar preview while typing,
      since MudBlazor's default (non-`Immediate`) text field binding only commits a
      field's value on blur, so the second name field genuinely is empty at first
      render. Reproduced live adding "Kip Kuh" as the tree's first person. Fixed by
      skipping empty segments instead of indexing into an empty string. (The same
      `FirstOrDefault()` pattern exists in 5 other places — `CustomAppBar`,
      `Dashboard`, `PersonDetailDrawer`, `PersonNode`, `SvgExportService` — but all of
      those read already-saved `PersonDto` names, which are required non-empty
      fields, so they can't hit this; left unchanged rather than over-scoping.)

**Confirmed working (no action needed):**
- Full invite → register → account creation → empty-tree landing flow works cleanly,
  including the pre-filled email banner and accurate "Account created" toast.
- The "add yourself via Add First Person, without completing the linking modal"
  edge case — which Admin's Users tab shows as "— Unlinked —" — is not a dead end:
  `Home.razor`'s existing recovery path (`ShowLinkingModalAsync` with
  `isRecovery: true`, gated on `_people.Any()`) correctly re-prompts with a clean
  "Complete your profile" card on the next visit once there's at least one person to
  link against. Confirmed live by logging in as the test account a second time.
- Mobile viewport (finally achieved a real sub-768px `window.innerWidth` this
  session, unlike a prior session's tooling limitation) — the AppBar correctly
  switches to hamburger + centered identity, the linking modal fits cleanly, and
  `document.documentElement.scrollWidth` exceeding `innerWidth` (from the tree
  canvas's absolutely-positioned content) is real but inert: both `html` and `body`
  have `overflow-x: hidden` set, so there's no user-visible horizontal scroll.

**Fixed (second pass, same day)** — prompted by "are there common-but-not-golden
paths worth testing too?":
- [x] **Add Person form silently did nothing on empty required fields** —
      `PersonForm.razor`'s `Submit()` checked `_firstName`/`_lastName` and returned
      on failure with no error shown (`Required`/`RequiredError` were declared on the
      `MudTextField`s but nothing ever triggered their validation UI, since that only
      fires on blur or an explicit `Validate()` call, not because a sibling button
      was clicked). A user submitting with an untouched name field would see the
      button appear to do nothing at all — no error, no toast, no navigation. Fixed
      by wiring `_firstNameError`/`_lastNameError` and binding them to `Error`/
      `ErrorText`, the same explicit pattern `Register.razor` already used
      correctly. Confirmed via a genuine reproduction (not a tooling artifact) by
      reading the code path, then verified live post-fix.

**False leads investigated and ruled out this session** (kept here so a future
session doesn't re-chase the same ghosts):
- A password-confirmation mismatch appeared to silently do nothing on submit
  across three careful attempts (no screenshot involved, checked DOM/console/
  server logs directly) — traced to `resize_window` to a very small viewport
  (375×812) desyncing the screenshot pixel scale from the actual CSS viewport
  (`devicePixelRatio: 2.5` compounding non-proportionally), so click coordinates
  read off a screenshot were landing entirely outside the real ~582×327px
  viewport. Re-verified at a normal window size — the mismatch validation works
  correctly and always did. Lesson: after any `resize_window`, check
  `window.innerWidth` against the screenshot's own pixel dimensions before
  trusting further coordinate-based clicks; prefer `find`-returned element refs
  over raw coordinates when a resize has happened recently.
- A `document.documentElement.scrollWidth` (1154) exceeding `innerWidth` (577) on
  a genuinely-achieved mobile viewport looked like a horizontal-scroll bug, but
  both `html` and `body` have `overflow-x: hidden`, so it's inert — confirmed via
  computed style, not just eyeballing.
- Adding a person while using the `DevAuth` bypass appeared to silently fail (the
  person "created" per server logs but never showing up in the People list).
  Traced to `DevAuthHandler` never issuing a `"FamilyId"` claim, which
  `CurrentUserService.FamilyId` requires — a testing-harness-only gap, since real
  registered users always get a real family via the invite/registration flow.
  Not fixed (out of scope — it's dev-tooling, not app behavior), just documented
  here so it isn't re-investigated as a user-facing bug next time.

**Confirmed correct (no fix needed), from the common-path pass:**
- An invalid/expired/garbage invite token on `/register` in production's
  `InviteOnly` mode does *not* bypass the invite check — the full signup form
  renders (no upfront "this link is invalid" banner, a minor polish gap, not a
  security issue), but submitting is correctly rejected server-side with a clear
  "This invitation is invalid, expired, or for a different email address."
  message. No account is created.
- Password-confirmation mismatch shows "Passwords do not match" correctly.

**Second follow-up pass (2026-09-08)** — completed the anchor-relationship
submission end-to-end (worked around the DevAuth/FamilyId gap by using two real
registered accounts instead): registered "Kip", had Kip add "Rose Kuh" as the
tree's first person, generated a second invite, registered "Grandma Kuh" through
it, picked "YES, I DO know someone" → Rose Kuh → "I'm their parent" → Continue.
The relationship was created correctly and Grandma landed focused on herself
with "Your family tree has 2 people" showing — functionally correct.

- [x] **[LAYOUT BUG, fixed 2026-09-09] Two related people with no birth date
      anywhere in the tree render exactly on top of each other.** Confirmed live:
      Grandma Kuh's node and label rendered directly on top of Rose Kuh's ("GK"
      circle with "Rose"/"Grandma" text overlapping illegibly). Root cause pinned
      down in `FamilyTreeLayoutEngine.ComputeBirthYears` (`Services/
      FamilyTreeLayoutEngine.cs:956-970`): the existing multi-pass relative-year
      inference (parent ≈ avg(child years) − 25, sibling ≈ avg(sibling years),
      etc.) only has something to propagate from once at least one person in the
      tree has a **real, known** `BirthDate` (Pass 0). When the entire tree has
      zero known birth dates — the normal state of a brand-new tree on day one,
      exactly what a first-invited distant family member like a Kuh would hit —
      `years.Any()` is false after passes 0-2, so the `else` branch
      (`FamilyTreeLayoutEngine.cs:965-970`) defaults **every** person to
      `DateTime.Now.Year` with no regard for parent/child/sibling/spouse links
      that could still establish *relative* spacing even without a real anchor
      date. The existing code already explicitly guards against a narrower
      version of this exact failure mode (see the Pass 1 comment about "two
      people with no birth date and no inferable link... render on top of each
      other") — this is the same bug class, just triggered when *no one at all*
      has a seed date rather than only the two unlinked people lacking one.
      **Fixed** by computing connected components *before* birth years (was
      after) and, when a component has no known birth date of its own, seeding
      one representative person in it so the existing relative-offset inference
      has something to propagate from — scoped **per component**, not "the
      whole tree has no dates", so a brand-new dateless branch grafted onto an
      already-dated family no longer collapses onto that unrelated family's
      median year either (a second, related collapse this fix also closes, not
      covered by the original bug report above). `FamilyTreeLayoutEngine.cs` —
      `ComputeBirthYears` (Pass 0.5) + `ComputeLayout` (Phase 2/3 reordered).
      Regression tests:
      `FamilyTreeLayoutEngineTests.EntirelyDatelessFamily_ParentAndChildDoNotRenderOnTopOfEachOther`
      and `.DatelessComponent_DoesNotCollapseOntoAnUnrelatedComponentsMedianYear`.
- No console errors observed during the full flow (checked via
  `read_console_messages`).

- [ ] **[LAYOUT BUG, not yet fixed, found live 2026-09-09] A root-individual sibling with
      an inferred birth year close-but-not-equal to the sibling they anchor next to can
      still render on top of them.** Reproduced live in the same dev DB used for the fix
      above: registered a plain account ("Ellen"), linked as Douglas's sibling via the
      generic "we're siblings" LinkToTreeModal path (no shared parent recorded). Douglas
      has a real `BirthDate` (1978); Ellen has none, so Pass 1 sibling-inference gives her
      Douglas's own year (1978) — NOT his sister Lauren's real year (1982), even though
      Ellen visually landed exactly on top of Lauren, not Douglas. Root cause is X-axis,
      not birth-year: `FindOpenXAdjacentTo`'s and `UnboundedExtend`'s collision checks
      (`FamilyTreeLayoutEngine.cs`, `n.Y == anchor.Y` — currently ~line 569 and ~590) treat
      two nodes as "colliding" only when their Y values are *exactly* equal (same row).
      Douglas and Lauren differ by 4 birth years × `PxPerYear` (6.5) ≈ 26px — far less than
      a node's own diameter (60-80px) — so Ellen, anchored next to Douglas at
      `Douglas.X + NodeSpacingX`, lands on an X slot the exact-Y check sees as free even
      though it visually overlaps Lauren's much-larger circle sitting a mere 26px above/
      below on the canvas. This is the same underlying class of bug as the two entries
      above it (an X/Y collision-avoidance check that's technically correct on its stated
      condition but doesn't account for a case just outside that condition), but a
      genuinely different code path and root cause from the birth-year fix above it —
      deliberately NOT fixed in the same change, per this file's own rule that each
      classification-transition bug gets its own scoped fix + before/after regression
      test, not a second fix riding along on an unrelated PR. Plausible fix direction:
      change the collision predicate from `n.Y == anchor.Y` to a vertical-distance check
      (`Math.Abs(n.Y - anchor.Y) < <node diameter at that depth>`), so near-miss Y values
      are still treated as an occupied row.
      **Update 2026-10-10:** the layout fuzz harness reproduces this as invariant 1. That
      fix direction is incomplete: check collisions at the placed person's own Y, not the
      anchor's. See `[LAYOUT FUZZ 2026-10-10] R4` at the top of this file.

**Not covered this pass** — worth a follow-up before or shortly after the real invite:
- Google OAuth sign-up path.
- A real physical mobile device (this session's viewport was emulated via window
  resize, not a real touch device).
- Duplicate-email registration (registering twice with the same address).

**Already solid, confirmed during audit (no action needed):**
Login rate limiting + lockout, dev-auth hard-blocked outside Development, branch protection
on `master`, real CI (build+test on every push, separate from manual-only deploy), 41-file
test suite across Core/Web including UI/component tests, `docs/Style-Guide.md` +
CSS-custom-property design tokens (no hardcoded-hex retrofit needed), custom 404 page,
mobile touch-target sizing (44×44px), secrets hygiene (`.gitignore` correctly excludes
`appsettings.Production/Staging.json`).

---

## Bugs noticed in testing on 28-June
- story invite lacks a correct register link
- how should stories appear for a typical user? in the detail panel, the plain text is probably not great --> should be a list
- can photos tag multiple people?
- should someone be able to add a photo to a story?


## In Progress / Up Next

- [x] **[CRITICAL — DO BEFORE INVITING MORE FAMILY] R0: a parent cycle crashes the whole web process (found 2026-10-10 by the layout fuzz harness). FIXED 2026-10-11 on `fix/r0-parent-cycle`: write paths reject cycles, the engine survives existing ones, `Invariant7b` un-skipped. Not deployed until this merges and `deploy-web.yml` is run by hand; write-up under `[LAYOUT FUZZ 2026-10-10] R0` at the top of this file.** A person with 3+ recorded parents, one of whom is their own descendant, makes `FamilyTreeLayoutEngine` recurse until the stack overflows, which kills the App Service process for every family. Fix on its own branch: reject cycles in `PersonService` (ancestor check on parent add) **and** make the engine's three recursions cycle-safe, because bad data may already exist. Un-skip `FamilyTreeLayoutFuzzTests.Invariant7b_ParentCycleDoesNotCrash` in the same PR. Full details: `[LAYOUT FUZZ 2026-10-10] R0` at the top of this file.
- [x] **Deploy** — pushed to production; user testing in progress at arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net
- [x] **[SECURITY — Critical] Family-scope check in `GetByIdAsync`** — `PersonService`, `RelationshipService`, and `MediumService` all lack a `FamilyId` guard on single-entity lookups; any authenticated user who knows a GUID can read across family boundaries
- [x] **[SECURITY — Critical] `DevAuth` production guard** — add a startup assertion that `DevAuth:Enabled` is false outside of Development environment
- [x] **[SECURITY — High] Lock `AllowedHosts`** — set to `arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net` in `appsettings.json`; dev overrides to `*` in `appsettings.Development.json`.
- [x] **[SECURITY — DO WHEN DOMAIN MOVES] Update `AllowedHosts` for `arborkin.com`** — ✅ completed 2026-07-29: updated `"AllowedHosts"` in `appsettings.json` to `arborkin.com;arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net` (keeping both for backwards compatibility)
- [x] **[DEPLOYMENT] Plan & execute arborkin.com domain migration (user ready 2026-07-26)** — ✅ completed 2026-07-29: app live at `https://arborkin.com`. Scope completed: DNS setup via GoDaddy CNAME record, SSL cert auto-provisioned (Azure App Service Managed Certificates), `AllowedHosts` updated, tier upgrade to B1 for custom domain support. Blog post: `docs/blogs/06-custom-domain-deployment.md`.
- [x] **[SECURITY — High] Add security response headers** — X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Content-Security-Policy added via middleware in `Program.cs`
- [x] **[SECURITY — High] Strengthen password policy** — `RequireDigit = true`, `RequireUppercase = true` enabled in `Program.cs`; `RequireNonAlphanumeric` left false to avoid UX friction; minimum length remains 10
- [x] **[SECURITY — Medium] Move reset/invite tokens out of URL** — reset-password and invite links now carry only an opaque DB-row `Guid` (`?id=`/`?invite=`) instead of the real Identity/invite token; the secret token never leaves the server, and `ResetPassword.razor`/`Register.razor` strip even the opaque id from browser history via `history.replaceState` once validated
- [x] **[SECURITY — Medium] Upgrade `Azure.Storage.Blobs`** — upgraded from `12.29.0-beta.1` to `12.29.0` stable
- [x] **[SECURITY — Medium] Surface `AuditLogService` failures** — already logs at `LogError` level in catch block
- [ ] **[SECURITY — Medium] MFA/2FA** — `security.md` documents this as required for Admin/SuperUser but it is not yet implemented
- [x] ~~**[SECURITY — DO BEFORE 2ND FAMILY] Family scoping gap** — fixed `AuthService.RegisterAsync` and Google OAuth callback to insert `UserFamily` row on registration~~ — **this entry was incorrect/stale.** As of 2026-07-04, `grep`ing the entire `src/` tree for `UserFamily` inserts found *zero* — neither `RegisterAsync` nor the Google callback ever created one. Actually fixed 2026-07-04 (see below); if this was truly backfilled in prod before, the registration code regressed since, or the backfill never shipped with a code fix.
- [x] **[SECURITY — Critical] Family scoping bypass via null FamilyId, and missing `UserFamily` creation (2026-07-04)** — two compounding bugs, found while testing the tree-linking flow: (1) `PersonService`/`MediumService`/`StoryService`/`GedcomExportService` gated the "super-user sees everything" bypass on `!currentUser.FamilyId.HasValue` alone — indistinguishable from a *regular* user whose `UserFamily` assignment is simply missing, who would then see every family's data instead of none. Fixed by adding `ICurrentUserService.IsSuperUser` (backed by the existing `SuperUser` role claim) and gating the bypass on that explicitly everywhere. (2) Neither `AuthService.RegisterAsync` nor the Google OAuth new-account path ever actually created a `UserFamily` row (see struck-through entry above) — every registered user had a null `FamilyId` claim, which is exactly the condition bug (1) mishandled. Fixed by inserting a `UserFamily` row at registration (using the invite's `FamilyId` if present, else the single default family), and added `IAuthService.EnsureUserFamilyAsync` for the Google path. Also added `PersonService.CreateAsync` fallback (`ResolveFamilyIdForCreateAsync`) so a super-user creating a person directly via "Add Person" doesn't leave it with `FamilyId == null` either — this is what caused Bill/Morton/Willa to end up ownerless during this session's testing. Backfilled local dev DB (56 people + 2 users). **Production almost certainly has the same gap — every invited real user (parents, sister, brother-in-law) may have `FamilyId == null` and needs the same `UserFamily` backfill once this deploys.** Regression tests: `PersonServiceTests.GetAllAsync_NoFamilyIdAndNotSuperUser_ReturnsNoPeople`, `GetAllAsync_SuperUser_ReturnsAllPeopleAcrossFamilies`, `CreateAsync_SuperUserWithNoFamilyId_AssignsNonNullFamilyId_AndReusesSameFamily`.
- [x] **[SECURITY — Medium] `RelationshipService` has no family scoping at all (2026-07-04)** — found while fixing the FamilyId bypass above; unlike Person/Medium/Story, `RelationshipService` never checked `FamilyId` on any method. Fixed by adding a `ValidateBothPeopleInScopeAsync` helper (super-users bypass; everyone else must have both people resolve to the caller's `FamilyId`), applied to `CreateAsync`, `UpdateAsync` (checked against both the existing relationship's people and the requested-new people — so a relationship you legitimately own can't be retargeted onto someone else's family either), and `DeleteAsync`. `GetAllAsync`/`GetForPersonAsync` scoped to match. Failures return a generic "not found" message rather than revealing cross-family existence. Regression tests: `RelationshipServiceFamilyScopingTests.CreateAsync_BothPeopleInCallersFamily_Succeeds`, `CreateAsync_OnePersonFromDifferentFamily_Fails`, `GetAllAsync_OnlyReturnsRelationshipsWithinCallersFamily`, `DeleteAsync_RelationshipInDifferentFamily_ReturnsGenericNotFound`.
- [x] **[SECURITY — Low] NuGet lock file** — `Directory.Build.props` added with `RestorePackagesWithLockFile = true`; `packages.lock.json` generated for all 5 projects
- [x] **GEDCOM export** — endpoint already wired in Dashboard; `GedcomExportService` writing standard `.ged` format from the current family's people + relationships (verified working 2026-07-26)
- [x] **Bulk import deactivated (2026-07-04)** — `/import` now shows a "temporarily unavailable" placeholder; removed every UI entry point (AppBar icon + mobile drawer, Dashboard quick action, Admin "Imports" tab). Code (`ImportFormPanel`, `ClaudeImportService`, `ImportsTab`, `ImportBatch`) left in place, unreferenced. A single test import this session rendered a "crazy"-looking tree — bulk-imported data can surface many of the same layout edge cases as this session's hand-entered testing, all at once, with no per-person feedback loop. See `docs/FutureFeatures/bulk-import-deactivated.md` for the reasoning and the suggested path back (likely dropping the birth-year timeline Y-axis for pure generational depth).
- [x] **Mobile/tablet responsive layout (2026-06-17)** — `@media (max-width: 768px)` breakpoint across AppBar (hamburger nav + centered user identity), person drawer (full-width — with a MudBlazor `--mud-drawer-width` variable gotcha fixed along the way), toolbar (defaults collapsed via a JS viewport watcher, expanded state docks as a full-width footer)
- [x] **Stories feature (2026-06-17)** — see `docs/FutureFeatures/stories-table.md` and `story-invite-flow.md` for what shipped (superseded `story-invite-email.md`)
- [x] **Azure Monitor alert for App Service health/5xx** — ✅ completed 2026-07-29: Three alerts configured via Action Group (availability < 95% over 5 min, any HTTP 5xx over 1 min, response time > 5 sec over 5 min). All alerts email `Ops:AlertEmail`. Complements the `Program.cs` try/catch+email for migration failures. See `docs/MONITORING.md`.
- [x] **Cache-busting for dynamically-imported JS** (`ftDrag.js`, `canvas-interaction.js`) — added `?v=1` query parameters to module imports (2026-07-26, PR #14); increment version when modules change to bypass browser cache during dev
- [ ] **Multiple-person tagging on media** — currently each photo is tied to a single `PersonId`. Add a junction table `MediaTag` (MediumId, PersonId) to allow tagging multiple family members in one photo. Change `Medium.PersonId` to optional (primary owner only). Update UI in `PersonMediaPage` and photo upload to support multi-select tagging. Phase 2 feature for collaborative photo curation.
- [ ] **Semantic duplicate detection in `LinkToTreeModal`** — when creating "Bill"/a new subject/connection person, warn if a person with a matching (or close) name already exists in the family instead of silently creating a second row. Deliberately not done in the initial subject-first linking flow (2026-07-04) — name-matching is fuzzy (two real people can share a name) and was judged not worth the false-positive risk for MVP. Ties into the existing Phase 4 "Duplicate detection | Merge similar people" line below; scope this specifically to the linking-modal creation paths (`AuthService.CreateUnlinkedPersonAsync`, `LinkUserToTreeAsync`'s new-person branch) rather than a general merge tool.
- [ ] **[SECURITY — High] Branch protection on `master`** — confirmed via `gh api repos/shalant/FamilyTree/branches/master/protection` that master is unprotected; most history is direct pushes, not PR merges. `Deploy Web` is already manual (`workflow_dispatch` only) and decoupled from push, but with no PR requirement a buggy direct-to-master commit (this is how the broken dark mode reached prod) can still ride along on the next manual deploy. Add a branch protection rule requiring a PR + passing `CI` check before merge to `master`.
- [x] **Mobile-responsive auth screens (fixed 2026-07-08)** — `LoginOverlay`, `Register`, `ForgotPassword`, `ResetPassword`, `StoryRespond` (the `.intro-overlay`/`.ft-auth-page` + `.intro-glass` auth card) weren't covered by the 2026-06-17 mobile responsive pass, which only touched the authenticated app shell. Turned out to be two layers: (1) a real bug, not just a missing breakpoint — both containers vertically centered the card via `align-items: center`, which clips content with **no way to scroll to the cut-off part** whenever the card is taller than the viewport (a well-known flexbox gotcha); `.ft-auth-page` had it worse, using `overflow: hidden` (zero scroll fallback at all) instead of `.intro-overlay`'s `overflow-y: auto`. Fixed on both by centering via `margin: auto 0` on `.intro-glass` instead of the parent's `align-items` — degrades gracefully to normal top-anchored, fully-scrollable flow the instant it doesn't fit, rather than clipping. (2) Even scrollable, size-dependent centering flip-flops between "centered" and "flush against the top" on phones, where the card is a much larger fraction of viewport height — reads as broken rather than responsive. Added a `@media (max-width: 768px)` block that drops centering entirely for one consistent top-anchored layout, plus shrinks the logo (80%/320px max → 55%/160px max) and tightens card padding so the card is short enough to rarely need scrolling on phone/small-tablet heights in the first place. Pure CSS (`app.css`), no C# changes, no rebuild required. Manually verified: normal desktop (unchanged), a short desktop window (the original cutoff case — now scrolls cleanly), and mobile device emulation (top-anchored, smaller logo, no longer floating off-center). Not covered by any automated test (no visual-regression tooling in this repo) — a future pass through `docs/Testing/InviteLinkingModelBranchTestingPlan.md`-style manual checks on a real phone before shipping is worth doing.
- [ ] **Suggest/auto-copy parent relationships when self-service linking as a sibling** — found 2026-07-06: a new person created via LinkToTreeModal's "we're siblings" path gets only a Sibling relationship row, no Parent link at all, even when the connected sibling already has parents on the tree. The layout engine now positions this case sensibly either way (see the `FamilyTreeLayoutEngine` root-individual sibling-anchor fix, same date), but the *data* is still incomplete — a self-service sibling isn't a real nuclear-family member without a shared-parent link. Needs to be a suggestion/confirmation step, not a silent copy — a genuine half-sibling only shares one parent, and sometimes the "sibling" truly has no recorded shared ancestry. Doug wants to use this as a hands-on exercise for understanding the relationship/DB model himself — not necessarily something to hand off for implementation.
- [ ] **[COSMETIC — known limitation] Sibling connector can cross an unrelated marriage line** — found 2026-07-06 via synthetic test data (`test`/`test2`/`test rosenberg`): `SiblingLink` renders as a single dead-straight SVG line between its two endpoints (`FamilyTreeCanvas.razor`), so if a third person (e.g. a sibling's new spouse) ends up positioned physically between two mutually-linked siblings, the dashed sibling line passes straight through that person's node and their own marriage connector. Not caused by missing gender data — gender plays no role anywhere in positioning or connector routing (ordering is always canonical lower-GUID, per `CLAUDE.md`). Root cause is the same deliberate "straight lines, never reorder" tradeoff already chosen this session for stability; this is a new concrete instance of it, not new bad architecture. Possible low-risk mitigation: give the sibling-line path a small vertical bow/offset when it would otherwise cross directly through another node's row — a rendering-only change, isolated from the stability-sensitive positioning logic. Deliberately deprioritized — good-enough-for-MVP, not worth polishing on a rare shape surfaced by synthetic test entities rather than real family data.
- [x] **Root-group left-to-right order wasn't actually stable — it was an artifact of alphabetical dictionary insertion order (fixed 2026-07-06)** — marrying "Bill" himself (an orphan-sibling root individual) to a new spouse "Gish" sent Bill's *entire* nuclear group (Gish, Bill, Willa, Tom) to the far left of the tree, past Bud+Florence, instead of Morton (Bill's actual neighbor) simply being pushed right as expected. Root cause: `CoupleHelper.Derive` built its couple list by iterating `PersonService.GetAllAsync()`'s people (sorted alphabetically by last name), and inserted each couple into a `Dictionary` at the moment it was first discovered via a *child's* shared-parent check or a spouse check — so a couple's position in that ordering depended on whichever of their relatives had the alphabetically-earliest last name, not on any genealogical or previously-rendered position. Here, Willa's last name ("Kuh") sorts before both "Rosenberg" and "Small," so the instant she gained a second parent (Gish), Bill+Gish's couple jumped to position 0. This is different from the `FamilyTreeLayoutEngineTests.GrowingAnOrphanSiblingsFamily_PushesTheNextSiblingRightWithoutOverlap` case, which marries Willa (Bill's *child*) to Tom — Bill himself never transitions from "individual" to "couple" there, so this ordering artifact never got triggered; only marrying the root individual directly does. Reconsidered priority 2026-07-06 — marrying an existing tree member is core, everyday MVP functionality, not a rare edge case, especially for a large tree. Fixed in `FamilyTreeLayoutEngine.BuildNuclearGroups` by sorting couples by their own identity (min position of the two partners in the people list) instead of dictionary-insertion order — leaves the couples-before-single-parent-groups structural precedence untouched, only fixes ordering *within* the couples. Regression test: `MarryingAnOrphanSiblingRoot_DoesNotJumpTheirGroupPastUnrelatedRootsByAlphabeticalAccident`. **Follow-up same night**: that new test was itself flaky (~40% failure rate across repeated runs, same inputs) — traced to a second instance of the exact same bug class in the cross-root-couple reorder step, which picked which of the two bridged root groups anchors the reorder by comparing the married couple's own canonical GUIDs (Marc vs. Ellen) — arbitrary, and could occasionally shove Bill+Gish's unrelated group to the very front depending purely on random GUID comparison. Fixed by anchoring on each root group's current position in the already-stably-sorted list instead of the couple's GUIDs. Verified with 23 consecutive clean runs (8 full Web suites + 15 targeted layout-engine runs) after the fix, vs. failures within the first few runs before it. Both fixes documented in `CLAUDE.md`'s "Root-Group Placement Order" entry. Full suite (66 Core + 16 Web) green.
- [x] **Admin "Deleted" tab: one Restore click could restore extra, unintended people (fixed 2026-07-07)** — clicking Restore on Gladys Rosenberg was followed by two more restores the user didn't consciously trigger (confirmed via audit log: Dora Herskovitz ~12s later, then Harvey Fleishman ~4s after that — a remembered "Ellen Rosenberg" toast didn't match anything in the audit log, and Ellen's `DeletedAt` was confirmed still null throughout). Root cause: `DeletedTab.razor` removed the restored row from the local list immediately (`_deleted.Remove(row)`), shifting every row below it up by one position — a later click, even seconds apart while scanning a long list (443 rows in this case), could land on whoever had since shifted into that same screen position. No button-disable or confirmation step made this worse. Fixed: a restored row now stays visible in place, marked "Restored" with its own button disabled, instead of disappearing — row positions stay stable for the rest of the viewing session; the list only shrinks on the next full reload. Also added a `IsRestoring`/`IsRestored` guard against double-firing the same row. Full suite (66 Core + 16 Web) green.
- [x] **Layout engine: a person with two recorded spouses could corrupt their real children's positions (fixed 2026-07-07)** — restoring Harvey Fleishman (found via the Restore bug above — Florence's second husband after Bud died, no children together, no `EndDate` recorded on the relationship) sent the whole Bud+Florence+Marc branch sprawling apart on the live canvas. Two compounding issues: (1) `primaryGroup` didn't prefer whichever of a person's groups actually has children, so the childless Harvey+Florence group could win Florence's "primary" slot purely because "Fleishman" sorts alphabetically before "Rosenberg" — fixed by preferring groups with children first. (2) Even after that, `rootGroups` still let the *other*, non-primary group compete for its own independent root-placement slot and lock in Florence's position via the wrong anchor before her real family was processed — fixed by requiring a group be the primary group of **every** parent it has, not just at least one (Harvey has no competing group of his own, so his group trivially passed an "at least one" check). Harvey himself now anchors next to Florence in the safety-net placement pass instead of landing at the far edge of the canvas. Regression test: `PersonWithTwoSpouses_ChildAnchorsUnderTheMarriageThatActuallyHasChildren` — was itself flaky before fix (2) landed; verified with 19+ consecutive clean runs after. Doug's read on this, worth keeping in mind for future layout work: incomplete/messy data (missing `EndDate` on a former marriage) shouldn't be able to corrupt an *unrelated* correct relationship's rendering — the goal isn't perfect handling of every dirty-data shape, but graceful degradation that doesn't cascade.
- [x] **Layout engine: cross-root couple detection stopped firing once one side was nested under a restored grandparent (fixed 2026-07-07)** — a fourth instance of the same classification-transition bug class, found while diagnosing the Harvey/Dora restore fallout. Restoring Dora Herskovitz made Bud her recorded child (Dora's spouse Louis is still deleted, so she's a single-parent group) — this flipped Bud+Florence's group from being a root itself to being nested one level down. Marc (Bud+Florence's son, married to Ellen — Ray+Rose's daughter) is the *same* cross-root marriage the engine has correctly detected and reordered for all session, but the old `directChildOfRoot` check only recognized a bridging couple when both partners were literal *direct* children of a root group's own `ChildIds` — Marc is now a grandchild of a root, not a direct child, so the check silently stopped firing. Both Dora's subtree and Ray+Rose's subtree then independently tried to place Marc and Ellen via ordinary recursion, and whichever ran first hijacked the other's position, stretching a stray connector across the canvas. Fixed by replacing the single-hop check with a `descendantOfRoot` map built by walking the same `primaryGroup`-driven recursion `PlaceGroup` itself performs, so a bridging marriage is detected regardless of nesting depth — no changes needed to the downstream adjacency-reorder or midpoint-placement steps, since those already operate on whole root groups. One known, documented limitation left in place: the "sort children to the inner edge" cosmetic polish still only applies at a root's own direct `ChildIds` level, so a deep cross-root couple lands correctly but without that same inner-edge polish (not incorrect, just less tidy). Regression test: `CrossRootCouple_StillDetectedWhenOneSideIsNestedUnderAGrandparent` — verified with 20 consecutive clean runs. Full suite (66 Core + 18 Web) green.
- [x] **[DATA INTEGRITY] Orphaned relationships from a Person soft-delete that bypassed the `DeleteAsync` cascade (found and fully fixed 2026-07-08/09)** — while manually testing "delete a person with relationships, then restore, confirm relationships come back too" (chosen because `PersonService.DeleteAsync`/`RestoreAsync` stamp cascade-deleted relationships with the person's exact `DeletedAt` timestamp, then restore only rows matching that timestamp — see `CLAUDE.md`), Marc Rosenberg's Sibling relationship to Elliot came back **duplicated**: one live row from the current Elliot, and one live row still pointing at an *older* Elliot person (`0CAF8540`) who is himself soft-deleted. Root cause, reconstructed from raw `CreatedAt`/`DeletedAt` timestamps in `People`/`Relationships`: the original Elliot (created 2026-06-07, same batch as Marc) went through several bulk-PDF-import-and-rollback cycles on 2026-06-24 — most of those rollbacks correctly matched person + relationship `DeletedAt` timestamps (clean cascade), but the final one (`13:59:03`, person-only) soft-deleted Elliot's `Person` row **without** touching his three original relationship rows (Sibling→Marc, Parent from Bud, Parent from Florence — all still `DeletedAt = NULL` at the time). That means some path in the import/rollback flow (`ClaudeImportService` or its rollback logic — bulk import is currently deactivated per the entry above, but its code and any residual `ImportBatch` data are still in place) deletes `Person` rows directly instead of going through `PersonService.DeleteAsync`, so the relationship cascade never ran. Not knowing the original Elliot still existed (soft-deleted, invisible in the UI) rather than being restorable, a second "Elliot Rosenberg" person was manually created on 2026-07-07 with the same Sibling/Parent relationships — so the tree carried two Elliot person rows (one dead but still relationship-linked, one live) doing the same job. **(1) done 2026-07-08** — the three orphaned rows (`0DAF8540` Sibling, `0EAF8540`/`0FAF8540` Parent) were soft-deleted directly via SQL, stamped with the same `DeletedBy` (Doug's `AppUser.Id`) as the original person delete; Marc now shows exactly one live relationship of each kind. **(2) done 2026-07-08** — root cause was `ClaudeImportService.RollbackBatchAsync`: it soft-deleted every `Person` in the batch but only cascade-soft-deleted a `Relationship` when *both* endpoints were in the batch, so a relationship linking a batch person to a pre-existing one (exactly Elliot→Marc) survived as a live orphan. Fixed to cascade on either side, matching `PersonService.DeleteAsync`; also had to rewrite it off `ExecuteUpdateAsync` (not supported by the InMemory EF provider this repo's tests use, and the only place in the codebase using that bulk-update pattern) onto normal tracked-entity updates so it could get regression coverage — 3 new tests in `ClaudeImportServiceRollbackTests`. **(3) done 2026-07-09** — the broader "add server-side validation" idea: new `IDataIntegrityService.FixOrphanedRelationshipsAsync` finds every live `Relationship` referencing a soft-deleted `Person` and auto-soft-deletes it to match (same correction as (1), just automatic instead of hand-run SQL). Hybrid detection/response, chosen deliberately over either extreme — flag-only would mean the same manual toil every time this recurs, pure auto-fix-and-forget would mask the early-warning signal that something is still bypassing `DeleteAsync`'s cascade (exactly the signal that led to finding (2)): runs automatically on every app startup (best-effort, never blocks startup, mirrors the existing migration-failure-email pattern) *and* is exposed as a manual "Run integrity check" button in Admin's Deleted tab; either path also writes one audit-log entry per fix, and a startup run that finds anything sends an email alert so the root cause still gets investigated rather than silently healing forever. Regression tests: `DataIntegrityServiceTests` (4 cases). 75 Core tests green.
- [x] **[UX GAP] A signed-in user's own linked Person being soft-deleted silently drops them onto an arbitrary stranger's tree (found and fixed 2026-07-08)** — surfaced while manually testing duplicate-person cleanup: deleting a Person who happens to be the currently-signed-in account's own linked person (`AppUser.PersonId`) doesn't clear that reference, and neither `Home.razor`'s `_currentUserIsUnlinked` computation nor `CheckAndShowLinkingModalIfNeededAsync`'s recovery-modal trigger could distinguish "never linked" (`PersonId` null) from "was linked, now dangling" (`PersonId` set, but the Person it points at is soft-deleted) — both only checked `PersonId.HasValue`, which stays `true` in the dangling case. The result: the app silently fell through to an arbitrary "first person" fallback with no error, no message, no relink prompt — the hero overlay just confidently displayed a total stranger's tree as if nothing happened. Fixed by extracting the resolvability check into `LinkedPersonResolver.IsResolved(personId, people)` (mirrors `PersonNameDuplicateMatcher`'s "pull logic out of the heavy Razor page so it's unit-testable" pattern) and using it in both places instead of `PersonId.HasValue` — a dangling `PersonId` now gets the exact same treatment as "never linked": the "Choose your starting point" empty state instead of a stranger's tree, and `CheckAndShowLinkingModalIfNeededAsync` auto-opens the recovery modal to reconnect. Also fixed a related pre-existing gap in the same function: `_anchorPersonId` (drives lineage-side tree ordering) was never refreshed after a successful recovery-link, so a user who linked via the recovery path kept a stale/null anchor even once properly linked. Regression tests: `LinkedPersonResolverTests` (4 cases — never linked, resolved, dangling, empty roster). Full suite green (71 Core + 41 Web).
- [x] **`LinkUserToTreeAsync` let a "is this you?" match crash on an already-claimed Person instead of failing cleanly (found and fixed 2026-07-08)** — surfaced while manually testing the new duplicate-detection modal's subject-first path: two different test accounts (`test4`, `test5`) both happened to be linked to a person named "Elliot Rosenberg" (two live, differently-created Elliot records from earlier testing this same session). The "is this you?" picker offered both as valid matches, but selecting *either* threw a raw `DbUpdateException` — `Cannot insert duplicate key row ... unique index 'IX_AspNetUsers_PersonId'` — because `AuthService.LinkUserToTreeAsync`'s existing-person branch never checked whether the target `Person` was already claimed by a *different* `AppUser` before writing; it just let SQL Server's unique constraint reject the save and let the exception bubble out as a raw error toast. This gap predates today's feature — the plain "are you already on this tree? pick yourself" flow (`LinkToExistingPersonAsync`) had the exact same hole, today's new duplicate-detection modal just happened to be the first code path to actually collide with it (real family data is unlikely to have two people who both already registered under the exact same name — this needed the session's accumulated test-account cruft to surface). Fixed two ways: (1) `LinkUserToTreeAsync` now checks `ctx.Users.AnyAsync(u => u.PersonId == personId && u.Id != userId)` before writing and returns a clean `"X is already linked to another account."` failure instead of ever reaching the DB constraint; (2) `LinkToTreeModal`'s duplicate-confirm picker now proactively filters out already-claimed candidates via a new `IAuthService.GetLinkedPersonIdsAsync` — only for the "is this you?" case (`ConfirmUserDuplicate`), not the story-subject case (`ConfirmSubjectDuplicate`), since a story subject like "Bill" is allowed to already have his own separate account. Regression tests: `LinkUserToTreeAsync_PersonAlreadyLinkedToAnotherUser_FailsCleanly_DoesNotThrow`, `LinkUserToTreeAsync_SameUserRelinkingSamePerson_StillSucceeds` (confirms re-linking yourself to the same person you already claimed still works — only a *different* user holding the `PersonId` is a conflict). Full suite green (71 Core + 37 Web).
- [x] **[TECH DEBT] `AuthService` doesn't follow the documented `ServiceResponse<T>` + try/catch pattern (found 2026-07-09, completed 2026-07-28)** — unified `AuthService` to return `ServiceResponse<T>` across all methods, added try/catch + logging to all methods, deleted `AuthResult`/`InviteResult` records. Updated 7 Razor components and test infrastructure. All 116 tests pass. (PR #17)
- [x] **Add a delete action to the People list and Person edit screens (found 2026-07-09, fixed 2026-07-26)** — `PersonDetailDrawer` now has full delete support. Added delete buttons to `People.razor` (per-row icon) and `PersonEdit.razor` (outlined button below form). Both route through `ConfirmDialog` with clear warnings. (PR #15)
- [x] **[SECURITY — Medium] No role-based authorization on high-leverage CRUD service methods (found 2026-07-09, completed 2026-07-29)** — implemented Admin vs Member role distinction; `UpdateAsync`/`DeleteAsync` on Person and Relationship gated on `IsSuperUser || IsAdmin`. (PR #18)
- [x] **Semantic duplicate detection with fuzzy name matching (completed 2026-07-29)** — PersonNameDuplicateMatcher upgraded from exact-match to Levenshtein-distance fuzzy matching with 80% similarity threshold. Catches typos and common variations while prioritizing exact matches first. (PR #19)
- [ ] **Gherkin/BDD test suite (pending)** — add SpecFlow or similar BDD framework for human-readable acceptance tests. Scope: cover the main user journeys (sign up, link to tree, add person, view tree) with Given-When-Then scenarios. Benefits: stakeholder-visible test documentation, regression safety as features grow.
- [ ] **Test coverage reporting in README (pending)** — integrate code coverage measurement (e.g., OpenCover or Codecov) into CI pipeline; display coverage badge and summary in README. Current status: 124 automated tests (75 Core + 49 Web) but no formal coverage metrics exposed yet.
- [x] **Mobile overhaul: touch/pinch-zoom on the tree canvas + combined mobile control panel (2026-08-08, on `feature/mobile-overhaul`, pending live-device verification before merge)** — production mobile was reported as unusable: couldn't scroll/pan the tree, navbar looked broken, toolbar was absent. Root cause for the pan/zoom issue was concrete: `canvas-interaction.js` was 100% mouse+wheel-driven with zero touch/pointer handling, while `.ft-viewport`'s `touch-action:none` blocked the browser's native touch fallback too — touching the canvas did nothing at all. Fixed by migrating the whole file to Pointer Events (single code path for mouse/touch/pen; `setPointerCapture` removes the old `window`-level mousemove/mouseup fallback; a second simultaneous pointer drives pinch-to-zoom via the existing `_applyZoomAt` zoom-toward-a-point math). Separately, while scoping the navbar/toolbar complaints, decided to consolidate the hero overlay (previously `display:none` on mobile) and the floating toolbar's inline expand into one `MobileControlPanel` (focus-person info + Zoom In/Out/Center/Reset), opened via the AppBar identity tap or the toolbar's mini pill — both routed through `TreeContextService` (extended with `RequestZoomIn`/`RequestZoomOut`/`RequestResetView`/`ToggleMobilePanel`/`CloseMobilePanel`, mirroring the pre-existing `RequestCenterTree` pattern). This also removed a confirmed dead affordance: `CustomAppBar`'s mobile identity block had `pointer-events:none` with an unwired `ToggleContext()`/`_contextExpanded` behind it — a real candidate for "the navbar looked broken." **Known gap**: the actual root cause of the navbar complaint (if it wasn't just the dead tap target) was never confirmed live — the available browser automation this session couldn't shrink its render viewport to actually trigger the `max-width:768px` breakpoint, so Phase 0 of the plan (reproduce-then-fix) couldn't run as intended. Needs a real device or real DevTools responsive-mode check before/shortly after merge. 12 new bUnit tests (`CustomAppBarTests`, `CustomToolbarTests`, `MobileControlPanelTests`); full suite green (75 Core + 60 Web, 4 pre-existing skips unrelated).
- [ ] **`ftDrag.js` touch/pointer support for repositioning the toolbar/hero (deprioritized 2026-08-08)** — `ftDrag.js`'s drag implementation is still mouse-only (`mousedown`/`mousemove`/`mouseup`). Zero live impact today: the draggable grip is hidden on mobile, and the mobile toolbar trigger's position is now CSS-anchored (not user-draggable) per the mobile-overhaul entry above. Only worth doing if mobile drag-to-reposition becomes an actual feature request.
- [ ] **Confirm the mobile navbar fix on a real device/DevTools (follow-up to mobile overhaul above)** — this session's browser automation couldn't emulate a real mobile viewport (`window.innerWidth` never actually shrank despite the tool reporting success), so the navbar/toolbar diagnosis in the mobile-overhaul entry above is unconfirmed. Load `/` at ≤768px width on a real phone or DevTools responsive mode and verify: hamburger menu, AppBar layout, `MobileControlPanel` opening from both the identity tap and the canvas mini-trigger, no console errors.

---

## Recently Completed

- [x] **Naming conventions reviewed (2026-06-17)** — audited table/column naming after adding `Stories`; kept PascalCase table names, plural table nouns (`People`, `Media`, `Stories`, etc.) with singular C# entity classes, and plain `Id` PKs. This already matches standard .NET/EF Core + SQL Server convention (lowercase-snake_case is Postgres/Rails advice, not applicable here; `TableNameId`-style PKs would conflict with ASP.NET Identity's own `Id` columns). No renames needed.
- [x] **Admin Imports tab** — replaced placeholder alert with full `ImportFormPanel` component; extracted import UI (GEDCOM/PDF/CSV/paste tabs + action bar) into shared `ImportFormPanel.razor`; used in both `/import` page and admin tab; import history table preserved below the form; Blazor stale-circuit reconnect modal also added
- [x] **Dashboard** — `/dashboard` hub with family stats (total/living/generations/photos), quick actions, recently added list, "Coming soon" export section, feature-request and contact-admin buttons
- [x] **Import family data** — `/import` page with GEDCOM / PDF / CSV / paste-text tabs; `IImportService` (`ClaudeImportService`) backend; `ImportBatch` model + `AddImportBatch` migration; preview-before-commit flow
- [x] **Feature request backend** — `UserMessage` DB model + `AddUserMessages` migration; `IUserMessageService.SubmitFeatureRequestAsync`; Admin panel "Messages" tab shows all submissions with type badge (Feature / Message)
- [x] **Password reset email** — `IEmailSender` abstraction; `SmtpEmailSender` (Gmail SMTP) + `LogEmailSender` (dev console fallback); `AuthService` sends reset link automatically; Gmail App Password configured in Azure
- [x] **Family scoping** — `ICurrentUserService` (interface in Core, impl in Web); `FamilyId` claim baked into auth cookie; all service queries scoped; `CreatedBy`/`UpdatedBy`/`DeletedBy` stamped on every write; super-users bypass scoping
- [x] **DB performance indexes** — filtered composite indexes on People, Relationships, AuditLogs via `AddPerformanceIndexes` migration
- [x] **Testing** — 11 xUnit tests with InMemory EF; covers create/audit stamping/family scoping/soft delete/restore/canonical ordering/duplicate prevention
- [x] **Auth** — ASP.NET Core Identity with cookie auth; Google OAuth + email/password login; invite-only registration (`Auth:RegistrationMode`); rate limiting on `/auth/do-login` (5 req/15 min/IP); account lockout after 5 failed attempts; super-user bootstrap on startup; `LoginOverlay`, `Register.razor` with `?invite=<id>` query param (opaque invite-row id, not the raw token); `DevAuth` bypass for dev; `Admin.razor` with dashboard, deleted people, audit log, users, and activity tabs
- [x] **SVG export — multi-style/theme** — `ExportDialog` updated with Style (Classic/Minimal/Dark), Color Theme (Forest/Ocean/Sepia/Mono), and Degrees of Separation slider; BFS filter to include only people within N hops of focus person; `SvgExportService` rewired with 12 hand-tuned palettes, radial gradients, drop shadows, decade bands (Classic), and flat rendering (Minimal/Dark)
- [x] **Focus persisted to DB** — `AppUser.FocusPersonId` column added (migration `20260609165520_AddUserFocusPerson`); `IAuthService.SaveFocusPersonAsync` / `GetFocusPersonIdAsync`; `ResolveFocusAsync` chains DB → localStorage → first person; login/Google OAuth redirects to `/?focus=<id>` using `FocusPersonId ?? PersonId`
- [x] **Search opens drawer without re-centering** — search now navigates to `/?view=<id>` instead of `/?focus=<id>`; `OnParametersSetAsync` handles `View` param (opens drawer only) separately from `Focus` param (re-centers tree + opens drawer)
- [x] **CI/CD split** — `deploy-web.yml` is now `workflow_dispatch` only (manual deploy); new `ci.yml` runs build + test on every push to `master`/`main`; `deploy-api.yml` deleted (leftover from old separate-API architecture — Core is now a class library, not a deployable)
- [x] **Pre-auth iteration** — soft delete on Person/Relationship/Medium (`DeletedAt`/`DeletedBy` + EF Global Query Filters); `AuditLog`, `UserActivity`, `AppUser`, `UserFamily`, `UserInvite` tables created; `IAuditLogService` writes on Create/Update/Delete/Restore; `PersonService.DeleteAsync` is now a soft delete; `PersonService.RestoreAsync` + `GetDeletedAsync` added
- [x] **Export dialog (initial)** — toolbar FileDownload button opens `ExportDialog` with scope (all/immediate/ancestors/descendants) and format (JSON/CSV); downloads via `ftDownloadFile` JS; RFC 4180-compliant CSV
- [x] `FamilyId` schema migration — `Family` table added; `Person.FamilyId` (nullable FK); seeder creates "My Family" and assigns all seeded people to it
- [x] Cross-root couple layout fix — children of two sibling-in-law families (e.g. Rose/Ray and Bud/Florence) no longer tangle horizontally; each partner placed as leaf under own parent group, children centered at couple midpoint
- [x] Former/divorced spouse support — `Relationship.EndDate` distinguishes active (null) from former (set); `PersonDto.FormerSpouseIds` added; dashed grey 💔 connector vs. solid green ❤ in canvas; full UI in `PersonForm`
- [x] Classic pedigree T-bar connectors — replaced bezier arcs with straight horizontal couple lines, vertical stems, T-bars, and vertical drops
- [x] Draggable toolbar and hero overlay — Blazor owns position state; JS calls `OnDragEnd` on mouseup; positions persisted to `localStorage`
- [x] Reset button on toolbar — clears drag positions, removes `localStorage` entries, centers tree
- [x] Focus persistence — `localStorage` saves the focused person's Id; survives page refresh; URL `?focus=<id>` overrides and re-saves
- [x] Shareable link — "Share" button copies `/?focus=<id>` URL to clipboard
- [x] Sibling inference dialog — after adding a sibling, offers to link that sibling's existing siblings
- [x] `QuestionDetector` — extracts questions from biography notes and surfaces them as toasts post-save
- [x] Blob storage — Azure Blob Storage integrated for photo uploads via `BlobStorageService`
- [x] Multi-photo upload with primary photo selection
- [x] All person fields saved (birthplace, gender, maiden name, biography, etc.)
- [x] `ChildIds` and `SiblingIds` persisted in relationship sync
- [x] Dark/light mode toggle with `localStorage` persistence

---

## Vision: A Family Tree App That Tells Stories

### Phase 1 — Core foundation ✅ (largely complete)
| Area | Status |
|------|--------|
| Data model — Person, relationships, notes, photos | ✅ done |
| CRUD UI — add/edit/delete with clean forms | ✅ done |
| Tree view — zoom, pan, focus, T-bar connectors | ✅ done |
| Theme — light/dark mode | ✅ done |
| Hosting — Azure App Service + Blob Storage | ✅ done |
| Accounts — sign in (Google, role-based) | ✅ done |
| Privacy — private tree by default | ✅ done |

---

### Phase 2 — Personalization & storytelling
| Area | Features 
|------|----------|
| Profile cards | Biography, photos, life events |
| Events table | Structured life events per person (bar mitzvah, immigration, military, etc.) — see `FutureFeatures/events-table.md` |
| Stories table | ✅ done — see `FutureFeatures/stories-table.md` |
| Timeline view | Chronological events + stories per person — see `FutureFeatures/person-timeline-view.md` |
| Media | ✅ Multi-person photo tagging (in backlog); Documents, voice clips (photos already done) |
| Design polish | Smooth transitions |
| Search & filter | By name, year, or relation |
| Responsive layout | ✅ done (2026-06-17) — mobile-friendly tree and forms |

---

### Phase 3 — Collaboration & sharing
| Area | Features |
|------|----------|
| Story invite flow | ✅ done — see `FutureFeatures/story-invite-flow.md` |
| Share a memory / Public profiles | Logged-in family members submit memories; optional public read-only profile URL — see `FutureFeatures/share-memory-public-profile.md` |
| Invites | Share tree with family (viewer/editor roles) |
| Change history | "Recently added/updated" feed |
| Comments | Collaborative memories on person profiles |
| Notifications | Birthday reminders, new photo alerts |
| Public view | Optional public tree with privacy controls |
| Real-time presence | See who else is browsing concurrently |

**Admin dashboard ideas (from 2026-06-06 brainstorm):**
- Track all changes with audit log
- Admin can disable features per user or lock out users
- Optional daily CRUD cap (e.g. 10 ops/day) with ability to request more
- Manual "donor" flag (e.g. Venmo confirmed) unlocking additional privileges
- Visibility limit by relationship depth (e.g. only see people within 3 links) — complex but possible via BFS on the graph

---

### Phase 4 — Intelligence & insights
| Area | Features |
|------|----------|
| Relationship inference | Auto-detect likely missing links |
| Duplicate detection | Merge similar people |
| Age & generation analytics | Oldest ancestor, average lifespan, etc. |
| AI suggestions | "Would you like to add Mary as Emma's grandmother?" |
| Export/import | GEDCOM, PDF, CSV |

---

### Phase 5 — Growth & polish
| Area | Features |
|------|----------|
| Themes | Family crest, color palette, custom fonts |
| Performance | Lazy loading for large trees |
| Localization | Multi-language support |
| Mobile layout | Improved canvas and form experience on phone |

---

## Optional "wow" features
- **Timeline slider** — drag to filter visible generations by year range
- **Animated connectors** — subtle pulse along parent lines when focusing
- **Photo avatars** — circular crops with soft shadows replacing initials
- **GEDCOM import/export** — interoperability with standard genealogy tools
- **AI-assisted suggestions** — relationship completion from biography text



- Introductory demo