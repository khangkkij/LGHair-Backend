using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<MinhTamHairContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
// Cấu hình JSON để tránh lỗi vòng lặp khi truy vấn dữ liệu quan hệ (Voucher <-> KhachHang)
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
        //Tuyệt dổi ko được sửa cổng mặt dịnh là 5173 vì Vite/Vue sẽ chạy ở cổng này, nếu sửa sẽ bị lỗi CORS
        // thành viên nghe theo
        policy.WithOrigins("http://localhost:5173") // Cổng mặc định của Vite/Vue
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Bắt buộc để gửi AuthToken qua Cookie
    });
});

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

    // QUAN TRỌNG: Tự động lấy Token từ Cookie "AuthToken" để xác thực người dùng
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
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
builder.Services.AddAuthorization(options => {
    options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
    options.AddPolicy("StaffPolicy", policy => policy.RequireRole("Admin", "NhanVien"));
    options.AddPolicy("CustomerPolicy", policy => policy.RequireRole("KhachHang"));
});
builder.Services.AddMemoryCache();
builder.Services.AddTransient<IEmailService, EmailService>();

var app = builder.Build();
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

app.UseCors("AllowVueApp"); // Bật CORS trước

app.UseAuthentication();    // Bật Xác thực trước
app.UseAuthorization();     // Bật Phân quyền sau

app.MapControllers();

app.Run();
