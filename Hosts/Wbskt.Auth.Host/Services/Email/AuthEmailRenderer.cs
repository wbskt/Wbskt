using System.Net;
using System.Text;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// Turns an <see cref="EmailContent"/> into the HTML and plain-text bodies of one mail, in the
/// wbskt.com branding: the orange <c>w</c> tile and wordmark on the site's warm dark palette.
/// </summary>
/// <remarks>
/// <para>
/// <b>Email-safe, not web-safe.</b> Layout is nested presentation tables with every style inline, a
/// fixed 560px column and a bulletproof button, because Outlook on Windows renders with Word and
/// Gmail drops most of a <c>&lt;style&gt;</c> block. The <c>&lt;style&gt;</c> that is there only
/// narrows the padding on phones and is safe to lose.
/// </para>
/// <para>
/// <b>Every string from the content is HTML-encoded here</b>, including the heading and the
/// preheader. Usernames, tenant names and email addresses are chosen by users, and these bodies are
/// the one place in the platform where user-supplied text is rendered as markup in someone else's
/// client. The plain-text body carries them verbatim, since nothing renders it.
/// </para>
/// </remarks>
internal static class AuthEmailRenderer
{
    private const string Canvas = "#141413";
    private const string Card = "#1a1a19";
    private const string Rule = "#2a2a28";
    private const string Heading = "#f2f1ed";
    private const string Body = "#c9c6bf";
    private const string Muted = "#8f8b84";
    private const string Accent = "#e85d26";
    private const string OnAccent = "#ffffff";
    private const string Wordmark = "#ecebe7";

    private const string Sans = "Inter,-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";
    private const string Mono = "'JetBrains Mono',ui-monospace,SFMono-Regular,Menlo,Consolas,monospace";

    public static OutboundMail Render(AuthEmailOptions options, string to, EmailContent content) =>
        new(to, Subject(content.Subject), Text(options, content), Html(options, content));

