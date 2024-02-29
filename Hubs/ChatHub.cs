using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Sciencetopia.Models; // Ensure this using directive points to where your models are defined
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System;
using Sciencetopia.Data;

namespace Sciencetopia.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task SendMessage(string conversationId, string senderId, string receiverId, string content)
        {
            // Ensure the conversation exists
            var conversation = await _context.Conversations
                                     .Include(c => c.Messages)
                                     .FirstOrDefaultAsync(c => c.Id == conversationId);

            // If the conversation does not exist, optionally create a new one (or handle as needed)
            if (conversation == null)
            {
                conversation = new Conversation
                {
                    Id = Guid.NewGuid().ToString(),
                    Messages = new List<Message>()
                };
                _context.Conversations.Add(conversation);
            }

            // Create and save the new message
            var message = new Message
            {
                Id = Guid.NewGuid().ToString(),
                Content = content,
                SentTime = DateTime.UtcNow,
                SenderId = senderId,
                ReceiverId = receiverId,
                ConversationId = conversation.Id,
                IsRead = false // Assuming the message is unread when first sent
            };

            conversation.Messages.Add(message);
            await _context.SaveChangesAsync();

            // After saving the message, convert it to DTO before sending
            var messageDto = new MessageDTO
            {
                Id = message.Id,
                Content = message.Content,
                SentTime = message.SentTime,
                SenderId = message.SenderId,
                ReceiverId = message.ReceiverId
            };

            await Clients.User(senderId).SendAsync("ReceiveMessage", messageDto);
            await Clients.User(receiverId).SendAsync("ReceiveMessage", messageDto);
        }

        public async Task JoinConversation(string conversationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);

            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.SentTime)
                .Select(m => new MessageDTO
                {
                    Content = m.Content,
                    SentTime = m.SentTime,
                    SenderId = m.SenderId
                    // Add other required fields but avoid circular references
                })
                .ToListAsync();

            await Clients.Caller.SendAsync("LoadHistory", messages);
        }

    }
}
