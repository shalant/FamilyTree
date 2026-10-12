-- check-user-families.sql  --  READ-ONLY. Changes no data.
--
-- Lists every account and the families it belongs to. A regular (non-super-user) account
-- with no UserFamily row logs in fine but sees a permanently empty tree: its FamilyId claim
-- comes only from a UserFamily row (AppUserClaimsPrincipalFactory), and PersonService shows
-- an account with no claim nothing at all. The super-user sees every family regardless.
--
-- Run it against the production database yourself, e.g. Azure portal > the SQL database >
-- Query editor, or SSMS / sqlcmd. Any row with Status = 'MISSING ...' needs a UserFamily
-- row added. Rerun after a new registration to confirm sign-up creates the row
-- (FamilyCount = 1 for the new account).
--
-- First run against production: 2026-10-12, all 5 accounts ok (docs/TodoList.md,
-- "Check before sending invites").

SELECT u.Email,
       u.DisplayName,
       u.IsSuperUser,
       COUNT(uf.FamilyId)                               AS FamilyCount,
       STRING_AGG(f.Name + ' (' + uf.Role + ')', ', ') AS Families,
       CASE WHEN COUNT(uf.FamilyId) = 0 AND u.IsSuperUser = 0
            THEN 'MISSING - sees an empty tree' ELSE 'ok' END AS Status
FROM dbo.AspNetUsers AS u
LEFT JOIN dbo.UserFamilies AS uf ON uf.UserId = u.Id
LEFT JOIN dbo.Families     AS f  ON f.Id = uf.FamilyId
GROUP BY u.Id, u.Email, u.DisplayName, u.IsSuperUser
ORDER BY Status DESC, u.Email;
