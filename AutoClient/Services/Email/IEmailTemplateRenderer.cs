namespace AutoClient.Services.Email;

public interface IEmailTemplateRenderer
{
    (string Subject, string HtmlBody) Render(EmailTemplateType templateType, EmailTemplateModel model);
}

public enum EmailTemplateType
{
    CarReady,
    UpcomingVisit,
    PartsNeeded
}

public class EmailTemplateModel
{
    public string ClientName { get; set; } = "";
    public string WorkshopName { get; set; } = "";
    public string WorkshopPhone { get; set; } = "";
    public string? VehiclePlate { get; set; }
    public string? VehicleBrand { get; set; }
    public string? VehicleModel { get; set; }
    public DateTime? ServiceDate { get; set; }
    public decimal? ServiceCost { get; set; }
    public DateTime? NextServiceDate { get; set; }
    public string? NextServiceMileage { get; set; }
    public string? PartsDescription { get; set; }
}
