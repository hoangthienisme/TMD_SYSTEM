using Microsoft.AspNetCore.SignalR;

namespace TMDSystem.Hubs
{
	public class NotificationHub : Hub
	{
		public override async Task OnConnectedAsync()
		{
			var httpContext = Context.GetHttpContext();
			if (httpContext?.Session != null)
			{
				// 1. Admin Group
				var role = httpContext.Session.GetString("RoleName");
				if (role == "Admin")
				{
					await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
					Console.WriteLine($"✅ Admin connected: {Context.ConnectionId}");
				}

				// 2. User Group
				var userId = httpContext.Session.GetInt32("UserId");
				if (userId.HasValue)
				{
					await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
					Console.WriteLine($"✅ User_{userId} connected: {Context.ConnectionId}");
				}

				// 3. Department Group
				var deptId = httpContext.Session.GetInt32("DepartmentId");
				if (deptId.HasValue)
				{
					await Groups.AddToGroupAsync(Context.ConnectionId, $"Dept_{deptId}");
					Console.WriteLine($"✅ Dept_{deptId} connected: {Context.ConnectionId}");
				}
			}

			await base.OnConnectedAsync();
		}

		public override async Task OnDisconnectedAsync(Exception? exception)
		{
			Console.WriteLine($"❌ Disconnected: {Context.ConnectionId}");
			await base.OnDisconnectedAsync(exception);
		}
	}
}