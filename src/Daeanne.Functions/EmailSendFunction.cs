using System.Text.Json;
using Azure;
using Azure.Communication.Email;
using Daeanne.Shared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Daeanne.Functions;

/// <summary>
/// Picks up outbound email messages from the daeanne-outbox Service Bus queue
/// and sends them via Azure Communication Services Email.
/// </summary>
public class EmailSendFunction(IConfiguration config, ILogger<EmailSendFunction> logger)
{
    [Function("EmailSend")]
    public async Task Run(
        [ServiceBusTrigger("daeanne-outbox", Connection = "ServiceBusConnection")] string messageBody)
    {
        BridgeEmailMessage email;
        try
        {
            email = JsonSerializer.Deserialize<BridgeEmailMessage>(messageBody)
                    ?? throw new InvalidOperationException("Deserialized to null");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "EmailSend: failed to deserialize message body");
            throw; // dead-letter
        }

        var acsConnStr = config["AcsEmailConnectionString"]
            ?? throw new InvalidOperationException("AcsEmailConnectionString not configured");
        var senderAddress = config["AcsEmailSenderAddress"]
            ?? throw new InvalidOperationException("AcsEmailSenderAddress not configured");

        var emailClient = new EmailClient(acsConnStr);

        var emailContent = new EmailContent(email.Subject)
        {
            PlainText = email.BodyText
        };
        if (!string.IsNullOrEmpty(email.BodyHtml))
            emailContent.Html = email.BodyHtml;

        var emailMessage = new EmailMessage(
            senderAddress: senderAddress,
            recipientAddress: email.To,
            content: emailContent);

        try
        {
            var op = await emailClient.SendAsync(WaitUntil.Completed, emailMessage);
            logger.LogInformation("EmailSend: sent to {To}, ACS operation id: {OpId}", email.To, op.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "EmailSend: ACS send failed for message to {To}", email.To);
            throw; // triggers SB retry / dead-letter after max delivery count
        }
    }
}
