using MinhTamHair.Models.Chat;
using System.Text;
using System.Text.Json;

namespace MinhTamHair.Services.Chat
{
    /// <summary>
    /// Service giao tiếp với Firebase Firestore thông qua REST API.
    /// Không cần file Service Account JSON — chỉ cần ProjectId và WebApiKey từ appsettings.
    /// </summary>
    public interface IFirestoreService
    {
        Task<string> CreateChatRoomAsync(ChatRoom room);
        Task AddMessageAsync(string roomId, ChatMessage message);
        Task UpdateChatRoomLastMessageAsync(string roomId, string lastMessage, bool forceActive = false);
        Task ToggleRoomStatusAsync(string roomId, string status);
    }

    public class FirestoreService : IFirestoreService
    {
        private readonly HttpClient _httpClient;
        private readonly string _projectId;
        private readonly string _baseUrl;

        public FirestoreService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _projectId = configuration["Firebase:ProjectId"]
                ?? throw new InvalidOperationException("Firebase:ProjectId chưa được cấu hình.");

            // Base URL của Firestore REST API (không cần xác thực vì dùng Rules public cho dev)
            _baseUrl = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";
        }

        /// <summary>
        /// Tạo phòng chat mới trong Collection "ChatRooms"
        /// </summary>
        public async Task<string> CreateChatRoomAsync(ChatRoom room)
        {
            var body = new
            {
                fields = new
                {
                    customerName = new { stringValue = room.CustomerName },
                    customerEmail = new { stringValue = room.CustomerEmail },
                    lastMessage = new { stringValue = "" },
                    status = new { stringValue = "Active" },
                    unreadCount = new { integerValue = "0" },
                    createdAt = new { timestampValue = DateTime.UtcNow.ToString("o") },
                    updatedAt = new { timestampValue = DateTime.UtcNow.ToString("o") }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/ChatRooms", content);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Firestore API Error ({response.StatusCode}): {errContent}");
            }

            var result = await response.Content.ReadAsStringAsync();
            var doc = JsonSerializer.Deserialize<JsonElement>(result);
            var name = doc.GetProperty("name").GetString() ?? "";
            // Lấy phần ID cuối cùng từ đường dẫn
            return name.Split('/').Last();
        }

        /// <summary>
        /// Thêm tin nhắn vào Sub-collection "Messages" của phòng chat
        /// </summary>
        public async Task AddMessageAsync(string roomId, ChatMessage message)
        {
            var body = new
            {
                fields = new
                {
                    senderRole = new { stringValue = message.SenderRole },
                    senderName = new { stringValue = message.SenderName },
                    content = new { stringValue = message.Content },
                    isRead = new { booleanValue = false },
                    timestamp = new { timestampValue = DateTime.UtcNow.ToString("o") }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var url = $"{_baseUrl}/ChatRooms/{roomId}/Messages";
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Cập nhật lastMessage và updatedAt của phòng chat (hiển thị ở danh sách Admin)
        /// </summary>
        public async Task UpdateChatRoomLastMessageAsync(string roomId, string lastMessage, bool forceActive = false)
        {
            object body;
            string url;
            if (forceActive)
            {
                body = new
                {
                    fields = new
                    {
                        lastMessage = new { stringValue = lastMessage },
                        status = new { stringValue = "Active" },
                        updatedAt = new { timestampValue = DateTime.UtcNow.ToString("o") }
                    }
                };
                url = $"{_baseUrl}/ChatRooms/{roomId}?updateMask.fieldPaths=lastMessage&updateMask.fieldPaths=status&updateMask.fieldPaths=updatedAt";
            }
            else
            {
                body = new
                {
                    fields = new
                    {
                        lastMessage = new { stringValue = lastMessage },
                        updatedAt = new { timestampValue = DateTime.UtcNow.ToString("o") }
                    }
                };
                url = $"{_baseUrl}/ChatRooms/{roomId}?updateMask.fieldPaths=lastMessage&updateMask.fieldPaths=updatedAt";
            }

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Đổi trạng thái phòng chat: "Active" hoặc "Closed"
        /// </summary>
        public async Task ToggleRoomStatusAsync(string roomId, string status)
        {
            var body = new
            {
                fields = new
                {
                    status = new { stringValue = status },
                    updatedAt = new { timestampValue = DateTime.UtcNow.ToString("o") }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var url = $"{_baseUrl}/ChatRooms/{roomId}?updateMask.fieldPaths=status&updateMask.fieldPaths=updatedAt";
            var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }
}
