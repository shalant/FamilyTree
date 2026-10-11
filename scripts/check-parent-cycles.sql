-- check-parent-cycles.sql  --  READ-ONLY. Changes no data (its only write is to a table variable that
-- vanishes when the batch ends).
--
-- Finds people caught in a Parent loop (someone recorded as their own ancestor). Before the
-- R0 fix (docs/TodoList.md, 2026-10-11) such a loop crashed the whole web process when the
-- tree rendered, and nothing stopped one being saved. The app now refuses new loops and
-- survives existing ones, but any loop already in the data should still be repaired.
--
-- Run it against the production database yourself, e.g. Azure portal > the SQL database >
-- Query editor, or SSMS / sqlcmd. Zero rows from both result sets = no loops.
--
-- Result set 1: every person who is their own ancestor.
-- Result set 2: the Parent links between those people. Removing ANY ONE link in a loop
--   breaks it (Edit person > remove the parent, or the relationship, in the app).
--
-- It looks at exactly what the tree is drawn from: live (not soft-deleted) people joined by
-- live 'Parent' relationships (PersonAId = parent, PersonBId = child).

DECLARE @loops TABLE (PersonId uniqueidentifier PRIMARY KEY, FirstName nvarchar(200), LastName nvarchar(200), FamilyId uniqueidentifier NULL);

WITH LiveParentLinks AS (
    SELECT r.PersonAId AS ParentId, r.PersonBId AS ChildId
    FROM dbo.Relationships AS r
    JOIN dbo.People AS parent ON parent.Id = r.PersonAId AND parent.DeletedAt IS NULL
    JOIN dbo.People AS child  ON child.Id  = r.PersonBId AND child.DeletedAt  IS NULL
    WHERE r.Type = 'Parent' AND r.DeletedAt IS NULL
),
-- Walk down from every parent link. A walk stops as soon as it arrives back at the person it
-- started from (a loop), and never revisits anyone else, so the recursion always terminates.
Walk AS (
    SELECT ParentId AS StartId, ChildId AS CurrentId,
           CAST('/' + CONVERT(varchar(36), ParentId) + '/' + CONVERT(varchar(36), ChildId) + '/' AS varchar(max)) AS Path
    FROM LiveParentLinks
    UNION ALL
    SELECT w.StartId, l.ChildId,
           w.Path + CONVERT(varchar(36), l.ChildId) + '/'
    FROM Walk AS w
    JOIN LiveParentLinks AS l ON l.ParentId = w.CurrentId
    WHERE w.CurrentId <> w.StartId
      AND (l.ChildId = w.StartId
           OR w.Path NOT LIKE '%/' + CONVERT(varchar(36), l.ChildId) + '/%')
),
PeopleInLoops AS (
    SELECT DISTINCT StartId AS PersonId FROM Walk WHERE CurrentId = StartId
)
INSERT INTO @loops (PersonId, FirstName, LastName, FamilyId)
SELECT p.Id, p.FirstName, p.LastName, p.FamilyId
FROM dbo.People AS p
JOIN PeopleInLoops AS pl ON pl.PersonId = p.Id
OPTION (MAXRECURSION 200);

SELECT PersonId, FirstName, LastName, FamilyId FROM @loops ORDER BY FamilyId, LastName, FirstName;

SELECT r.Id AS RelationshipId,
       parent.FirstName + ' ' + parent.LastName AS ParentName, r.PersonAId AS ParentId,
       child.FirstName  + ' ' + child.LastName  AS ChildName,  r.PersonBId AS ChildId
FROM dbo.Relationships AS r
JOIN dbo.People AS parent ON parent.Id = r.PersonAId
JOIN dbo.People AS child  ON child.Id  = r.PersonBId
WHERE r.Type = 'Parent' AND r.DeletedAt IS NULL
  AND parent.DeletedAt IS NULL AND child.DeletedAt IS NULL
  AND r.PersonAId IN (SELECT PersonId FROM @loops)
  AND r.PersonBId IN (SELECT PersonId FROM @loops)
ORDER BY ParentName, ChildName;

