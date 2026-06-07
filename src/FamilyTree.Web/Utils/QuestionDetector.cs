namespace FamilyTree.Web.Utils;

public static class QuestionDetector
{
    public static List<string> ExtractQuestions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.EndsWith("?"))
            .ToList();
    }
}