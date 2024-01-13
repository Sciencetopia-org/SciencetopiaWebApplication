using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Sciencetopia.Services;

public class EmailSender : IEmailSender
{
    private readonly string _smtpServer;
    private readonly int _smtpPort;
    private readonly string _fromAddress;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;

    public EmailSender()
    {
        // Initialize these with your SMTP settings
        _smtpServer = "smtp.gmail.com";
        _smtpPort = 587; // Example SMTP port
        _fromAddress = "83035706yf@gmail.com";
        _smtpUsername = "83035706yf@gmail.com";
        _smtpPassword = "MyPinTheStrongest";
    }

    public async Task SendEmailAsync(string email, string subject, string message)
    {
        using var client = new SmtpClient(_smtpServer, _smtpPort)
        {
            Credentials = new NetworkCredential(_smtpUsername, _smtpPassword),
            EnableSsl = true
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(_fromAddress),
            Subject = subject,
            Body = message,
            IsBodyHtml = true
        };
        mailMessage.To.Add(email);

        await client.SendMailAsync(mailMessage);
    }
}
