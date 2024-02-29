using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

public class CustomUserIdProvider : IUserIdProvider
{
    public string GetUserId(HubConnectionContext connection)
    {
        // Extract the user ID from the connection's ClaimsPrincipal, similar to your controller
        return connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
    }
}
