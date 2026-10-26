using System.Net;

namespace FamilyTree.Core.Services;

/// <summary>The email Admin › Users sends with a registration invite link.</summary>
public static class UserInviteEmailBuilder
{
    public static string Build(string inviterName, string registerLink, int expiresInDays)
    {
        var safeInviter = WebUtility.HtmlEncode(inviterName);
        var safeLink = WebUtility.HtmlEncode(registerLink);
        var expiry = expiresInDays == 1 ? "1 day" : $"{expiresInDays} days";

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
                          {safeInviter} has invited you<br/>to join the family tree
                        </h1>

                        <p style="margin:0 0 20px;font-size:14px;color:#666662;text-align:center;line-height:1.7;">
                          ArborKin is a private place where the family keeps its people,
                          photos and stories together. Create your account to see the tree
                          and add what you know.
                        </p>

                        <!-- CTA button -->
                        <div style="text-align:center;margin:28px 0 8px;">
                          <a href="{safeLink}"
                             style="display:inline-block;background:#085041;color:#ffffff;text-decoration:none;
                                    font-family:Georgia,serif;font-size:15px;font-weight:600;
                                    padding:14px 36px;border-radius:8px;letter-spacing:0.3px;">
                            Create your account →
                          </a>
                        </div>

                        <p style="margin:24px 0 0;font-size:12px;color:#999994;text-align:center;line-height:1.6;">
                          This link works for {expiry} and only once.<br>
                          Button not working? Copy and paste this link into your browser:<br>
                          <a href="{safeLink}" style="color:#0F6E56;word-break:break-all;">{safeLink}</a>
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
}
