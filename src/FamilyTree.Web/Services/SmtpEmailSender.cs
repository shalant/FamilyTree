using System.Net;
using System.Net.Mail;
using FamilyTree.Core.Services;
using Microsoft.Extensions.Options;

namespace FamilyTree.Web.Services;

public class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var opt = options.Value;
#pragma warning disable SYSLIB0006 // SmtpClient is obsolete; MailKit is the recommended replacement
        using var client = new SmtpClient(opt.SmtpHost, opt.SmtpPort)
        {
            EnableSsl = opt.EnableSsl,
            Credentials = string.IsNullOrWhiteSpace(opt.Username)
                ? null
                : new NetworkCredential(opt.Username, opt.Password),
        };
        var from = new MailAddress(opt.FromAddress, opt.FromName);
        using var msg = new MailMessage { From = from, Subject = subject, Body = htmlBody, IsBodyHtml = true };
        msg.To.Add(to);
        await client.SendMailAsync(msg, ct);
#pragma warning restore SYSLIB0006
    }
}

public class EmailOptions
{
    public string  SmtpHost    { get; set; } = "";
    public int     SmtpPort    { get; set; } = 587;
    public bool    EnableSsl   { get; set; } = true;
    public string? Username    { get; set; }
    public string? Password    { get; set; }
    public string  FromAddress { get; set; } = "noreply@arborkin.app";
    public string  FromName    { get; set; } = "ArborKin";
}
