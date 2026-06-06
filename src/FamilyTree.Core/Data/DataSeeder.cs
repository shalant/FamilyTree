using FamilyTree.Core.Models;
using FamilyTree.Shared.Enums;

namespace FamilyTree.Core.Data;

public static class DataSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.People.Any()) return; // already seeded

        var now = DateTime.UtcNow;

        // ─────────────────────────────────────────────
        //  Generation 1 (Great-grandparents)
        // ─────────────────────────────────────────────
        var williamSmith = new Person { FirstName = "William", LastName = "Smith", BirthDate = new DateOnly(1910, 1, 15), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var elizabethSmith = new Person { FirstName = "Elizabeth", LastName = "Smith", MaidenName = "Brown", BirthDate = new DateOnly(1912, 3, 22), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var georgeWilson = new Person { FirstName = "George", LastName = "Wilson", BirthDate = new DateOnly(1908, 6, 10), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var margaretWilson = new Person { FirstName = "Margaret", LastName = "Wilson", MaidenName = "Taylor", BirthDate = new DateOnly(1914, 9, 5), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };

        // ─────────────────────────────────────────────
        //  Generation 2 (Grandparents)
        // ─────────────────────────────────────────────
        var john = new Person { FirstName = "John", LastName = "Smith", BirthDate = new DateOnly(1940, 3, 12), BirthPlace = "Chicago, IL", Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var mary = new Person { FirstName = "Mary", LastName = "Smith", MaidenName = "Johnson", BirthDate = new DateOnly(1943, 7, 4), BirthPlace = "Milwaukee, WI", Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var richard = new Person { FirstName = "Richard", LastName = "Smith", BirthDate = new DateOnly(1938, 2, 28), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var dorothy = new Person { FirstName = "Dorothy", LastName = "Smith", MaidenName = "Miller", BirthDate = new DateOnly(1945, 11, 20), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var henry = new Person { FirstName = "Henry", LastName = "Wilson", BirthDate = new DateOnly(1936, 5, 8), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var catherine = new Person { FirstName = "Catherine", LastName = "Wilson", MaidenName = "Anderson", BirthDate = new DateOnly(1941, 8, 15), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };

        // ─────────────────────────────────────────────
        //  Generation 3 (Parents)
        // ─────────────────────────────────────────────
        var james = new Person { FirstName = "James", LastName = "Smith", BirthDate = new DateOnly(1968, 11, 22), BirthPlace = "Chicago, IL", Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var susan = new Person { FirstName = "Susan", LastName = "Smith", BirthDate = new DateOnly(1971, 2, 14), BirthPlace = "Chicago, IL", Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var robert = new Person { FirstName = "Robert", LastName = "Smith", BirthDate = new DateOnly(1965, 7, 19), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var patricia = new Person { FirstName = "Patricia", LastName = "Wilson", BirthDate = new DateOnly(1967, 4, 3), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var charles = new Person { FirstName = "Charles", LastName = "Wilson", BirthDate = new DateOnly(1962, 10, 12), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var barbara = new Person { FirstName = "Barbara", LastName = "Wilson", MaidenName = "Thomas", BirthDate = new DateOnly(1969, 1, 25), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };

        // ─────────────────────────────────────────────
        //  Generation 4 (Children)
        // ─────────────────────────────────────────────
        var linda = new Person { FirstName = "Linda", LastName = "Smith", MaidenName = "Davis", BirthDate = new DateOnly(1970, 5, 30), BirthPlace = "Naperville, IL", Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var michael = new Person { FirstName = "Michael", LastName = "Smith", BirthDate = new DateOnly(1995, 8, 19), BirthPlace = "Chicago, IL", Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var emma = new Person { FirstName = "Emma", LastName = "Smith", BirthDate = new DateOnly(1998, 4, 2), BirthPlace = "Chicago, IL", Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var david = new Person { FirstName = "David", LastName = "Smith", BirthDate = new DateOnly(1992, 6, 14), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var jennifer = new Person { FirstName = "Jennifer", LastName = "Smith", MaidenName = "Martinez", BirthDate = new DateOnly(1994, 3, 7), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var sarah = new Person { FirstName = "Sarah", LastName = "Wilson", BirthDate = new DateOnly(1989, 9, 11), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var thomas = new Person { FirstName = "Thomas", LastName = "Wilson", BirthDate = new DateOnly(1991, 12, 28), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var andrew = new Person { FirstName = "Andrew", LastName = "Wilson", BirthDate = new DateOnly(1987, 7, 5), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var jessica = new Person { FirstName = "Jessica", LastName = "Wilson", MaidenName = "Garcia", BirthDate = new DateOnly(1990, 2, 18), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };

        // ─────────────────────────────────────────────
        //  Generation 5 (Grandchildren)
        // ─────────────────────────────────────────────
        var olivia = new Person { FirstName = "Olivia", LastName = "Smith", BirthDate = new DateOnly(2020, 5, 10), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var ethan = new Person { FirstName = "Ethan", LastName = "Smith", BirthDate = new DateOnly(2018, 11, 3), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var ava = new Person { FirstName = "Ava", LastName = "Smith", BirthDate = new DateOnly(2021, 8, 22), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };
        var liam = new Person { FirstName = "Liam", LastName = "Wilson", BirthDate = new DateOnly(2019, 3, 15), Gender = Gender.Male, CreatedAt = now, UpdatedAt = now };
        var sophia = new Person { FirstName = "Sophia", LastName = "Wilson", BirthDate = new DateOnly(2022, 1, 8), Gender = Gender.Female, CreatedAt = now, UpdatedAt = now };

        var allPeople = new[]
        {
            williamSmith, elizabethSmith, georgeWilson, margaretWilson,
            john, mary, richard, dorothy, henry, catherine,
            james, susan, robert, patricia, charles, barbara,
            linda, michael, emma, david, jennifer, sarah, thomas, andrew, jessica,
            olivia, ethan, ava, liam, sophia
        };

        db.People.AddRange(allPeople);
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

        // Generation 1 marriages
        {
            var (a, b) = Canonical(williamSmith.Id, elizabethSmith.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1935, 5, 1), CreatedAt = now });
        }
        {
            var (a, b) = Canonical(georgeWilson.Id, margaretWilson.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1932, 8, 15), CreatedAt = now });
        }

        // Generation 1 → Generation 2 parents
        rels.Add(new Relationship { PersonAId = williamSmith.Id, PersonBId = john.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = elizabethSmith.Id, PersonBId = john.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = williamSmith.Id, PersonBId = richard.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = elizabethSmith.Id, PersonBId = richard.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = georgeWilson.Id, PersonBId = henry.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = margaretWilson.Id, PersonBId = henry.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // Generation 2 marriages
        {
            var (a, b) = Canonical(john.Id, mary.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1965, 6, 1), CreatedAt = now });
        }
        {
            var (a, b) = Canonical(richard.Id, dorothy.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1963, 3, 20), CreatedAt = now });
        }
        {
            var (a, b) = Canonical(henry.Id, catherine.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1960, 10, 10), CreatedAt = now });
        }

        // Generation 2 siblings
        {
            var (a, b) = Canonical(john.Id, richard.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Sibling, CreatedAt = now });
        }

        // Generation 2 → Generation 3 parents
        rels.Add(new Relationship { PersonAId = john.Id, PersonBId = james.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = mary.Id, PersonBId = james.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = john.Id, PersonBId = susan.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = mary.Id, PersonBId = susan.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = richard.Id, PersonBId = robert.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = dorothy.Id, PersonBId = robert.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = henry.Id, PersonBId = charles.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = catherine.Id, PersonBId = charles.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = henry.Id, PersonBId = patricia.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = catherine.Id, PersonBId = patricia.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // Generation 3 marriages
        {
            var (a, b) = Canonical(james.Id, linda.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1993, 9, 15), CreatedAt = now });
        }
        {
            var (a, b) = Canonical(robert.Id, jennifer.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1990, 4, 22), CreatedAt = now });
        }
        {
            var (a, b) = Canonical(charles.Id, barbara.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(1988, 7, 10), CreatedAt = now });
        }
        {
            var (a, b) = Canonical(andrew.Id, jessica.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Spouse, StartDate = new DateOnly(2012, 6, 3), CreatedAt = now });
        }

        // Generation 3 siblings
        {
            var (a, b) = Canonical(james.Id, susan.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Sibling, CreatedAt = now });
        }

        // Generation 3 → Generation 4 parents
        rels.Add(new Relationship { PersonAId = james.Id, PersonBId = michael.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = linda.Id, PersonBId = michael.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = james.Id, PersonBId = emma.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = linda.Id, PersonBId = emma.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = robert.Id, PersonBId = david.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = jennifer.Id, PersonBId = david.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = charles.Id, PersonBId = sarah.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = barbara.Id, PersonBId = sarah.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = charles.Id, PersonBId = thomas.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = barbara.Id, PersonBId = thomas.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = andrew.Id, PersonBId = liam.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = jessica.Id, PersonBId = liam.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // Generation 4 siblings
        {
            var (a, b) = Canonical(michael.Id, emma.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Sibling, CreatedAt = now });
        }
        {
            var (a, b) = Canonical(sarah.Id, thomas.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Sibling, CreatedAt = now });
        }

        // Generation 4 → Generation 5 parents
        rels.Add(new Relationship { PersonAId = michael.Id, PersonBId = ethan.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = michael.Id, PersonBId = olivia.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = emma.Id, PersonBId = ava.Id, Type = RelationshipType.Parent, CreatedAt = now });
        rels.Add(new Relationship { PersonAId = liam.Id, PersonBId = sophia.Id, Type = RelationshipType.Parent, CreatedAt = now });

        // Generation 5 siblings
        {
            var (a, b) = Canonical(ethan.Id, olivia.Id);
            rels.Add(new Relationship { PersonAId = a, PersonBId = b, Type = RelationshipType.Sibling, CreatedAt = now });
        }

        db.Relationships.AddRange(rels);
        db.SaveChanges();
    }
}
