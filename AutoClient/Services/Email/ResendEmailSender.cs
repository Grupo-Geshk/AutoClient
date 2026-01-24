using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoClient.Settings;
using Microsoft.Extensions.Options;

namespace AutoClient.Services.Email;

/// <summary>
/// Email sender implementation using Resend HTTP API.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendEmailSettings _settings;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        HttpClient httpClient,
        IOptions<ResendEmailSettings> settings,
        ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
    }

    public async Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        IEnumerable<EmailAttachment>? attachments = null,
        IEnumerable<string>? bcc = null,
        CancellationToken ct = default)
    {
        try
        {
            var fromAddress = string.IsNullOrWhiteSpace(_settings.FromName)
                ? _settings.FromAddress
                : $"{_settings.FromName} <{_settings.FromAddress}>";

            var payload = new Dictionary<string, object>
            {
                ["from"] = fromAddress,
                ["to"] = new[] { to },
                ["subject"] = subject,
                ["html"] = htmlBody
            };

            if (!string.IsNullOrWhiteSpace(textBody))
            {
                payload["text"] = textBody;
            }

            if (bcc?.Any() == true)
            {
                payload["bcc"] = bcc.ToArray();
            }

            if (attachments?.Any() == true)
            {
                payload["attachments"] = attachments.Select(a => new
                {
                    filename = a.FileName,
                    content = Convert.ToBase64String(a.Content),
                    content_type = a.ContentType
                }).ToArray();
            }

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/emails", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Email sent successfully via Resend. To: {To}, Subject: {Subject}, Status: {StatusCode}",
                    to, subject, (int)response.StatusCode);
                return true;
            }

            _logger.LogError(
                "Failed to send email via Resend. To: {To}, Subject: {Subject}, Status: {StatusCode}, Response: {Response}",
                to, subject, (int)response.StatusCode, responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Exception sending email via Resend. To: {To}, Subject: {Subject}",
                to, subject);
            return false;
        }
    }
}
