using System.Text;
using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Core.Services;

public class GedcomExportService : IGedcomExportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;

    public GedcomExportService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
    }

    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var familyId = _currentUser.FamilyId;
        var isSuperUser = _currentUser.IsSuperUser;

        var people = await ctx.People
            .Where(p => isSuperUser || (familyId != null && p.FamilyId == familyId))
            .AsNoTracking()
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .ToListAsync(ct);

        var personIds = people.Select(p => p.Id).ToHashSet();

        var rels = await ctx.Relationships
            .Where(r => personIds.Contains(r.PersonAId) && personIds.Contains(r.PersonBId))
            .AsNoTracking()
            .ToListAsync(ct);

        return Build(people, rels);
    }

    private static string Build(List<Person> people, List<Relationship> rels)
    {
        var sb = new StringBuilder();

        // Stable GEDCOM IDs
        var indiId = new Dictionary<Guid, string>();
        for (int i = 0; i < people.Count; i++)
            indiId[people[i].Id] = $"@I{i + 1}@";

        var spouseRels = rels.Where(r => r.Type == RelationshipType.Spouse).ToList();
        var parentRels = rels.Where(r => r.Type is RelationshipType.Parent or RelationshipType.Adopted).ToList();

        // Build FAM records
        var famList  = new List<FamRecord>();
        var coupleFam = new Dictionary<string, FamRecord>();
        var soloFam   = new Dictionary<Guid, FamRecord>();
        int fc = 1;

        foreach (var r in spouseRels)
        {
            var key = CoupleKey(r.PersonAId, r.PersonBId);
            if (!coupleFam.ContainsKey(key))
            {
                var fam = new FamRecord($"@F{fc++}@", r.PersonAId, r.PersonBId,
                                        r.StartDate, r.EndDate);
                coupleFam[key] = fam;
                famList.Add(fam);
            }
        }

        // parent ID → set of child IDs (for co-parent matching)
        var childrenOf = parentRels
            .GroupBy(r => r.PersonAId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.PersonBId).ToHashSet());

        foreach (var pr in parentRels)
        {
            var parentId = pr.PersonAId;
            var childId  = pr.PersonBId;

            var parentFams = famList
                .Where(f => f.PersonAId == parentId || f.PersonBId == parentId)
                .ToList();

            FamRecord target;

            if (parentFams.Count == 0)
            {
                if (!soloFam.TryGetValue(parentId, out target!))
                {
                    target = new FamRecord($"@F{fc++}@", parentId, null, null, null);
                    soloFam[parentId] = target;
                    famList.Add(target);
                }
            }
            else if (parentFams.Count == 1)
            {
                target = parentFams[0];
            }
            else
            {
                // Prefer the FAM where the co-parent is also a parent of this child
                target = parentFams.FirstOrDefault(f =>
                {
                    var otherId = f.PersonAId == parentId ? f.PersonBId : f.PersonAId;
                    return otherId is not null &&
                           childrenOf.TryGetValue(otherId.Value, out var oc) &&
                           oc.Contains(childId);
                })
                // Fall back to the active (no divorce) FAM
                ?? parentFams.FirstOrDefault(f => f.DivorceDate is null)
                ?? parentFams[0];
            }

            target.Children.Add(childId);
        }

        // person → FAMs where they are HUSB/WIFE
        var famsOf = new Dictionary<Guid, List<FamRecord>>();
        foreach (var fam in famList)
        {
            AddToList(famsOf, fam.PersonAId, fam);
            if (fam.PersonBId is not null)
                AddToList(famsOf, fam.PersonBId.Value, fam);
        }

        // person → FAMs where they are a child
        var famcOf = new Dictionary<Guid, List<FamRecord>>();
        foreach (var fam in famList)
            foreach (var cid in fam.Children)
                AddToList(famcOf, cid, fam);

        // ── HEAD ─────────────────────────────────────────────────────────────
        sb.AppendLine("0 HEAD");
        sb.AppendLine("1 SOUR ArborKin");
        sb.AppendLine("2 VERS 1.0");
        sb.AppendLine("2 NAME ArborKin");
        sb.AppendLine($"1 DATE {DateTime.UtcNow:dd MMM yyyy}".ToUpper());
        sb.AppendLine("1 CHAR UTF-8");
        sb.AppendLine("1 GEDC");
        sb.AppendLine("2 VERS 5.5.1");
        sb.AppendLine("2 FORM LINEAGE-LINKED");

        // ── INDI records ──────────────────────────────────────────────────────
        foreach (var p in people)
        {
            sb.AppendLine($"0 {indiId[p.Id]} INDI");

            var given = string.Join(" ",
                new[] { p.FirstName, p.MiddleName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            sb.AppendLine($"1 NAME {given} /{p.LastName}/");
            sb.AppendLine($"2 GIVN {given}");
            sb.AppendLine($"2 SURN {p.LastName}");
            if (!string.IsNullOrWhiteSpace(p.MaidenName))
                sb.AppendLine($"2 _MARN {p.MaidenName}");

            sb.AppendLine($"1 SEX {p.Gender switch { Gender.Male => "M", Gender.Female => "F", _ => "U" }}");

            if (p.BirthDate is not null || !string.IsNullOrWhiteSpace(p.BirthPlace))
            {
                sb.AppendLine("1 BIRT");
                if (p.BirthDate is not null)        sb.AppendLine($"2 DATE {GedDate(p.BirthDate.Value)}");
                if (!string.IsNullOrWhiteSpace(p.BirthPlace)) sb.AppendLine($"2 PLAC {p.BirthPlace}");
            }

            if (p.DeathDate is not null || !string.IsNullOrWhiteSpace(p.DeathPlace))
            {
                sb.AppendLine("1 DEAT");
                if (p.DeathDate is not null)         sb.AppendLine($"2 DATE {GedDate(p.DeathDate.Value)}");
                if (!string.IsNullOrWhiteSpace(p.DeathPlace)) sb.AppendLine($"2 PLAC {p.DeathPlace}");
            }

            if (!string.IsNullOrWhiteSpace(p.BiographyNotes))
                AppendNote(sb, p.BiographyNotes);

            if (famsOf.TryGetValue(p.Id, out var myFams))
                foreach (var f in myFams) sb.AppendLine($"1 FAMS {f.Id}");

            if (famcOf.TryGetValue(p.Id, out var myFamc))
                foreach (var f in myFamc) sb.AppendLine($"1 FAMC {f.Id}");
        }

        // ── FAM records ───────────────────────────────────────────────────────
        var byId = people.ToDictionary(p => p.Id);
        foreach (var fam in famList)
        {
            sb.AppendLine($"0 {fam.Id} FAM");

            var a = byId.GetValueOrDefault(fam.PersonAId);
            var b = fam.PersonBId is not null ? byId.GetValueOrDefault(fam.PersonBId.Value) : null;

            if (a is not null && b is not null)
            {
                var (husb, wife) = HusbWife(a, b);
                if (husb is not null) sb.AppendLine($"1 HUSB {indiId[husb.Id]}");
                if (wife is not null) sb.AppendLine($"1 WIFE {indiId[wife.Id]}");
            }
            else if (a is not null)
            {
                sb.AppendLine($"1 {(a.Gender == Gender.Female ? "WIFE" : "HUSB")} {indiId[a.Id]}");
            }

            if (b is not null)
            {
                // Mark as married (date optional)
                sb.AppendLine("1 MARR");
                if (fam.MarriageDate is not null)
                    sb.AppendLine($"2 DATE {GedDate(fam.MarriageDate.Value)}");
            }

            if (fam.DivorceDate is not null)
            {
                sb.AppendLine("1 DIV Y");
                sb.AppendLine($"2 DATE {GedDate(fam.DivorceDate.Value)}");
            }

            foreach (var cid in fam.Children)
                if (indiId.TryGetValue(cid, out var ci))
                    sb.AppendLine($"1 CHIL {ci}");
        }

        sb.AppendLine("0 TRLR");
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GedDate(DateOnly d)
    {
        string[] m = ["JAN","FEB","MAR","APR","MAY","JUN","JUL","AUG","SEP","OCT","NOV","DEC"];
        return $"{d.Day:D2} {m[d.Month - 1]} {d.Year}";
    }

    private static void AppendNote(StringBuilder sb, string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        bool first = true;
        foreach (var rawLine in lines)
        {
            var chunks = ChunkLine(rawLine);
            foreach (var chunk in chunks)
            {
                if (first) { sb.AppendLine($"1 NOTE {chunk}"); first = false; }
                else        sb.AppendLine($"2 CONT {chunk}");
            }
        }
    }

    private static IEnumerable<string> ChunkLine(string line)
    {
        if (line.Length <= 200) { yield return line; yield break; }
        for (int i = 0; i < line.Length; i += 200)
            yield return line.Substring(i, Math.Min(200, line.Length - i));
    }

    private static string CoupleKey(Guid a, Guid b) =>
        a < b ? $"{a}:{b}" : $"{b}:{a}";

    private static (Person? husb, Person? wife) HusbWife(Person a, Person b)
    {
        if (a.Gender == Gender.Male && b.Gender == Gender.Female) return (a, b);
        if (a.Gender == Gender.Female && b.Gender == Gender.Male) return (b, a);
        if (a.Gender == Gender.Male)   return (a, b);
        if (a.Gender == Gender.Female) return (b, a);
        return (a, b);
    }

    private static void AddToList<TKey, TVal>(Dictionary<TKey, List<TVal>> dict, TKey key, TVal val)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = [];
        list.Add(val);
    }

    private sealed class FamRecord(
        string id, Guid personAId, Guid? personBId,
        DateOnly? marriageDate, DateOnly? divorceDate)
    {
        public string    Id           { get; } = id;
        public Guid      PersonAId    { get; } = personAId;
        public Guid?     PersonBId    { get; } = personBId;
        public DateOnly? MarriageDate { get; } = marriageDate;
        public DateOnly? DivorceDate  { get; } = divorceDate;
        public HashSet<Guid> Children { get; } = [];
    }
}
