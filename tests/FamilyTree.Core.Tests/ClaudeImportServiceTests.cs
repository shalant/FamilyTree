using System.Text;
using FamilyTree.Core.Services;
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
    public void ExtractTextFromDocument_UnsupportedExtension_ThrowsNotSupportedException()
    {
        var act = () => ClaudeImportService.ExtractTextFromDocument([], "family.docx");

        act.Should().Throw<NotSupportedException>();
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
