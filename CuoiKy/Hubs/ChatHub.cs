using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;

namespace CuoiKy.Hubs
{
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, string> WaitingUsers = new ConcurrentDictionary<string, string>();

        // User gửi tin nhắn (mặc định là Khách)
        public void SendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Luôn sử dụng tên mặc định "Khách"
            string userName = "Khách";

            // Đảm bảo user có trong danh sách
            if (!WaitingUsers.ContainsKey(Context.ConnectionId))
            {
                WaitingUsers[Context.ConnectionId] = userName;
            }

            Clients.Group("Staffs").receiveUserMessage(userName, message);
        }

        // User yêu cầu hỗ trợ (tự động đặt tên Khách)
        public void RequestSupport()
        {
            string userName = "Khách";

            WaitingUsers[Context.ConnectionId] = userName;

            // Chỉ thông báo cho user
            Clients.Caller.receiveSystemMessage("Đang chờ nhân viên...");

            // Cập nhật danh sách cho nhân viên
            BroadcastWaitingUsers();
        }

        // Nhân viên đăng nhập
        public void StaffLogin(string staffName)
        {
            if (string.IsNullOrWhiteSpace(staffName))
                staffName = "Nhân viên";

            Groups.Add(Context.ConnectionId, "Staffs");
            BroadcastWaitingUsers();
        }

        // Nhân viên gửi tin cho user
        public void StaffSendToUser(string userConnectionId, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(userConnectionId))
                return;

            Clients.Client(userConnectionId).receiveMessage("Nhân viên", message);
            Clients.Caller.receiveMessage("Bạn", message);
        }

        // Nhân viên chấp nhận hỗ trợ
        public void StaffAcceptUser(string userConnectionId)
        {
            string userName;
            if (WaitingUsers.TryGetValue(userConnectionId, out userName))
            {
                Clients.Client(userConnectionId).receiveSystemMessage("Nhân viên đã kết nối");
            }
        }

        // Nhân viên đóng trò chuyện
        public void StaffCloseChat(string userConnectionId)
        {
            string userName;
            if (WaitingUsers.TryRemove(userConnectionId, out userName))
            {
                Clients.Client(userConnectionId).receiveSystemMessage("Nhân viên đã đóng trò chuyện");
                Clients.Caller.receiveSystemMessage("Đã đóng chat với " + userName);
                BroadcastWaitingUsers();
            }
        }

        // Khi ngắt kết nối
        public override Task OnDisconnected(bool stopCalled)
        {
            string removed;
            if (WaitingUsers.TryRemove(Context.ConnectionId, out removed))
            {
                BroadcastWaitingUsers();
            }

            return base.OnDisconnected(stopCalled);
        }

        // Gửi danh sách user chờ cho nhân viên
        private void BroadcastWaitingUsers()
        {
            var users = WaitingUsers
                .Select(u => new { ConnectionId = u.Key, UserName = u.Value })
                .ToList();

            Clients.Group("Staffs").receiveWaitingUsers(users);
        }
    }
}