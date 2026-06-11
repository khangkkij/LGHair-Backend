using Microsoft.AspNetCore.Mvc;
using MinhTamHair.Models.Chat;
using MinhTamHair.Services.Chat;

namespace MinhTamHair.Controllers
{
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        private readonly IFirestoreService _firestoreService;

        public ChatController(IFirestoreService firestoreService)
        {
            _firestoreService = firestoreService;
        }

        // ───────────────────────────────────────────────────────────────
        // POST /api/chat/room
        // Tạo phòng chat mới khi khách hàng bắt đầu chat lần đầu
        // Body: { "customerName": "Nguyễn A", "customerEmail": "a@gmail.com" }
        // ───────────────────────────────────────────────────────────────
        [HttpPost("room")]
        public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerName))
                return BadRequest(new { message = "customerName không được để trống." });

            var room = new ChatRoom
            {
                CustomerName = request.CustomerName,
                CustomerEmail = request.CustomerEmail ?? ""
            };

            try
            {
                var roomId = await _firestoreService.CreateChatRoomAsync(room);
                return Ok(new { roomId, message = "Tạo phòng chat thành công." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi tạo phòng chat ở Firestore", error = ex.Message });
            }
        }

        // ───────────────────────────────────────────────────────────────
        // POST /api/chat/send
        // Gửi tin nhắn vào phòng chat (dùng cho cả Khách hàng lẫn Nhân viên)
        // Body: { "roomId": "abc", "senderRole": "Customer"|"Staff", "senderName": "Nguyễn A", "content": "Xin chào" }
        // ───────────────────────────────────────────────────────────────
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RoomId))
                return BadRequest(new { message = "roomId không được để trống." });

            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { message = "Nội dung tin nhắn không được để trống." });

            if (request.SenderRole != "Customer" && request.SenderRole != "Staff")
                return BadRequest(new { message = "senderRole phải là 'Customer' hoặc 'Staff'." });

            var message = new ChatMessage
            {
                SenderRole = request.SenderRole,
                SenderName = request.SenderName ?? request.SenderRole,
                Content = request.Content
            };

            // 1. Lưu tin nhắn vào sub-collection Messages
            await _firestoreService.AddMessageAsync(request.RoomId, message);

            // 2. Cập nhật lastMessage của phòng chat và mở lại phòng nếu là Khách hàng nhắn
            bool isCustomer = request.SenderRole == "Customer";
            await _firestoreService.UpdateChatRoomLastMessageAsync(request.RoomId, request.Content, isCustomer);

            return Ok(new { message = "Gửi tin nhắn thành công." });
        }

        // ───────────────────────────────────────────────────────────────
        // POST /api/chat/toggle-status/{roomId}
        // Nhân viên đóng hoặc mở lại phòng chat
        // Body: { "status": "Closed" } hoặc { "status": "Active" }
        // ───────────────────────────────────────────────────────────────
        [HttpPost("toggle-status/{roomId}")]
        public async Task<IActionResult> ToggleStatus(string roomId, [FromBody] ToggleStatusRequest request)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                return BadRequest(new { message = "roomId không được để trống." });

            if (request.Status != "Active" && request.Status != "Closed")
                return BadRequest(new { message = "status phải là 'Active' hoặc 'Closed'." });

            await _firestoreService.ToggleRoomStatusAsync(roomId, request.Status);
            return Ok(new { message = $"Phòng chat đã được chuyển sang trạng thái '{request.Status}'." });
        }
    }

    // ─── Request DTOs ───────────────────────────────────────────────────

    public class CreateRoomRequest
    {
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
    }

    public class SendMessageRequest
    {
        public string RoomId { get; set; } = string.Empty;
        /// <summary>"Customer" hoặc "Staff"</summary>
        public string SenderRole { get; set; } = string.Empty;
        public string? SenderName { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class ToggleStatusRequest
    {
        /// <summary>"Active" hoặc "Closed"</summary>
        public string Status { get; set; } = string.Empty;
    }
}
