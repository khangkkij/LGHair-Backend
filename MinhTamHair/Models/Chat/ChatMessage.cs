namespace MinhTamHair.Models.Chat
{
    /// <summary>
    /// Đại diện cho một tin nhắn trong Sub-collection "Messages" của ChatRoom.
    /// Firestore path: ChatRooms/{roomId}/Messages/{messageId}
    /// </summary>
    public class ChatMessage
    {
        public string? Id { get; set; }
        /// <summary>"Customer" hoặc "Staff"</summary>
        public string SenderRole { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsRead { get; set; } = false;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
