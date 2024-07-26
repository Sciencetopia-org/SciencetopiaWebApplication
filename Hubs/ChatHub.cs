using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Sciencetopia.Models;
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
                SentTime = DateTimeOffset.UtcNow,
                SenderId = senderId,
                ReceiverId = receiverId,
                ConversationId = conversation.Id,
                IsRead = false // Assuming the message is unread when first sent
            };

            conversation.Messages.Add(message);
            await _context.SaveChangesAsync();

            // After saving the message, convert it to DTO before sending
            var messageDto = new MessageWithUserDetailsDTO
            {
                Id = message.Id,
                Content = message.Content,
                SentTime = message.SentTime,
                Sender = new UserDetailsDTO
                {
                    Id = message.SenderId,
                    UserName = await _context.Users
                        .Where(u => u.Id == message.SenderId)
                        .Select(u => u.UserName)
                        .FirstOrDefaultAsync(),
                    AvatarUrl = await _context.Users
                        .Where(u => u.Id == message.SenderId)
                        .Select(u => u.AvatarUrl)
                        .FirstOrDefaultAsync()
                }
            };

            var lastMessageSentTime = message.SentTime;

            // Send the message to the receiver
            await Clients.User(receiverId).SendAsync("ReceiveMessage", conversationId, messageDto);

            // Update unread message count for the receiver
            var unreadMessageCount = await _context.Messages
                .Where(m => m.ReceiverId == receiverId && !m.IsRead)
                .CountAsync();

            // Notify client of updated message count per conversation
            var conversationMessageCount = await _context.Messages
                .Where(m => m.ReceiverId == receiverId && m.ConversationId == conversationId && !m.IsRead)
                .CountAsync();

            // Send notification to the receiver
            await Clients.User(receiverId).SendAsync("updateMessages", unreadMessageCount);
            await Clients.User(receiverId).SendAsync("updateConversationMessages", conversationId, conversationMessageCount);
        }

        public async Task JoinConversation(string conversationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);

            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.SentTime)
                .Select(m => new MessageDTO
                {
                    Id = m.Id,
                    Content = m.Content,
                    SentTime = m.SentTime,
                    SenderId = m.SenderId,
                    ReceiverId = m.ReceiverId
                })
                .ToListAsync();

            await Clients.Caller.SendAsync("LoadHistory", messages);
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;

            if (userId != null)
            {
                // Retrieve all conversations where the user is involved
                var conversations = await _context.Conversations
                    .Where(c => c.Messages.Any(m => m.SenderId == userId || m.ReceiverId == userId))
                    .ToListAsync();

                // Loop through each conversation and get the count of unread messages for that conversation
                foreach (var conversation in conversations)
                {
                    var unreadMessageCount = await _context.Messages
                        .Where(m => m.ConversationId == conversation.Id && m.ReceiverId == userId && !m.IsRead)
                        .CountAsync();

                    // Send the unread message count for each conversation to the user
                    await Clients.Caller.SendAsync("updateConversationMessages", conversation.Id, unreadMessageCount);
                }

                // Send the total unread message count to the connected user
                var totalUnreadMessageCount = await _context.Messages
                    .Where(m => m.ReceiverId == userId && !m.IsRead)
                    .CountAsync();

                await Clients.Caller.SendAsync("updateMessages", totalUnreadMessageCount);
            }

            await base.OnConnectedAsync();
        }

        public async Task MarkMessagesAsRead(string conversationId, string userId)
        {
            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId && m.ReceiverId == userId && !m.IsRead)
                .ToListAsync();

            foreach (var message in messages)
            {
                message.IsRead = true;
            }

            await _context.SaveChangesAsync();

            // Notify client of updated message count
            var receiverMessageCount = await _context.Messages
                .Where(m => m.ReceiverId == userId && !m.IsRead)
                .CountAsync();

            // Notify client of updated message count per conversation
            var conversationMessageCount = await _context.Messages
                .Where(m => m.ReceiverId == userId && m.ConversationId == conversationId && !m.IsRead)
                .CountAsync();

            await Clients.User(userId).SendAsync("updateMessages", receiverMessageCount);
            await Clients.User(userId).SendAsync("updateConversationMessages", conversationId, conversationMessageCount);
        }
    }
}
