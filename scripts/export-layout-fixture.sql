-- export-layout-fixture.sql  --  READ-ONLY. Changes no data.
--
-- Exports one family as a layout fixture (ADR 004, step 1): the people and relationships the
-- tree is drawn from, in the JSON shape tests/FamilyTree.Web.Tests/LayoutQuality/LayoutFixture.cs
-- loads. Only what the layout engine reads is exported: ids, first and last names, birth
-- dates, and relationship type, end date and whether it ended (IsEnded). No places, notes, photos or emails.
--
-- The output still holds real names and birth dates, and the repo is public, so save it ONLY
-- under tests/FamilyTree.Web.Tests/LayoutQuality/Fixtures/private/ (gitignored). Full steps
-- are in docs/Testing/layout-quality-gallery.md. Example with sqlcmd (-y 0 stops sqlcmd from
-- cutting the JSON off; the header lines it prints above the JSON are skipped by the loader):
--
--   sqlcmd -S <server> -d <database> -G -y 0 -v FamilyId="<family guid>" ^
--     -i scripts/export-layout-fixture.sql ^
--     -o tests/FamilyTree.Web.Tests/LayoutQuality/Fixtures/private/live.json
--
-- It mirrors PersonService.GetAllAsync: live (not soft-deleted) people in the family, and
-- live relationships whose two people are both among them. To choose who the tree is
-- focused on, add "focus": "<person guid>" to the JSON afterwards; without it the
-- alphabetically first person is the focus. (Focus sets node sizes: the focused person and
-- their immediate relatives are drawn larger.)

SET NOCOUNT ON;

DECLARE @FamilyId uniqueidentifier = '$(FamilyId)';

DECLARE @json nvarchar(max) = (
    SELECT
        JSON_QUERY(ISNULL((
            SELECT p.Id AS id, p.FirstName AS firstName, p.LastName AS lastName, p.BirthDate AS birthDate
            FROM dbo.People AS p
            WHERE p.FamilyId = @FamilyId AND p.DeletedAt IS NULL
            ORDER BY p.LastName, p.FirstName, p.Id
            FOR JSON PATH, INCLUDE_NULL_VALUES), '[]')) AS people,
        JSON_QUERY(ISNULL((
            SELECT r.PersonAId AS personAId, r.PersonBId AS personBId, r.Type AS type, r.EndDate AS endDate, r.IsEnded AS isEnded
            FROM dbo.Relationships AS r
            JOIN dbo.People AS a ON a.Id = r.PersonAId AND a.FamilyId = @FamilyId AND a.DeletedAt IS NULL
            JOIN dbo.People AS b ON b.Id = r.PersonBId AND b.FamilyId = @FamilyId AND b.DeletedAt IS NULL
            WHERE r.DeletedAt IS NULL
            ORDER BY r.PersonAId, r.PersonBId, r.Type
            FOR JSON PATH, INCLUDE_NULL_VALUES), '[]')) AS relationships
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);

-- One row, one value, so sqlcmd doesn't split the JSON across lines.
SELECT @json;
