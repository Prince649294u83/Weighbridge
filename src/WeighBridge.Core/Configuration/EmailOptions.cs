namespace WeighBridge.Core.Configuration;

/// <summary>
/// Configuration options for the Email service.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; set; } = false;
    public string Frequency { get; set; } = "Email only Final Entry";
    public bool EmailPdf { get; set; } = false;
    public string SenderName { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SmtpServer { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string[] Recipients { get; set; } = Array.Empty<string>();
}
