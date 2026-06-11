namespace MinhTamHair.Models.Chat
{
    /// <summary>
    /// Đại diện cho một phòng chat trong Firestore Collection "ChatRooms".
    /// </summary>
    public class ChatRoom
    {
        public string? Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        /// <summary>"Active" hoặc "Closed"</summary>
        public string Status { get; set; } = "Active";
        public int UnreadCount { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
