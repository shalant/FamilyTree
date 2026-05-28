-- Useful queries for inspecting the FamilyTree database in SSMS

-- All people, ordered by birth date
SELECT Id, FirstName, LastName, BirthDate, DeathDate, Gender
FROM People
ORDER BY BirthDate;

-- All relationships with names resolved
SELECT
    r.Id,
    pa.FirstName + ' ' + pa.LastName AS PersonA,
    r.Type,
    pb.FirstName + ' ' + pb.LastName AS PersonB,
    r.StartDate
FROM Relationships r
JOIN People pa ON pa.Id = r.PersonAId
JOIN People pb ON pb.Id = r.PersonBId
ORDER BY r.Type, pa.LastName;

-- Family members of a specific person (change @PersonId)
DECLARE @PersonId INT = 1;

SELECT
    p.Id,
    p.FirstName + ' ' + p.LastName AS Name,
    r.Type AS Relationship
FROM Relationships r
JOIN People p ON p.Id = CASE
    WHEN r.PersonAId = @PersonId THEN r.PersonBId
    ELSE r.PersonAId
END
WHERE r.PersonAId = @PersonId OR r.PersonBId = @PersonId
ORDER BY r.Type;
