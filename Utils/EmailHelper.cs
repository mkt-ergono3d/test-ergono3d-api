using MimeKit;
using MailKit;
using MailKit.Net.Smtp;

namespace inso.Utils
{
    public class EmailHelper
    {
        private readonly ConfigurationManager? _configurationManager;

        private readonly ILogger<EmailHelper>? _logger;

        public EmailHelper(ConfigurationManager configurationManager, ILogger<EmailHelper> logger)
        {
            _configurationManager = configurationManager;
            _logger = logger;
        }

        public EmailHelper() {  }

        public async Task<bool> Send(string from, string to, string subject, string body, string subtype = "plain")
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("noreply", "noreply@ergono3d.com"));
                message.To.Add(new MailboxAddress(to.Split("@")[0], to));
                message.Subject = subject;
                message.Body = new TextPart(subtype) { Text = body };
                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync("smtp.zeptomail.com", 587);
                    await client.AuthenticateAsync("emailapikey", Environment.GetEnvironmentVariable("INSOLE_SMTP_PASSWORD") ?? throw new InvalidOperationException("Set INSOLE_SMTP_PASSWORD for this test environment."));
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Send email({to}) failed!");
                return false;
            }
        }

        public async Task SendMailWithBase64Attachment(string to, string base64)
        {
            var fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var message = new MimeKit.MimeMessage();
            message.From.Add(new MailboxAddress("noreply", "noreply@ergono3d.com"));
            message.To.Add(new MailboxAddress(to.Split("@")[0], to));
            message.Subject = "Ergono3d - " + fileName;
            fileName += ".stl";
            var body = @"
<html>
    <body>
        <div>Hi,</div>
        <div>Your custom 3D insole file has been generated and is attached below.</div>
        <div>This file is for personal use and professional evaluation only.</div>
        <div>Please ensure accuracy before printing or clinical application.</div>
        <div style=""margin-top:10px"">Best,</div>
        <div>The Ergono3D Team</div>
    </body>
</html>";
            var builder = new MimeKit.BodyBuilder { HtmlBody = body };
            byte[] fileBytes = Convert.FromBase64String(base64);
            builder.Attachments.Add(fileName, fileBytes, new MimeKit.ContentType("application", "octet-stream"));
            message.Body = builder.ToMessageBody();
            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                await client.ConnectAsync("smtp.zeptomail.com", 587);
                await client.AuthenticateAsync("emailapikey", Environment.GetEnvironmentVariable("INSOLE_SMTP_PASSWORD") ?? throw new InvalidOperationException("Set INSOLE_SMTP_PASSWORD for this test environment."));
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

    }
}
