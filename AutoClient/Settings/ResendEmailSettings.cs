namespace AutoClient.Settings;

/// <summary>
/// Configuration settings for Resend email provider.
/// </summary>
public class ResendEmailSettings
{
    /// <summary>
    /// The Resend API key for authentication.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Resend API base URL (default: https://api.resend.com).
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.resend.com";

    /// <summary>
    /// The verified sender email address.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// The display name for the sender.
    /// </summary>
    public string FromName { get; set; } = "AutoClient";
}
