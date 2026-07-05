-- ============================================================================
-- BACKFILL: UserFamilies + Person.FamilyId gap (2026-07-04)
-- ============================================================================
-- DO NOT RUN AGAINST PRODUCTION WITHOUT REVIEW. Doug must read the SELECT
-- output in Step 1 and Step 2 and confirm the counts/mappings look right
-- BEFORE running the UPDATE/INSERT statements in Step 3 and Step 4.
--
-- Context: neither AuthService.RegisterAsync nor the Google OAuth new-account
-- path ever inserted a UserFamilies row (fixed in code this session — see
-- docs/TodoList.md, 2026-07-04 entries). Every real user who registered
-- before that fix has NO UserFamilies row, so their auth cookie carries no
-- FamilyId claim. Separately, any Person created directly by a super-user
-- before PersonService.ResolveFamilyIdForCreateAsync existed may have
-- FamilyId == NULL. This script backfills both, using the SAME fallback
-- rule the code now uses: the oldest Family row by CreatedAt.
--
-- Safe to re-run: every write step only touches rows that are currently
-- missing data (WHERE NOT EXISTS / WHERE FamilyId IS NULL), so re-running
-- after a partial run just picks up whatever's still missing.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;

-- ----------------------------------------------------------------------------
-- STEP 0 — sanity check: how many Family rows exist?
-- If this is >1, STOP. The blind "oldest family" fallback below is only safe
-- when there's exactly one family in the system (true in local dev, and
-- presumably true in prod today since the 2nd-family split was a local-dev-
-- only test). With 2+ families, some of these users/people need a real,
-- judgment-based mapping (e.g. via which invite they registered from, or
-- which existing person they're related to) instead of a single fallback.
-- ----------------------------------------------------------------------------
SELECT COUNT(*) AS FamilyCount FROM Families;
SELECT Id, Name, CreatedAt FROM Families ORDER BY CreatedAt;

-- ----------------------------------------------------------------------------
-- STEP 1 — preview: users missing a UserFamilies row
-- ----------------------------------------------------------------------------
SELECT u.Id AS UserId, u.Email, u.DisplayName, u.PersonId, u.IsSuperUser, u.CreatedAt
FROM AspNetUsers u
WHERE NOT EXISTS (SELECT 1 FROM UserFamilies uf WHERE uf.UserId = u.Id)
ORDER BY u.CreatedAt;

-- ----------------------------------------------------------------------------
-- STEP 2 — preview: people with a NULL FamilyId
-- ----------------------------------------------------------------------------
SELECT Id, FirstName, LastName, CreatedAt
FROM People
WHERE FamilyId IS NULL
ORDER BY CreatedAt;

-- ============================================================================
-- Everything below this line WRITES data. Confirm Step 0/1/2 output first.
-- ============================================================================

BEGIN TRANSACTION;

DECLARE @DefaultFamilyId UNIQUEIDENTIFIER =
    (SELECT TOP 1 Id FROM Families ORDER BY CreatedAt);

IF @DefaultFamilyId IS NULL
BEGIN
    RAISERROR('No Family row exists at all — cannot backfill. Investigate before proceeding.', 16, 1);
    ROLLBACK TRANSACTION;
END
ELSE
BEGIN
    -- ------------------------------------------------------------------------
    -- STEP 3 — backfill NULL Person.FamilyId to the default family
    -- ------------------------------------------------------------------------
    UPDATE People
    SET FamilyId = @DefaultFamilyId
    WHERE FamilyId IS NULL;

    PRINT CONCAT('People rows backfilled: ', @@ROWCOUNT);

    -- ------------------------------------------------------------------------
    -- STEP 4 — insert missing UserFamilies rows
    -- Prefer the FamilyId of the user's own linked Person (now guaranteed
    -- non-null after Step 3) over the blind default, in case a future run of
    -- this script is used after a 2nd family already exists.
    -- ------------------------------------------------------------------------
    INSERT INTO UserFamilies (UserId, FamilyId, Role, JoinedAt)
    SELECT
        u.Id,
        COALESCE(p.FamilyId, @DefaultFamilyId),
        CASE WHEN u.IsSuperUser = 1 THEN 'Admin' ELSE 'Member' END,
        SYSUTCDATETIME()
    FROM AspNetUsers u
    LEFT JOIN People p ON p.Id = u.PersonId
    WHERE NOT EXISTS (SELECT 1 FROM UserFamilies uf WHERE uf.UserId = u.Id);

    PRINT CONCAT('UserFamilies rows inserted: ', @@ROWCOUNT);

    -- Review the counts printed above, then either COMMIT or ROLLBACK.
    -- Left uncommitted deliberately — do not add a blind COMMIT here.
END
