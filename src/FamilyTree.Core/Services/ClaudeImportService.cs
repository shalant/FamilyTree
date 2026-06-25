using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Import;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
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
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["AI:AnthropicApiKey"]);

    public async Task<ImportPreview> ExtractFromTextAsync(string text,
        IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var apiKey = config["AI:AnthropicApiKey"]
            ?? throw new InvalidOperationException("AI:AnthropicApiKey is not configured.");

        var model = config["AI:ImportModel"] ?? "claude-sonnet-4-6";
        var prompt = BuildPrompt(text);

        progress?.Report(new ImportProgress("Sending to Claude…"));

        using var http = httpFactory.CreateClient("anthropic");
        var request = new
        {
            model,
            max_tokens = 64000,
            stream = true,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        // Stream the response so we can report live people-found counts as deltas arrive.
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("Claude API error {Status}: {Body}", resp.StatusCode, errBody);
            throw new InvalidOperationException($"Claude API error {(int)resp.StatusCode}: {errBody}");
        }

        var accumulated  = new StringBuilder();
        var diagnostics  = new ImportDiagnostics { Model = model };
        var prevFound    = 0;
        var prevReported = 0;

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var evt = JsonNode.Parse(data);
            var evtType = evt?["type"]?.GetValue<string>();

            switch (evtType)
            {
                case "message_start":
                    diagnostics.InputTokens = evt?["message"]?["usage"]?["input_tokens"]?.GetValue<int>();
                    break;

                case "content_block_delta":
                    var delta = evt?["delta"]?["text"]?.GetValue<string>() ?? "";
                    accumulated.Append(delta);

                    // Throttle UI updates: only re-report when a new person appears
                    // (counted by firstName occurrences) or every 2 000 chars.
                    var found = CountPeopleInPartialJson(accumulated.ToString());
                    if (found > prevFound || accumulated.Length - prevReported > 2000)
                    {
                        prevFound    = found;
                        prevReported = accumulated.Length;
                        progress?.Report(new ImportProgress(
                            "Extracting family records…",
                            found,
                            $"{accumulated.Length:N0} chars received"));
                    }
                    break;

                case "message_delta":
                    diagnostics.StopReason   = evt?["delta"]?["stop_reason"]?.GetValue<string>();
                    diagnostics.OutputTokens = evt?["usage"]?["output_tokens"]?.GetValue<int>();
                    break;
            }
        }

        var rawText = accumulated.ToString();
        progress?.Report(new ImportProgress("Parsing results…", CountPeopleInPartialJson(rawText)));

        diagnostics.RawResponseLength = rawText.Length;
        diagnostics.JsonRepairApplied = Regex.IsMatch(rawText, @"(:\s*)(\d{1,4}(?:/\d{1,4}){1,2})(\s*[,}\]])");

        try
        {
            var preview = ParseClaudeResponse(rawText);
            preview.Diagnostics = diagnostics;
            return preview;
        }
        catch (Exception ex)
        {
            var dumpPath = Path.Combine(Path.GetTempPath(),
                $"claude-import-fail-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            try { await File.WriteAllTextAsync(dumpPath, rawText, ct); } catch { /* best effort */ }
            logger.LogError(ex, "Failed to parse Claude import response (len={Len}). Raw dumped to {Path}",
                rawText.Length, dumpPath);
            throw;
        }
    }

    public async Task<ImportPreview> ExtractFromDocumentAsync(byte[] fileBytes, string fileName,
        int? startPage = null, int? endPage = null,
        IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new ImportProgress("Reading document…"));
        var text = ExtractTextFromDocument(fileBytes, fileName, startPage, endPage);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("No extractable text was found in this document.");

        progress?.Report(new ImportProgress(
            $"Sending {text.Length:N0} characters to Claude…"));
        return await ExtractFromTextAsync(text, progress, ct);
    }

    public int GetPdfPageCount(byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes);
        using var reader = new PdfReader(ms);
        using var doc = new PdfDocument(reader);
        return doc.GetNumberOfPages();
    }

    public async Task<ImportPreview> AnnotateMatchesAsync(ImportPreview preview, CancellationToken ct = default)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        // Scope the match pool to the same family CommitAsync will write into.
        var family   = await ctx.Families.FirstOrDefaultAsync(ct);
        var familyId = family?.Id;

        // Include people with a NULL FamilyId: legacy/unassigned records (never
        // backfilled) belong to the same single family in practice, and excluding them
        // silently emptied the match pool — the original cause of imports duplicating
        // existing people instead of linking to them.
        var existing = await ctx.People
            .AsNoTracking()
            .Where(p => p.DeletedAt == null &&
                        (!familyId.HasValue || p.FamilyId == familyId || p.FamilyId == null))
            .Select(p => new PersonDto
            {
                Id         = p.Id,
                FirstName  = p.FirstName,
                MiddleName = p.MiddleName,
                LastName   = p.LastName,
                MaidenName = p.MaidenName,
                BirthDate  = p.BirthDate,
                Gender     = p.Gender,
            })
            .ToListAsync(ct);

        preview.Diagnostics ??= new ImportDiagnostics();
        preview.Diagnostics.MatchPoolSize = existing.Count;

        if (existing.Count == 0) return preview;   // nothing to match against

        foreach (var person in preview.People)
            person.Candidates = ImportMatchService.FindCandidates(person, existing);

        return preview;
    }

    public async Task<ImportResult> CommitAsync(ImportPreview preview, CancellationToken ct = default)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        var family = await ctx.Families.FirstOrDefaultAsync(ct);
        var familyId = family?.Id;

        var selected = preview.People.Where(p => p.Selected).ToList();
        var idMap = new Dictionary<int, Guid>();
        var peopleCreated = 0;
        var linkedCount   = 0;

        // Validate any "link to existing" choices: only honor MatchedExistingId values
        // that actually resolve to a person in this DB (guards against stale/bogus ids
        // causing FK failures when wiring relationships).
        var matchedIds = selected
            .Where(p => p.MatchedExistingId.HasValue)
            .Select(p => p.MatchedExistingId!.Value)
            .Distinct()
            .ToList();
        var validExisting = matchedIds.Count == 0
            ? new HashSet<Guid>()
            : (await ctx.People.Where(p => matchedIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)).ToHashSet();

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
            // Linked to an existing person → reuse their Guid, create nothing. Relationships
            // in Pass 2 then wire straight into the existing tree instead of an island.
            if (p.MatchedExistingId is { } existingId && validExisting.Contains(existingId))
            {
                idMap[p.Id] = existingId;
                linkedCount++;

                // Apply any per-field merges the user chose in the reconcile dialog.
                if (p.MergeFieldNames is { Count: > 0 })
                {
                    var existing = await ctx.People.FirstOrDefaultAsync(x => x.Id == existingId, ct);
                    if (existing != null) ApplyFieldMerges(existing, p, p.MergeFieldNames);
                }
                continue;
            }

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

        // Preload existing relationships that touch any linked person so we don't try to
        // re-insert a link that already exists (unique index on PersonAId/PersonBId/Type).
        // Existing rows are already stored in canonical order, matching the key built below.
        if (validExisting.Count > 0)
        {
            var existingRels = await ctx.Relationships
                .Where(r => validExisting.Contains(r.PersonAId) || validExisting.Contains(r.PersonBId))
                .Select(r => new { r.PersonAId, r.PersonBId, r.Type })
                .ToListAsync(ct);
            foreach (var r in existingRels)
                seen.Add((r.PersonAId, r.PersonBId, r.Type));
        }

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
                ImportBatchId = batch.Id,
            });
            relCreated++;
        }
        // Update batch totals + debug/audit report
        batch.PersonCount      = peopleCreated;
        batch.RelationshipCount = relCreated;
        batch.Report           = BuildReport(preview, selected, validExisting, peopleCreated, linkedCount, relCreated);
        await ctx.SaveChangesAsync(ct);

        return new ImportResult(peopleCreated, relCreated, linkedCount);
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

        // Soft-delete every person created by this batch.
        await ctx.People
            .IgnoreQueryFilters()
            .Where(p => p.ImportBatchId == batchId && p.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, now), ct);

        // Soft-delete every relationship created by this batch — including "bridging"
        // links to pre-existing people (one endpoint outside the batch). The pre-existing
        // person is never touched, so linking stays fully reversible.
        await ctx.Relationships
            .IgnoreQueryFilters()
            .Where(r => r.ImportBatchId == batchId && r.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeletedAt, now), ct);

        // Legacy fallback for batches created before relationships carried a batch tag:
        // sweep untagged relationships where both endpoints were people from this batch.
        var personIds = await ctx.People
            .IgnoreQueryFilters()
            .Where(p => p.ImportBatchId == batchId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (personIds.Count > 0)
        {
            await ctx.Relationships
                .IgnoreQueryFilters()
                .Where(r => r.DeletedAt == null
                         && r.ImportBatchId == null
                         && personIds.Contains(r.PersonAId)
                         && personIds.Contains(r.PersonBId))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeletedAt, now), ct);
        }

        var batch = await ctx.ImportBatches.FindAsync([batchId], ct);
        if (batch != null)
        {
            batch.RolledBackAt = now;
            await ctx.SaveChangesAsync(ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Applies the user-selected field merges from an imported person onto the existing
    // record they were linked to. Only the chosen fields are touched; everything else
    // on the existing record is left exactly as it was.
    internal static void ApplyFieldMerges(Person existing, ImportPersonDto p, List<string> fields)
    {
        foreach (var f in fields)
        {
            switch (f)
            {
                case ImportMergeFields.BirthDate:      existing.BirthDate  = ParseDate(p.BirthDate); break;
                case ImportMergeFields.DeathDate:      existing.DeathDate  = ParseDate(p.DeathDate); break;
                case ImportMergeFields.MiddleName:     existing.MiddleName = p.MiddleName?.Trim();   break;
                case ImportMergeFields.MaidenName:     existing.MaidenName = p.MaidenName?.Trim();   break;
                case ImportMergeFields.Gender:         existing.Gender     = ParseGender(p.Gender);  break;
                case ImportMergeFields.BiographyNotes: existing.BiographyNotes = BuildNotes(p);      break;
            }
        }
        existing.UpdatedAt = DateTime.UtcNow;
    }

    // Human-readable import report: extraction metadata + per-person match decisions
    // (the "CREATED despite a high-confidence candidate" lines are the ones that catch
    // matching bugs) + outcome. Stored on the ImportBatch, survives rollback.
    internal static string BuildReport(
        ImportPreview preview, List<ImportPersonDto> selected, HashSet<Guid> validExisting,
        int peopleCreated, int linkedCount, int relCreated)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Import report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        var d = preview.Diagnostics;
        if (d != null)
        {
            sb.AppendLine("## Extraction");
            sb.AppendLine($"- Model: {d.Model}");
            sb.AppendLine($"- Stop reason: {d.StopReason ?? "?"}"
                + (d.StopReason == "max_tokens"
                    ? "  ⚠️ TRUNCATED — response hit the token cap; some records may be missing"
                    : ""));
            sb.AppendLine($"- Tokens: {d.InputTokens?.ToString() ?? "?"} in / {d.OutputTokens?.ToString() ?? "?"} out");
            sb.AppendLine($"- Raw response: {d.RawResponseLength:N0} chars");
            sb.AppendLine($"- JSON repair applied: {(d.JsonRepairApplied ? "yes (bare slash-dates quoted)" : "no")}");
            sb.AppendLine($"- Matched against {d.MatchPoolSize} existing people");
            sb.AppendLine();
        }

        var skipped = preview.People.Count - selected.Count;
        sb.AppendLine($"## People — {peopleCreated} created, {linkedCount} linked, {skipped} skipped");
        foreach (var p in selected)
        {
            var name  = $"{p.FirstName} {p.LastName}".Trim();
            var birth = p.BirthDate is { Length: >= 4 } ? $" b.{p.BirthDate}" : "";
            if (p.MatchedExistingId is { } mid && validExisting.Contains(mid))
            {
                var c = p.Candidates?.FirstOrDefault(x => x.PersonId == mid);
                var merged = p.MergeFieldNames is { Count: > 0 }
                    ? $"  merged: {string.Join(", ", p.MergeFieldNames)}"
                    : "";
                sb.AppendLine($"- 🔗 LINKED   {name}{birth} → {c?.DisplayName ?? "existing record"} (score {c?.Score:0.00}){merged}");
            }
            else
            {
                var top  = p.Candidates?.FirstOrDefault();
                var flag = top != null
                    ? $"  ⚠️ had candidate {top.DisplayName} ({top.Score:0.00}) — created new instead"
                    : "";
                sb.AppendLine($"- ➕ CREATED  {name}{birth}{flag}");
            }
        }
        foreach (var p in preview.People.Where(p => !p.Selected))
            sb.AppendLine($"- ⊘ SKIPPED  {$"{p.FirstName} {p.LastName}".Trim()}");

        sb.AppendLine();
        sb.AppendLine($"## Relationships — {relCreated} created");
        return sb.ToString();
    }

    internal static string ExtractTextFromDocument(byte[] fileBytes, string fileName, int? startPage = null, int? endPage = null)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ExtractTextFromPdf(fileBytes, startPage, endPage),
            ".txt" => Encoding.UTF8.GetString(fileBytes),
            _ => throw new NotSupportedException(
                $"'{ext}' files aren't supported yet — try a PDF or .txt file, or paste the text directly.")
        };
    }

    // startPage/endPage are 1-based and inclusive; null means "from the first page" /
    // "to the last page". Bounds are clamped so an out-of-range request can't throw.
    private static string ExtractTextFromPdf(byte[] fileBytes, int? startPage = null, int? endPage = null)
    {
        using var ms = new MemoryStream(fileBytes);
        using var reader = new PdfReader(ms);
        using var doc = new PdfDocument(reader);

        var total = doc.GetNumberOfPages();
        var from  = Math.Max(1, startPage ?? 1);
        var to    = Math.Min(total, endPage ?? total);

        var sb = new StringBuilder();
        for (var i = from; i <= to; i++)
            sb.AppendLine(PdfTextExtractor.GetTextFromPage(doc.GetPage(i)));

        return sb.ToString();
    }

    private static ImportPreview ParseClaudeResponse(string raw)
    {
        // Strip markdown code fences if Claude wrapped the JSON
        var text = Regex.Replace(raw.Trim(), @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline).Trim();

        // Find the outermost { ... }
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException("Claude did not return valid JSON.");

        text = RepairCommonJsonGlitches(text[start..(end + 1)]);

        ClaudeOutput? doc;
        try
        {
            doc = JsonSerializer.Deserialize<ClaudeOutput>(text, _json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Claude returned malformed JSON that couldn't be parsed ({ex.Message}). " +
                "This is more likely on very large single-page extractions — try a smaller page range.", ex);
        }
        if (doc is null)
            throw new InvalidOperationException("Failed to deserialize Claude response.");

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

    // Repairs the most common ways Claude's JSON drifts out of spec. Lenient
    // JsonSerializerOptions already cover comments and trailing commas; this fixes
    // bare slash-separated date values the model sometimes emits unquoted, e.g.
    //   "marriageDate": 5/1886   →   "marriageDate": "5/1886"
    // The colon-prefix anchor means it only touches a value sitting directly after a
    // key, so slashes inside already-quoted strings (which are valid JSON) are left alone.
    // Counts how many people Claude has emitted so far by counting "firstName":
    // occurrences in the partial JSON stream — one per person object, reliable.
    private static int CountPeopleInPartialJson(string partial)
    {
        const string needle = "\"firstName\":";
        var count = 0;
        var pos   = 0;
        while ((pos = partial.IndexOf(needle, pos, StringComparison.Ordinal)) >= 0)
        {
            count++;
            pos += needle.Length;
        }
        return count;
    }

    internal static string RepairCommonJsonGlitches(string json) =>
        Regex.Replace(json, @"(:\s*)(\d{1,4}(?:/\d{1,4}){1,2})(\s*[,}\]])", "$1\"$2\"$3");

    internal static DateOnly? ParseDate(string? raw)
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

        // M/D/YYYY — the source document's native format. The model is asked to
        // normalize to ISO, but sometimes passes a raw slash-date through; honor it
        // here (after RepairCommonJsonGlitches has quoted it) so the date survives.
        if (DateOnly.TryParseExact(raw, ["M/d/yyyy", "MM/dd/yyyy", "M/dd/yyyy", "MM/d/yyyy"],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var us))
            return us;

        // M/YYYY — month + year only
        var slash = raw.Split('/');
        if (slash.Length == 2 &&
            int.TryParse(slash[0], out var m2) && m2 is >= 1 and <= 12 &&
            int.TryParse(slash[1], out var y2) && y2 is > 1000 and < 2100)
            return new DateOnly(y2, m2, 1);

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

        Output STRICTLY VALID JSON (RFC 8259): every key and every string value double-quoted,
        no comments, no trailing commas, no unquoted values. EVERY date must be a quoted string
        (e.g. "1886-05") — never a bare number or slash-separated date like 5/1886.

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
