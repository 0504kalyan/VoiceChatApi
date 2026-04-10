namespace VoiceChat.Api.Interfaces;

public interface IMailSender
{
    Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default);
}
