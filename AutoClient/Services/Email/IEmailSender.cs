namespace AutoClient.Services.Email;

/// <summary>
/// Abstraction for sending emails via HTTP-based providers.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email asynchronously.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="htmlBody">HTML content of the email.</param>
    /// <param name="textBody">Optional plain-text fallback.</param>
    /// <param name="attachments">Optional file attachments.</param>
    /// <param name="bcc">Optional BCC addresses.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if sent successfully, false otherwise.</returns>
    Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        IEnumerable<EmailAttachment>? attachments = null,
        IEnumerable<string>? bcc = null,
        CancellationToken ct = default);
}

/// <summary>
/// Represents an email attachment.
/// </summary>
public class EmailAttachment
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}
