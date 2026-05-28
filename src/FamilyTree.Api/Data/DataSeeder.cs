using FamilyTree.Api.Models;
using FamilyTree.Shared.Enums;

namespace FamilyTree.Api.Data;

public static class DataSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.People.Any()) return; // already seeded

        var now = DateTime.UtcNow;

        // ─────────────────────────────────────────────
        //  Create people
        // ─────────────────────────────────────────────
        var john = new Person
        {
            FirstName = "John",
            LastName = "Smith",
            BirthDate = new DateOnly(1940, 3, 12),
            BirthPlace = "Chicago, IL",
            Gender = Gender.Male,
            CreatedAt = now,
            UpdatedAt = now
        };

        var mary = new Person
        {
            FirstName = "Mary",
            LastName = "Smith",
            MaidenName = "Johnson",
            BirthDate = new DateOnly(1943, 7, 4),
            BirthPlace = "Milwaukee, WI",
            Gender = Gender.Female,
            CreatedAt = now,
            UpdatedAt = now
        };

        var james = new Person
        {
            FirstName = "James",
            LastName = "Smith",
            BirthDate = new DateOnly(1968, 11, 22),
            BirthPlace = "Chicago, IL",
            Gender = Gender.Male,
            CreatedAt = now,
            UpdatedAt = now
        };

        var susan = new Person
        {
            FirstName = "Susan",
            LastName = "Smith",
            BirthDate = new DateOnly(1971, 2, 14),
            BirthPlace = "Chicago, IL",
            Gender = Gender.Female,
            CreatedAt = now,
            UpdatedAt = now
        };

        var linda = new Person
        {
            FirstName = "Linda",
            LastName = "Smith",
            MaidenName = "Davis",
            BirthDate = new DateOnly(1970, 5, 30),
            BirthPlace = "Naperville, IL",
            Gender = Gender.Female,
            CreatedAt = now,
            UpdatedAt = now
        };

        var michael = new Person
        {
            FirstName = "Michael",
            LastName = "Smith",
            BirthDate = new DateOnly(1995, 8, 19),
            BirthPlace = "Chicago, IL",
            Gender = Gender.Male,
            CreatedAt = now,
            UpdatedAt = now
        };

        var emma = new Person
        {
            FirstName = "Emma",
            LastName = "Smith",
            BirthDate = new DateOnly(1998, 4, 2),
            BirthPlace = "Chicago, IL",
            Gender = Gender.Female,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.People.AddRange(john, mary, james, susan, linda, michael, emma);
        db.SaveChanges();

        // ─────────────────────────────────────────────
        //  Helper for canonical spouse ordering
        // ─────────────────────────────────────────────
        static (Guid A, Guid B) Canonical(Guid x, Guid y)
        {
            var ordered = new[] { x, y }.OrderBy(g => g).ToArray();
            return (ordered[0], ordered[1]);
        }

        // ─────────────────────────────────────────────
        //  Create relationships
        // ─────────────────────────────────────────────
        var rels = new List<Relationship>();

        // John + Mary married
        {
            var (a, b) = Canonical(john.Id, mary.Id);
            rels.Add(new Relationship
            {
                PersonAId = a,
                PersonBId = b,
                Type = RelationshipType.Spouse,
                StartDate = new DateOnly(1965, 6, 1),
                CreatedAt = now
            });
        }

        // Parents of James
        rels.Add(new Relationship { PersonAId = john.Id, PersonBId = james.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = mary.Id, PersonBId = james.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // Parents of Susan
        rels.Add(new Relationship { PersonAId = john.Id, PersonBId = susan.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = mary.Id, PersonBId = susan.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // James + Linda married
        {
            var (a, b) = Canonical(james.Id, linda.Id);
            rels.Add(new Relationship
            {
                PersonAId = a,
                PersonBId = b,
                Type = RelationshipType.Spouse,
                StartDate = new DateOnly(1993, 9, 15),
                CreatedAt = now
            });
        }

        // Parents of Michael
        rels.Add(new Relationship { PersonAId = james.Id, PersonBId = michael.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = linda.Id, PersonBId = michael.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // Parents of Emma
        rels.Add(new Relationship { PersonAId = james.Id, PersonBId = emma.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = linda.Id, PersonBId = emma.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // Siblings
        {
            var (a, b) = Canonical(james.Id, susan.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Sibling, CreatedAt = now });
        }
        {
            var (a, b) = Canonical(michael.Id, emma.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Sibling, CreatedAt = now });
        }

        db.Relationships.AddRange(rels);
        db.SaveChanges();
    }
}