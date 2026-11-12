namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// One piece of a mail's content, written once and rendered by <see cref="AuthEmailRenderer"/> as both
/// HTML and plain text. Every string is plain text: the HTML renderer encodes it, so a template never
/// writes markup and never has to remember to encode a username or a tenant name.
/// </summary>
internal abstract record EmailBlock;

/// <summary>A paragraph of the message.</summary>
internal sealed record EmailParagraph(string Text) : EmailBlock;

/// <summary>Smaller, quieter text: expiry, "if this was not you", why the mail was sent.</summary>
internal sealed record EmailNote(string Text) : EmailBlock;

/// <summary>The one thing the mail asks the reader to do: a button, with the link spelled out beneath it.</summary>
internal sealed record EmailAction(string Url, string Label) : EmailBlock;

/// <summary>A whole message before rendering.</summary>
internal sealed record EmailContent(string Subject, string Preheader, string Heading, IReadOnlyList<EmailBlock> Blocks);
