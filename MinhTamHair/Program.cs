using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using MinhTamHair.Services.Gemini;
using MinhTamHair.Services.LightX;
using MinhTamHair.Services.UploadIMG;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Zalo.local.json", optional: true, reloadOnChange: true);




// Add services to the container.
builder.Services.AddDbContext<MinhTamHairContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
    sqlServerOptionsAction: sqlOptions =>
    {
        // BẬT TÍNH NĂNG TỰ ĐỘNG THỬ LẠI KHI MẤT KẾT NỐI (Lỗi Transient)
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5, // Thử lại tối đa 5 lần
            maxRetryDelay: TimeSpan.FromSeconds(10), // Giữa mỗi lần thử cách nhau 10 giây
            errorNumbersToAdd: null); // Nếu có mã lỗi SQL cụ thể muốn bắt thêm thì điền vào đây
    }

    ));
// Cấu hình JSON để tránh lỗi vòng lặp khi truy vấn dữ liệu quan hệ (Voucher <-> KhachHang)
builder.Services.AddHttpClient();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Đảm bảo Frontend Vue nhận được maCode thay vì MaCode
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // Chống lỗi vòng lặp khi truy vấn dữ liệu quan hệ (Voucher <-> KhachHang)
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVueApp", policy =>
    {
        // Mỗi origin (cổng Vite khác nhau) phải được liệt kê — nếu thiếu, trình duyệt chặn CORS.
        policy.WithOrigins(
                  "http://localhost:5173",
                  "http://localhost:5174",
                  "https://localhost:5173",
                  "https://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Bắt buộc để gửi AuthToken qua Cookie
    });
});
// cấu hình redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetValue<string>("Redis");
    options.InstanceName = "MyApp:";
});
//builder.Services.AddDistributedMemoryCache();
// --- 4. Cấu hình Authentication (JWT + Cookie ) ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie()
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    };

	options.Events = new JwtBearerEvents
	{
		OnMessageReceived = context =>
		{
			// 1. Ưu tiên tìm Cookie của Admin/Nhân viên trước
			var token = context.Request.Cookies["adminAccessToken"];

			// 2. Nếu không có, tìm tiếp Cookie của Khách hàng (bạn check lại tên cookie khách hàng của bạn là gì để thay vào chữ "accessToken" nhé)
			if (string.IsNullOrEmpty(token))
			{
				token = context.Request.Cookies["accessToken"];
			}

			// 3. Đưa Token cho C# đọc
			if (!string.IsNullOrEmpty(token))
			{
				context.Token = token;
			}
			return Task.CompletedTask;
		}
	};
});
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Minh Tam Hair API", Version = "v1" });

    // 1. Thêm nút Authorize (Định nghĩa kiểu bảo mật là Bearer Token)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Vui lòng nhập token theo định dạng: Bearer {chuỗi_token_của_bạn}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // 2. Yêu cầu Swagger áp dụng Token này cho tất cả các API
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});
// --- 5. Cấu hình Phân quyền (Authorization) ---
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
    options.AddPolicy("StaffPolicy", policy => policy.RequireRole("Admin", "NhanVien"));
    options.AddPolicy("CustomerPolicy", policy => policy.RequireRole("KhachHang"));
});
builder.Services.AddMemoryCache();
builder.Services.AddTransient<IEmailService, EmailService>();
builder.Services.AddScoped<IQrEmailService, QrEmailService>();
builder.Services.AddHttpClient("ZaloPersonalBridgeProbe", client => client.Timeout = TimeSpan.FromSeconds(4));

builder.Services.AddHttpClient<IZaloService, ZaloService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});


builder.Services.Configure<CloudinarySetings>(builder.Configuration.GetSection("Cloudinary"));
//                  DI CLOUDINARY
builder.Services.AddScoped<IUploadIMG, UploadIMG>();
builder.Services.AddScoped<ILightXService, LightXService>();
builder.Services.AddHttpClient<GeminiService>();
//                  DI CLOUDINARY
var app = builder.Build();

//      Cấu hình CORS để cho phép Vue App truy cập API

    app.Use((context, next) =>
    {
	    context.Response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin-allow-popups");
	    return next();
    });




// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseDefaultFiles(); 
app.UseStaticFiles();

var uploadDirectory = Path.Combine(builder.Environment.ContentRootPath, "UserUploads");
if (!Directory.Exists(uploadDirectory))
{
    Directory.CreateDirectory(uploadDirectory);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadDirectory), // Cập nhật lại biến này thay vì viết gộp
    RequestPath = "/UserUploads"
});

app.UseCors("AllowVueApp"); // Bật CORS trước
app.UseAuthentication();    // Bật Xác thực trước
app.UseAuthorization();     // Bật Phân quyền sau

app.MapControllers();
app.MapFallbackToFile("/dist/index.html");
app.Run();
