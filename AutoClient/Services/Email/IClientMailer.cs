namespace AutoClient.Services.Email;

public interface IClientMailer
{
    Task SendOtpAsync(string to, string workshopName, string code, CancellationToken ct = default);

    Task SendServiceCompletedAsync(
        string to, string clientName, string plate, DateTime completedAt, decimal? cost,
        CancellationToken ct = default);

    Task SendUpcomingServiceReminderAsync(
        string to, string clientName, string plate, DateTime nextDate, string? nextMileageTarget,
        CancellationToken ct = default);

    Task SendTemplateAsync(string to, EmailTemplateType templateType, EmailTemplateModel model, CancellationToken ct = default);
}
