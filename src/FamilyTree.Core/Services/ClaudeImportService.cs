using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Import;
using FamilyTree.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class ClaudeImportService(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ClaudeImportService> logger) : IImportService
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["AI:AnthropicApiKey"]);

    public async Task<ImportPreview> ExtractFromTextAsync(string text, CancellationToken ct = default)
    {
        var apiKey = config["AI:AnthropicApiKey"]
            ?? throw new InvalidOperationException("AI:AnthropicApiKey is not configured.");

        var model = config["AI:ImportModel"] ?? "claude-sonnet-4-6";

        var prompt = BuildPrompt(text);

        using var http = httpFactory.CreateClient("anthropic");
        var request = new
        {
            model,
            max_tokens = 32000,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Claude API error {Status}: {Body}", resp.StatusCode, body);
            throw new InvalidOperationException($"Claude API error {(int)resp.StatusCode}: {body}");
        }

        var responseJson = JsonNode.Parse(body);
        var rawText = responseJson?["content"]?[0]?["text"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Unexpected response shape from Claude API.");

        return ParseClaudeResponse(rawText);
    }

    public async Task<ImportResult> CommitAsync(ImportPreview preview, CancellationToken ct = default)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        // Deterministically the oldest family — with more than one Family row now
        // possible (e.g. a separate test/demo family), an unordered FirstOrDefault
        // would pick an arbitrary one instead of the original/primary family.
        var family = await ctx.Families.OrderBy(f => f.CreatedAt).FirstOrDefaultAsync(ct);
        var familyId = family?.Id;

        var selected = preview.People.Where(p => p.Selected).ToList();
        var idMap = new Dictionary<int, Guid>();
        var peopleCreated = 0;

        // Create the batch record first so we can tag each person
        var batch = new ImportBatch
        {
            Id        = Guid.NewGuid(),
            Note      = "Text paste import",
            CreatedAt = DateTime.UtcNow,
        };
        ctx.ImportBatches.Add(batch);

        // Pass 1: create all selected people (no relationships yet)
        foreach (var p in selected)
        {
            var person = new Person
            {
                Id        = Guid.NewGuid(),
                FirstName = p.FirstName.Trim(),
                LastName  = p.LastName.Trim(),
                MiddleName = p.MiddleName?.Trim(),
                MaidenName = p.MaidenName?.Trim(),
                BirthDate = ParseDate(p.BirthDate),
                DeathDate = ParseDate(p.DeathDate),
                Gender    = ParseGender(p.Gender),
                FamilyId  = familyId,
                ImportBatchId = batch.Id,
                CreatedAt = DateTime.UtcNow,
                BiographyNotes = BuildNotes(p),
            };
            ctx.People.Add(person);
            idMap[p.Id] = person.Id;
            peopleCreated++;
        }
        await ctx.SaveChangesAsync(ct);

        // Pass 2: insert relationships
        var relCreated = 0;
        var seen = new HashSet<(Guid, Guid, RelationshipType)>();

        foreach (var rel in preview.Relationships)
        {
            if (!idMap.TryGetValue(rel.PersonAId, out var aId)) continue;
            if (!idMap.TryGetValue(rel.PersonBId, out var bId)) continue;

            var type = rel.Type switch
            {
                "Parent"  => RelationshipType.Parent,
                "Spouse"  => RelationshipType.Spouse,
                "Sibling" => RelationshipType.Sibling,
                _         => (RelationshipType?)null
            };
            if (type is null) continue;

            // Canonical ordering for bidirectional types
            var (pA, pB) = type == RelationshipType.Parent
                ? (aId, bId)
                : aId.CompareTo(bId) < 0 ? (aId, bId) : (bId, aId);

            var key = (pA, pB, type.Value);
            if (!seen.Add(key)) continue;

            ctx.Relationships.Add(new Relationship
            {
                Id        = Guid.NewGuid(),
                PersonAId = pA,
                PersonBId = pB,
                Type      = type.Value,
                StartDate = ParseDate(rel.MarriageDate),
                EndDate   = rel.IsFormer ? DateOnly.FromDateTime(DateTime.UtcNow) : null,
                CreatedAt = DateTime.UtcNow,
            });
            relCreated++;
        }
        // Update batch totals
        batch.PersonCount      = peopleCreated;
        batch.RelationshipCount = relCreated;
        await ctx.SaveChangesAsync(ct);

        return new ImportResult(peopleCreated, relCreated);
    }

    public async Task<List<ImportBatch>> GetImportBatchesAsync(CancellationToken ct = default)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        return await ctx.ImportBatches
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task RollbackBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;

        var people = await ctx.People
            .IgnoreQueryFilters()
            .Where(p => p.ImportBatchId == batchId && p.DeletedAt == null)
            .ToListAsync(ct);

        if (people.Count > 0)
        {
            var personIds = people.Select(p => p.Id).ToHashSet();

            foreach (var person in people)
                person.DeletedAt = now;

            // Soft-delete every relationship touching ANY person in this batch — matching
            // PersonService.DeleteAsync's cascade (either side, not both). A relationship
            // linking a batch-created person to a pre-existing one is just as orphaned by
            // this rollback as one entirely internal to the batch; leaving it live left a
            // dangling row pointing at a soft-deleted person (found 2026-07-08 — see the
            // Elliot Rosenberg duplicate-person entry in docs/TodoList.md).
            var relationships = await ctx.Relationships
                .IgnoreQueryFilters()
                .Where(r => r.DeletedAt == null
                         && (personIds.Contains(r.PersonAId) || personIds.Contains(r.PersonBId)))
                .ToListAsync(ct);

            foreach (var rel in relationships)
                rel.DeletedAt = now;

            await ctx.SaveChangesAsync(ct);
        }

        var batch = await ctx.ImportBatches.FindAsync([batchId], ct);
        if (batch != null)
        {
            batch.RolledBackAt = now;
            await ctx.SaveChangesAsync(ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ImportPreview ParseClaudeResponse(string raw)
    {
        // Strip markdown code fences if Claude wrapped the JSON
        var text = Regex.Replace(raw.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

        // Find the outermost { ... }
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException("Claude did not return valid JSON.");

        text = text[start..(end + 1)];

        var doc = JsonSerializer.Deserialize<ClaudeOutput>(text, _json)
            ?? throw new InvalidOperationException("Failed to deserialize Claude response.");

        var people = (doc.People ?? [])
            .Select(p => new ImportPersonDto
            {
                Id           = p.Id,
                FirstName    = p.FirstName ?? "",
                LastName     = p.LastName ?? "",
                MiddleName   = p.MiddleName,
                MaidenName   = p.MaidenName,
                Aliases      = p.Aliases,
                BirthDate    = p.BirthDate,
                DeathDate    = p.DeathDate,
                IsDeceased   = p.IsDeceased,
                Gender       = p.Gender ?? "Unknown",
                Notes        = p.Notes,
                ReferenceOnly = p.ReferenceOnly,
                Selected     = !p.ReferenceOnly,
            })
            .ToList();

        var rels = (doc.Relationships ?? [])
            .Select(r => new ImportRelationshipDto
            {
                Type         = r.Type ?? "",
                PersonAId    = r.PersonAId,
                PersonBId    = r.PersonBId,
                IsFormer     = r.IsFormer,
                MarriageDate = r.MarriageDate,
            })
            .ToList();

        return new ImportPreview { People = people, Relationships = rels };
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // YYYY-MM-DD
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var full))
            return full;

        // YYYY-MM
        if (raw.Length == 7 && raw[4] == '-' &&
            int.TryParse(raw[..4], out var yr) &&
            int.TryParse(raw[5..], out var mo) &&
            mo is >= 1 and <= 12)
            return new DateOnly(yr, mo, 1);

        // YYYY
        if (raw.Length == 4 && int.TryParse(raw, out var year) && year is > 1000 and < 2100)
            return new DateOnly(year, 1, 1);

        return null;
    }

    private static Gender ParseGender(string? raw) => raw?.ToLower() switch
    {
        "male"   => Gender.Male,
        "female" => Gender.Female,
        _        => Gender.Unknown
    };

    private static string? BuildNotes(ImportPersonDto p)
    {
        var parts = new List<string>();
        if (p.Aliases?.Length > 0)
            parts.Add("Also known as: " + string.Join(", ", p.Aliases));
        if (!string.IsNullOrWhiteSpace(p.Notes))
            parts.Add(p.Notes);
        if (p.ReferenceOnly)
            parts.Add("Included for reference only.");
        return parts.Count > 0 ? string.Join(" | ", parts) : null;
    }

    private static string BuildPrompt(string text) => $$"""
        Parse this genealogy document and extract every person along with their family relationships.

        DOCUMENT FORMAT:
        - "Y" before a name = person is deceased
        - Dates: M/D/YYYY (e.g. 7/22/1887), M/YYYY (e.g. 5/1886), or YYYY (e.g. 1883)
        - "(m. DATE NAME DATES)" = married to NAME; NAME's dates follow their name
        - "(d. DATE (m. DATE) NAME)" = divorced from NAME; d. is divorce date, m. is marriage date
        - Multiple "/" in a name = aliases (e.g. SADY/SADIE/SARAH REVA — use the last listed as firstName, put others in aliases)
        - "(incl. for ref. only; not a Herskovitz desc.)" = step-relative, set referenceOnly true
        - The document is hierarchical: ISRAEL HERSKOVITZ is the patriarch; each numbered entry is a descendant
        - A person's children are listed on the lines immediately following them, before the next same-generation person

        EXTRACT EVERY PERSON including spouses listed inline in marriage notations.
        Assign unique sequential integer IDs (1, 2, 3...) to ALL people including embedded spouses.
        Normalize dates to ISO: "YYYY-MM-DD", "YYYY-MM", or "YYYY".
        Infer gender from names and context.

        OUTPUT only a valid JSON object with no markdown, no explanation, no code fences:
        {
          "people": [
            {
              "id": 1,
              "firstName": "Israel",
              "lastName": "Herskovitz",
              "middleName": null,
              "maidenName": null,
              "aliases": [],
              "birthDate": "1865-09",
              "deathDate": "1951-05-08",
              "isDeceased": true,
              "gender": "Male",
              "notes": null,
              "referenceOnly": false
            }
          ],
          "relationships": [
            { "type": "Spouse", "personAId": 1, "personBId": 2, "isFormer": false, "marriageDate": "1886-05" },
            { "type": "Parent", "personAId": 1, "personBId": 3, "isFormer": false, "marriageDate": null }
          ]
        }

        DOCUMENT:
        {{text}}
        """;

    // ── Deserialization types (private) ───────────────────────────────────────

    private sealed class ClaudeOutput
    {
        public List<ClaudePerson>? People { get; set; }
        public List<ClaudeRel>? Relationships { get; set; }
    }

    private sealed class ClaudePerson
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MiddleName { get; set; }
        public string? MaidenName { get; set; }
        public string[]? Aliases { get; set; }
        public string? BirthDate { get; set; }
        public string? DeathDate { get; set; }
        public bool IsDeceased { get; set; }
        public string? Gender { get; set; }
        public string? Notes { get; set; }
        public bool ReferenceOnly { get; set; }
    }

    private sealed class ClaudeRel
    {
        public string? Type { get; set; }
        public int PersonAId { get; set; }
        public int PersonBId { get; set; }
        public bool IsFormer { get; set; }
        public string? MarriageDate { get; set; }
    }
}
