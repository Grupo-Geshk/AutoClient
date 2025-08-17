namespace AutoClient.DTOs.Services;

public class UpdateServiceNotesDto
{
    public string Notes { get; set; } = string.Empty;
    public bool? Append { get; set; }
}
