using System.Text;
using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.Import;
using FluentAssertions;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Xunit;

namespace FamilyTree.Core.Tests;

public class ClaudeImportServiceTests
{
    [Fact]
    public void ExtractTextFromDocument_Txt_ReturnsDecodedText()
    {
        var bytes = Encoding.UTF8.GetBytes("Israel Herskovitz, b. 1865");

        var text = ClaudeImportService.ExtractTextFromDocument(bytes, "family.txt");

        text.Should().Be("Israel Herskovitz, b. 1865");
    }

    [Fact]
    public void ExtractTextFromDocument_Pdf_ExtractsTextFromAllPages()
    {
        var bytes = BuildSamplePdf("Israel Herskovitz, b. 1865", "Page two: Sadie Reva, m. 1886");

        var text = ClaudeImportService.ExtractTextFromDocument(bytes, "family.pdf");

        text.Should().Contain("Israel Herskovitz, b. 1865");
        text.Should().Contain("Page two: Sadie Reva, m. 1886");
    }

    [Fact]
    public void ExtractTextFromDocument_PdfWithPageRange_ExtractsOnlyRequestedPages()
    {
        var bytes = BuildSamplePdf("Page one: Israel Herskovitz", "Page two: Sadie Reva");

        var text = ClaudeImportService.ExtractTextFromDocument(bytes, "family.pdf", startPage: 2, endPage: 2);

        text.Should().Contain("Page two: Sadie Reva");
        text.Should().NotContain("Israel Herskovitz");
    }

    [Fact]
    public void ExtractTextFromDocument_PdfPageRangeOutOfBounds_ClampsToAvailablePages()
    {
        var bytes = BuildSamplePdf("Page one", "Page two");

        // Asking for pages 1..99 of a 2-page PDF should clamp, not throw.
        var text = ClaudeImportService.ExtractTextFromDocument(bytes, "family.pdf", startPage: 1, endPage: 99);

        text.Should().Contain("Page one");
        text.Should().Contain("Page two");
    }

    [Fact]
    public void ExtractTextFromDocument_UnsupportedExtension_ThrowsNotSupportedException()
    {
        var act = () => ClaudeImportService.ExtractTextFromDocument([], "family.docx");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void RepairCommonJsonGlitches_QuotesBareSlashDate()
    {
        var broken = """{ "marriageDate": 5/1886, "personAId": 1 }""";

        var fixedJson = ClaudeImportService.RepairCommonJsonGlitches(broken);

        fixedJson.Should().Contain("\"marriageDate\": \"5/1886\"");
    }

    [Fact]
    public void RepairCommonJsonGlitches_QuotesBareSlashDateAtObjectEnd()
    {
        var broken = """{ "marriageDate": 7/22/1887 }""";

        var fixedJson = ClaudeImportService.RepairCommonJsonGlitches(broken);

        fixedJson.Should().Contain("\"marriageDate\": \"7/22/1887\"");
    }

    [Fact]
    public void RepairCommonJsonGlitches_LeavesSlashesInsideQuotedStrings()
    {
        var ok = """{ "notes": "married 5/1886 in Kiev" }""";

        var fixedJson = ClaudeImportService.RepairCommonJsonGlitches(ok);

        fixedJson.Should().Be(ok);
    }

    [Fact]
    public void BuildReport_FlagsCreatedDespiteCandidate_AndLinked()
    {
        var existingId = Guid.NewGuid();
        var linked = new ImportPersonDto
        {
            FirstName = "Marc", LastName = "Rosenberg", BirthDate = "1948-06-15", Selected = true,
            MatchedExistingId = existingId,
            Candidates = [new MatchCandidate { PersonId = existingId, DisplayName = "Marc Rosenberg", Score = 0.97 }],
        };
        var missed = new ImportPersonDto
        {
            FirstName = "Douglas", LastName = "Rosenberg", BirthDate = "1978-11-21", Selected = true,
            Candidates = [new MatchCandidate { PersonId = Guid.NewGuid(), DisplayName = "Douglas Rosenberg", Score = 0.98 }],
        };
        var preview = new ImportPreview
        {
            People = [linked, missed],
            Diagnostics = new ImportDiagnostics { Model = "claude-sonnet-4-6", StopReason = "end_turn", MatchPoolSize = 53 },
        };

        var report = ClaudeImportService.BuildReport(
            preview, [linked, missed], [existingId],
            peopleCreated: 1, linkedCount: 1, relCreated: 0);

        report.Should().Contain("🔗 LINKED   Marc Rosenberg");
        report.Should().Contain("➕ CREATED  Douglas Rosenberg");
        report.Should().Contain("had candidate Douglas Rosenberg (0.98) — created new instead");
        report.Should().Contain("Matched against 53 existing people");
    }

    [Theory]
    [InlineData("1894-07-04", 1894, 7, 4)]   // ISO full
    [InlineData("1886-05", 1886, 5, 1)]      // ISO year-month
    [InlineData("1865", 1865, 1, 1)]         // year only
    [InlineData("6/3/1917", 1917, 6, 3)]     // US M/D/YYYY (June 3, not March 6)
    [InlineData("10/15/1892", 1892, 10, 15)] // US MM/DD/YYYY
    [InlineData("5/1886", 1886, 5, 1)]       // M/YYYY
    public void ParseDate_HandlesSourceAndIsoFormats(string raw, int y, int m, int d)
    {
        ClaudeImportService.ParseDate(raw).Should().Be(new DateOnly(y, m, d));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void ParseDate_ReturnsNullForUnparseable(string? raw)
    {
        ClaudeImportService.ParseDate(raw).Should().BeNull();
    }

    private static byte[] BuildSamplePdf(params string[] pageTexts)
    {
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdfDoc = new PdfDocument(writer))
        using (var doc = new Document(pdfDoc))
        {
            foreach (var pageText in pageTexts)
            {
                doc.Add(new Paragraph(pageText));
                if (pageText != pageTexts[^1])
                    doc.Add(new AreaBreak());
            }
        }
        return ms.ToArray();
    }
}
