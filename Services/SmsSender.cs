using System.Threading.Tasks;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Sciencetopia.Services;

public class SmsSender : ISmsSender
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;

    public SmsSender()
    {
        _accountSid = "your_account_sid";
        _authToken = "your_auth_token";
        _fromNumber = "+1234567890"; // Your Twilio number
        TwilioClient.Init(_accountSid, _authToken);
    }

    public async Task SendSmsAsync(string number, string message)
    {
        await MessageResource.CreateAsync(
            to: new Twilio.Types.PhoneNumber(number),
            from: new Twilio.Types.PhoneNumber(_fromNumber),
            body: message);
    }
}