    /// <summary>
    /// Flattens a composed subject line to a single line. A subject is a mail <em>header</em>, not
    /// markup, so HTML encoding is the wrong tool and would only make it unreadable — the risk here
    /// is a tenant name or product name carrying a line break and continuing into a header of the
    /// sender's choosing. Every control character goes, not just CR and LF, because which ones a
    /// given relay treats as a terminator is not worth reasoning about one at a time.
    /// </summary>
    private static string Subject(string value)
    {
        var sb = new StringBuilder(value.Length);
        bool lastWasSpace = false;

        foreach (char c in value)
        {
            bool isBreak = char.IsControl(c) || c == '\u2028' || c == '\u2029';
            if (isBreak || c == ' ')
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    private static string Text(AuthEmailOptions options, EmailContent content)
    {
        var sb = new StringBuilder();
        sb.Append(content.Heading).Append("\r\n\r\n");

        foreach (EmailBlock block in content.Blocks)
        {
            switch (block)
            {
                case EmailParagraph p:
                    sb.Append(p.Text).Append("\r\n\r\n");
                    break;
                case EmailNote n:
                    sb.Append(n.Text).Append("\r\n\r\n");
                    break;
                case EmailAction a:
                    sb.Append(a.Label).Append(":\r\n").Append(a.Url).Append("\r\n\r\n");
                    break;
            }
        }

        string site = options.WebsiteUrl.TrimEnd('/');
        sb.Append("-- \r\n");
        sb.Append(options.ProductName).Append(" · ").Append(site).Append("\r\n");
        sb.Append("Privacy: ").Append(site).Append("/privacy · Terms: ").Append(site).Append("/terms\r\n");

        return sb.ToString();
    }

    private static string Html(AuthEmailOptions options, EmailContent content)
    {
        var sb = new StringBuilder(8 * 1024);

        sb.Append($$"""
                    <!doctype html>
                    <html lang="en" xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
                    <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <meta http-equiv="X-UA-Compatible" content="IE=edge">
                    <meta name="x-apple-disable-message-reformatting">
                    <meta name="format-detection" content="telephone=no, date=no, address=no, email=no, url=no">
                    <meta name="color-scheme" content="dark">
                    <meta name="supported-color-schemes" content="dark">
                    <title>{{Encode(Subject(content.Subject))}}</title>
                    <!--[if mso]><noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript><![endif]-->
                    <!--[if !mso]><!--><link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&amp;family=JetBrains+Mono:wght@400&amp;display=swap" rel="stylesheet"><!--<![endif]-->
                    <style>
                      :root { color-scheme: dark; supported-color-schemes: dark; }
                      body { margin: 0; padding: 0; width: 100% !important; background-color: {{Canvas}}; }
                      @media only screen and (max-width: 600px) {
                        .wb-card { padding: 28px 22px 26px !important; }
                        .wb-heading { font-size: 22px !important; }
                      }
                    </style>
                    </head>
                    <body style="margin:0;padding:0;background-color:{{Canvas}};">
                    <div style="display:none;max-height:0;max-width:0;overflow:hidden;opacity:0;mso-hide:all;font-size:1px;line-height:1px;color:{{Canvas}};">{{Encode(content.Preheader)}}{{PreheaderPadding()}}</div>
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="{{Canvas}}" style="background-color:{{Canvas}};">
                    <tr><td align="center" style="padding:40px 16px 48px;">
                    <!--[if mso]><table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" align="center"><tr><td><![endif]-->
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">
                    <tr><td style="padding:0 4px 24px;">
                    {{Brand(options)}}
                    </td></tr>
                    <tr><td class="wb-card" bgcolor="{{Card}}" style="background-color:{{Card}};border-radius:20px;padding:40px 40px 36px;font-family:{{Sans}};">
                    <h1 class="wb-heading" style="margin:0 0 20px;font-family:{{Sans}};font-size:26px;line-height:1.25;letter-spacing:-0.02em;font-weight:500;color:{{Heading}};">{{Encode(content.Heading)}}</h1>
                    """);

        foreach (EmailBlock block in content.Blocks)
        {
            sb.Append(block switch
            {
                EmailParagraph p => Paragraph(p.Text),
                EmailNote n => Note(n.Text),
                EmailAction a => Action(a),
                _ => string.Empty
            });
        }

        sb.Append($$"""
                    </td></tr>
                    <tr><td style="padding:24px 4px 0;">
                    {{Footer(options)}}
                    </td></tr>
                    </table>
                    <!--[if mso]></td></tr></table><![endif]-->
                    </td></tr>
                    </table>
                    </body>
                    </html>
                    """);

        return sb.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>
    /// Zero-width filler after the preheader, so a client's inbox preview stops at our sentence instead
    /// of running on into the heading and the first paragraph.
    /// </summary>
    private static string PreheaderPadding() => string.Concat(Enumerable.Repeat("&#8199;&#847; ", 60));

    /// <summary>
    /// The site's logo lockup: the orange tile with the <c>w</c>, then the wordmark. The cell under the
    /// image is the tile's colour and the alt text is a white <c>w</c>, so with images blocked the
    /// header still reads as the brand.
    /// </summary>
    private static string Brand(AuthEmailOptions options) => $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
        <td width="28" height="28" align="center" valign="middle" bgcolor="{Accent}" style="width:28px;height:28px;background-color:{Accent};border-radius:7px;"><img src="{Encode(options.LogoUrl)}" width="28" height="28" alt="w" style="display:block;width:28px;height:28px;border:0;border-radius:7px;outline:none;text-decoration:none;color:{OnAccent};font-family:{Sans};font-size:17px;font-style:italic;font-weight:800;line-height:28px;text-align:center;"></td>
        <td style="padding-left:10px;font-family:{Sans};font-size:16px;line-height:28px;font-weight:600;letter-spacing:-0.01em;color:{Wordmark};">{Encode(options.ProductName.ToUpperInvariant())}</td>
        </tr></table>
        """;

    private static string Paragraph(string text) =>
        $"""<p style="margin:0 0 16px;font-family:{Sans};font-size:15px;line-height:1.6;color:{Body};">{Encode(text)}</p>""" + "\n";

    private static string Note(string text) =>
        $"""<p style="margin:0 0 12px;font-family:{Sans};font-size:13px;line-height:1.55;color:{Muted};">{Encode(text)}</p>""" + "\n";

    /// <summary>
    /// A bulletproof button: the colour is on the cell, so Outlook's Word engine, which ignores padding
    /// and radius on a link, still shows a solid button. The link is spelled out beneath it for clients
    /// that strip buttons and readers who would rather see where it goes.
    /// </summary>
    private static string Action(EmailAction action)
    {
        string href = Encode(action.Url);
        return $"""
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px 0 24px;"><tr>
                <td align="center" bgcolor="{Accent}" style="background-color:{Accent};border-radius:12px;mso-padding-alt:13px 22px;"><a href="{href}" target="_blank" style="display:inline-block;padding:13px 22px;font-family:{Sans};font-size:15px;line-height:18px;font-weight:500;color:{OnAccent};text-decoration:none;border-radius:12px;">{Encode(action.Label)}</a></td>
                </tr></table>
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 20px;"><tr>
                <td style="border-top:1px solid {Rule};padding-top:16px;font-family:{Sans};font-size:13px;line-height:1.55;color:{Muted};">Button not working? Paste this link into your browser:<br><a href="{href}" target="_blank" style="font-family:{Mono};font-size:12px;line-height:1.6;color:{Body};text-decoration:underline;word-break:break-all;">{href}</a></td>
                </tr></table>

                """;
    }

    private static string Footer(AuthEmailOptions options)
    {
        string site = options.WebsiteUrl.TrimEnd('/');
        string link = $"color:{Body};text-decoration:none;";
        return $"""
                <p style="margin:0;font-family:{Sans};font-size:12px;line-height:1.7;color:{Muted};">{Encode(options.ProductName)} · Made for makers<br><a href="{Encode(site)}" target="_blank" style="{link}">{Encode(Uri.TryCreate(site, UriKind.Absolute, out Uri? uri) ? uri.Host : site)}</a> &nbsp;·&nbsp; <a href="{Encode(site + "/privacy")}" target="_blank" style="{link}">Privacy</a> &nbsp;·&nbsp; <a href="{Encode(site + "/terms")}" target="_blank" style="{link}">Terms</a></p>
                """;
    }
}
