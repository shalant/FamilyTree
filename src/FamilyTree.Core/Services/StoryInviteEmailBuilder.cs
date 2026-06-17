using System.Net;

namespace FamilyTree.Core.Services;

public static class StoryInviteEmailBuilder
{
    public static string Build(
        string inviterName,
        string personName,
        string? personPhotoUrl,
        int? birthYear,
        int? deathYear,
        string? personalNote,
        string respondLink)
    {
        var safeInviter = WebUtility.HtmlEncode(inviterName);
        var safePerson = WebUtility.HtmlEncode(personName);

        var lifeDates = (birthYear, deathYear) switch
        {
            (int b, int d) => $"{b} – {d}",
            (int b, null) => $"b. {b}",
            _ => null,
        };

        var photoBlock = !string.IsNullOrWhiteSpace(personPhotoUrl)
            ? $"""
              <img src="{WebUtility.HtmlEncode(personPhotoUrl)}" alt="{safePerson}"
                   style="width:72px;height:72px;border-radius:50%;object-fit:cover;border:3px solid #E1F5EE;" />
              """
            : $"""
              <div style="width:72px;height:72px;border-radius:50%;background:#E1F5EE;
                  display:inline-flex;align-items:center;justify-content:center;
                  font-family:Georgia,serif;font-size:24px;font-weight:700;color:#085041;">
                {WebUtility.HtmlEncode(Initials(personName))}
              </div>
              """;

        var personBlock = birthYear is null && deathYear is null && string.IsNullOrWhiteSpace(personPhotoUrl)
            ? "" // no person details to show — keep email minimal for typed-name invites
            : $"""
              <div style="text-align:center;margin-bottom:24px;">
                {photoBlock}
                <div style="margin-top:10px;font-family:Georgia,serif;font-size:17px;font-weight:600;color:#1a1a18;">
                  {safePerson}
                </div>
                {(lifeDates is null ? "" : $"""<div style="font-size:12px;color:#888884;margin-top:2px;">{lifeDates}</div>""")}
              </div>
              """;

        var noteBlock = string.IsNullOrWhiteSpace(personalNote)
            ? ""
            : $"""
              <div style="background:#f7f5f0;border-left:3px solid #5DCAA5;border-radius:6px;
                  padding:14px 18px;margin:0 0 24px;font-style:italic;color:#555551;font-size:14px;line-height:1.6;">
                "{WebUtility.HtmlEncode(personalNote)}"
              </div>
              """;

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f0ede8;font-family:Georgia,'Times New Roman',serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f0ede8;padding:40px 16px;">
                <tr><td align="center">
                  <table width="100%" style="max-width:520px;" cellpadding="0" cellspacing="0">

                    <!-- Header -->
                    <tr>
                      <td style="background:#085041;border-radius:12px 12px 0 0;padding:32px 40px 28px;text-align:center;">
                        <div style="font-family:Georgia,serif;font-size:26px;font-weight:700;color:#ffffff;letter-spacing:0.5px;">
                          ArborKin
                        </div>
                        <div style="font-size:12px;color:#9FE1CB;letter-spacing:2px;text-transform:uppercase;margin-top:4px;">
                          Family Tree
                        </div>
                      </td>
                    </tr>

                    <!-- Body -->
                    <tr>
                      <td style="background:#ffffff;padding:40px 40px 32px;">

                        <h1 style="margin:0 0 16px;font-family:Georgia,serif;font-size:21px;font-weight:700;color:#1a1a18;text-align:center;line-height:1.4;">
                          {safeInviter} would love to hear<br/>your memory of {safePerson}
                        </h1>

                        {personBlock}

                        <p style="margin:0 0 20px;font-size:14px;color:#666662;text-align:center;line-height:1.7;">
                          A favorite story, a small moment, a detail only you remember —
                          anything you'd like to share will be treasured.
                        </p>

                        {noteBlock}

                        <!-- CTA button -->
                        <div style="text-align:center;margin:28px 0 8px;">
                          <a href="{respondLink}"
                             style="display:inline-block;background:#085041;color:#ffffff;text-decoration:none;
                                    font-family:Georgia,serif;font-size:15px;font-weight:600;
                                    padding:14px 36px;border-radius:8px;letter-spacing:0.3px;">
                            Share your memory →
                          </a>
                        </div>

                        <p style="margin:24px 0 0;font-size:12px;color:#999994;text-align:center;line-height:1.6;">
                          Button not working? Copy and paste this link into your browser:<br>
                          <a href="{respondLink}" style="color:#0F6E56;word-break:break-all;">{respondLink}</a>
                        </p>
                      </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                      <td style="background:#085041;border-radius:0 0 12px 12px;padding:20px 40px;text-align:center;">
                        <p style="margin:0;font-size:11px;color:#9FE1CB;line-height:1.6;">
                          ArborKin · Family history, beautifully kept
                        </p>
                      </td>
                    </tr>

                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }
}
