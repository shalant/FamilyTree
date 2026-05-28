-- FamilyTree seed data for local development
-- Run in SSMS after applying EF Core migrations:
--   dotnet ef database update

USE FamilyTreeDb;
GO

INSERT INTO People (FirstName, LastName, BirthDate, BirthPlace, Gender, CreatedAt, UpdatedAt)
VALUES
    ('John',  'Smith', '1945-03-12', 'Chicago, IL',    'Male',   GETUTCDATE(), GETUTCDATE()),
    ('Mary',  'Smith', '1948-07-04', 'Milwaukee, WI',  'Female', GETUTCDATE(), GETUTCDATE()),
    ('James', 'Smith', '1972-11-22', 'Chicago, IL',    'Male',   GETUTCDATE(), GETUTCDATE()),
    ('Susan', 'Smith', '1975-02-14', 'Evanston, IL',   'Female', GETUTCDATE(), GETUTCDATE());
GO

-- John + Mary are spouses
INSERT INTO Relationships (PersonAId, PersonBId, Type, StartDate, CreatedAt)
VALUES (1, 2, 'Spouse', '1968-06-01', GETUTCDATE());

-- John + Mary are parents of James and Susan
INSERT INTO Relationships (PersonAId, PersonBId, Type, CreatedAt)
VALUES
    (1, 3, 'Parent', GETUTCDATE()),
    (2, 3, 'Parent', GETUTCDATE()),
    (1, 4, 'Parent', GETUTCDATE()),
    (2, 4, 'Parent', GETUTCDATE());
GO
