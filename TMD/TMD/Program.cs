using Microsoft.EntityFrameworkCore;
using TMD.Models;
using TMDSystem.Helpers;
using TMDSystem.Services;
using TMDSystem.Hubs; // ✅ THÊM DÒNG NÀY

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();

// Database Context
builder.Services.AddDbContext<TmdContext>(options =>
	options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Session configuration
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromHours(8);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
});

// HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// HttpClient cho Reverse Geocoding
builder.Services.AddHttpClient();

// Helper
builder.Services.AddScoped<AuditHelper>();
builder.Services.AddHostedService<AutoRejectRequestsService>();

// ✅ SignalR
builder.Services.AddSignalR();

// QUAN TRỌNG: Cấu hình giới hạn kích thước file upload 10MB
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
	options.MultipartBodyLengthLimit = 10_485_760; // 10MB
	options.ValueLengthLimit = 10_485_760;
});

// Cấu hình Kestrel
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
	options.Limits.MaxRequestBodySize = 10_485_760; // 10MB
});

// Cấu hình web.config cho IIS (nếu cần)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
	serverOptions.Limits.MaxRequestBodySize = 10_485_760; // 10MB
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Home/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // Cho phép truy cập file tĩnh từ wwwroot

app.UseRouting();
app.UseSession(); // Phải đặt trước UseAuthorization
app.UseAuthorization();

// ✅✅✅ THÊM DÒNG NÀY - QUAN TRỌNG NHẤT ✅✅✅
app.MapHub<NotificationHub>("/notificationHub");

app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Account}/{action=Login}/{id?}");

// Tạo thư mục uploads nếu chưa có
var uploadsPath = Path.Combine(app.Environment.WebRootPath, "uploads", "attendance");
if (!Directory.Exists(uploadsPath))
{
	Directory.CreateDirectory(uploadsPath);
	Console.WriteLine($"✅ Created uploads directory: {uploadsPath}");
}
else
{
	Console.WriteLine($"📁 Uploads directory exists: {uploadsPath}");
}

// ✅ THÊM LOG SIGNALR
Console.WriteLine("╔════════════════════════════════════════════╗");
Console.WriteLine("║     🚀 TMD SYSTEM IS RUNNING...           ║");
Console.WriteLine("╚════════════════════════════════════════════╝");
Console.WriteLine($"📁 Upload folder: {uploadsPath}");
Console.WriteLine("⏰ Using SERVER TIME for all attendance records");
Console.WriteLine("🌍 Reverse Geocoding: OpenStreetMap Nominatim API");
Console.WriteLine("📸 Max file size: 10MB (JPG, JPEG, PNG)");
Console.WriteLine("🔔 SignalR Hub: /notificationHub"); // ✅ THÊM LOG NÀY
Console.WriteLine("══════════════════════════════════════════════");

app.Run();