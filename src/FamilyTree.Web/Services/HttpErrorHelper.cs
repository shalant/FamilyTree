namespace FamilyTree.Web.Services;

internal static class HttpErrorHelper
{
    public static async Task<string> ProblemDetail(
        HttpResponseMessage response, string context)
    {
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<ProblemDetailsDto>();
            if (!string.IsNullOrWhiteSpace(problem?.Detail)) return problem.Detail;
            if (!string.IsNullOrWhiteSpace(problem?.Title)) return problem.Title;
        }
        catch { /* ignore */ }

        return $"Error {(int)response.StatusCode} {response.StatusCode} while {context}.";
    }

    public static string HttpDetail(HttpRequestException ex) =>
        ex.StatusCode.HasValue
            ? $"(HTTP {(int)ex.StatusCode})"
            : "(no response from server — is the API running?)";

    private sealed class ProblemDetailsDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
    }
}