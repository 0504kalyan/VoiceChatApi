namespace VoiceChat.Api.Options;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;

    /// <summary>Gmail address used to authenticate with SMTP (leave empty to log OTP only in Development).</summary>
    public string SmtpUser { get; set; } = string.Empty;

    /// <summary>For Gmail, use an App Password (not your login password) when 2-Step Verification is on.</summary>
    public string SmtpPassword { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "VoiceChat";
}
