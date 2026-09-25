using CCL.MES.Hybrid.Web;

// Web MVP — giao diện CCL.MES.Hybrid.Razor chạy trong trình duyệt (Blazor Server).
// Mô hình _Host + MapBlazorHub + fallback: MỌI đường dẫn trả cùng một trang host,
// định tuyến + phân quyền diễn ra BÊN TRONG component (AuthorizeRouteView) — y
// như app MAUI. Không có [Authorize] ở tầng endpoint để phải cấu hình scheme riêng.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(o =>
{
    // Trên LAN nội bộ: báo lỗi chi tiết giúp gỡ sự cố, nhưng chỉ khi Development.
    o.DetailedErrors = builder.Environment.IsDevelopment();
});
builder.Services.AddCclWebHost(builder.Configuration);

var app = builder.Build();

Console.WriteLine($"[boot] web host — CclApi:BaseUrl => {builder.Configuration["CclApi:BaseUrl"]}");

app.UseRouting();

// .NET 9+: _framework/blazor.server.js được phục vụ qua MapStaticAssets, KHÔNG qua
// UseStaticFiles (đo 25-09: chỉ UseStaticFiles ⇒ blazor.server.js 404, trang trắng).
app.MapStaticAssets();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

/// <summary>Cho WebApplicationFactory trong test.</summary>
public partial class Program;
