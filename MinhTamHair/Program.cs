using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MinhTamHair.Models;
using MinhTamHair.SanPham.Controllers;
using MinhTamHair.Services;
using MinhTamHair.Services.Chat;
using MinhTamHair.Services.Cleanup;
using MinhTamHair.Services.Gemini;
using MinhTamHair.Services.LightX;
using MinhTamHair.Services.UploadIMG;
using StackExchange.Redis;
using static System.Net.WebRequestMethods;

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
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = true;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVueApp", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:3000",   // Dev Proxy HTTP
                  "https://localhost:3000",  // Dev Proxy HTTPS
                  "http://localhost:5173",   // Customer app HTTP
                  "http://localhost:5174",   // Admin app HTTP
                  "https://localhost:5173",  // Customer app HTTPS (basicSsl)
                  "https://localhost:5174",  // Admin app HTTPS (basicSsl)
                  "http://localhost:4173",  // Customer app HTTP
                  "https://localhost:4173",  // Customer app HTTPS (basicSsl)
                  "https://lghair-admin.netlify.app",
                  "https://lghair.netlify.app",
                  "https://lghair-customer.netlify.app")
              .SetIsOriginAllowed(_ => true) // Cho phép động các origin từ Netlify / Localhost
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
// cấu hình redis
//var redisConnectionString = builder.Configuration.GetValue<string>("Redis");
//builder.Services.AddStackExchangeRedisCache(options =>
//{
//options.Configuration = redisConnectionString;
//options.InstanceName = "MinhTamHair:";
//});

// --- Tự động phát hiện & Kiểm tra kết nối Redis ---
var redisConnectionString = builder.Configuration.GetValue<string>("Redis");
bool isRedisAvailable = false;
ConnectionMultiplexer? sharedRedisMultiplexer = null;

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    try
    {
        // Cấu hình timeout ngắn + KHÔNG abort khi mất kết nối tạm thời, để multiplexer
        // tự retry nền thay vì mỗi lệnh Redis phải chờ full retry chain (điều này là
        // nguyên nhân chính gây delay 15-30s trên mọi request khi Upstash chập chờn).
        var redisOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.ConnectTimeout = 3000;      // 3s để connect
        redisOptions.SyncTimeout = 3000;         // 3s cho mỗi lệnh đồng bộ (Get/Set...)
        redisOptions.AsyncTimeout = 3000;        // 3s cho mỗi lệnh bất đồng bộ
        redisOptions.ConnectRetry = 1;           // chỉ thử connect lại 1 lần, không lặp nhiều vòng
        redisOptions.AbortOnConnectFail = false; // KHÔNG throw ngay, để multiplexer tự reconnect nền
        redisOptions.ReconnectRetryPolicy = new StackExchange.Redis.LinearRetry(1000);

        // Tạo và DÙNG CHUNG một multiplexer duy nhất, thay vì tạo 1 cái để test rồi
        // dispose, sau đó tạo thêm 1 cái khác (không timeout) cho DI — đó là lỗi khiến
        // request bị block chờ connect/retry ở singleton factory phía dưới.
        // AbortOnConnectFail=false nên .Connect() không throw ngay cả khi Upstash đang
        // không phản hồi lúc khởi động — multiplexer vẫn được tạo và tự nối lại nền sau.
        sharedRedisMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisOptions);
        isRedisAvailable = sharedRedisMultiplexer.IsConnected;

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(sharedRedisMultiplexer);
            options.InstanceName = "LGHair:";
        });

        Console.WriteLine(isRedisAvailable
            ? ">>> [REDIS CACHE] Đã kết nối Redis Cloud thành công!"
            : ">>> [CACHE WARNING] Redis chưa kết nối được lúc khởi động, sẽ tự retry nền.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> [CACHE WARNING] Không thể kết nối Redis ({ex.Message}). Tự động chuyển về Memory Cache.");
    }
}

if (sharedRedisMultiplexer == null)
{
    // Không có connection string hoặc parse/connect lỗi ngay từ đầu (config sai, không
    // phải do mạng chập chờn) — chỉ trường hợp này mới thật sự fallback về Memory Cache.
    builder.Services.AddDistributedMemoryCache();
    Console.WriteLine(">>> [LOCAL CACHE] Đang chạy với Bộ nhớ đệm RAM Local.");
}

// Đăng ký thêm IConnectionMultiplexer để dùng khi cần xóa cache theo pattern (KEYS/SCAN),
// việc IDistributedCache không hỗ trợ trực tiếp (chỉ Get/Set/Remove theo đúng 1 key).
// Dùng lại đúng multiplexer đã tạo ở trên (nếu có) — KHÔNG tạo kết nối thứ hai không
// timeout, tránh mỗi lần resolve service bị treo chờ connect/retry.
// Luôn đăng ký (kể cả khi IsConnected=false lúc khởi động) vì ServiceCacheService /
// ProductCacheService inject IConnectionMultiplexer bắt buộc trong constructor —
// không đăng ký sẽ làm toàn bộ request tới các controller dùng 2 service này crash
// ngay ở bước DI resolve, kể cả các action không hề đụng tới cache (như GetByDichVu).
if (sharedRedisMultiplexer != null)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(sharedRedisMultiplexer);
}
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
                // 1. Ưu tiên Header Authorization do Vue gửi (Bearer Token)
                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    // Nếu Frontend đã gửi kèm Token chuẩn qua Header, kệ cho C# tự đọc
                    return Task.CompletedTask;
                }

                // 2. Nếu không có Header (ví dụ gọi từ Swagger, load ảnh...), tìm trong Cookie
                var token = context.Request.Cookies["adminAccessToken"];
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
            },
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    // 1. Kiểm tra trong Memory Cache trước (tốc độ cao)
                    var memCache = context.HttpContext.RequestServices.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                    long? revokedUnix = null;

                    if (memCache != null && memCache.TryGetValue($"UserRevokedAt_{userId}", out long cachedRevoked))
                    {
                        revokedUnix = cachedRevoked;
                    }
                    else
                    {
                        // 2. Kiểm tra trong Distributed / Redis Cache
                        var distCache = context.HttpContext.RequestServices.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
                        if (distCache != null)
                        {
                            var revokedStr = await distCache.GetStringAsync($"UserRevokedAt_{userId}");
                            if (!string.IsNullOrEmpty(revokedStr) && long.TryParse(revokedStr, out long distRevoked))
                            {
                                revokedUnix = distRevoked;
                                memCache?.Set($"UserRevokedAt_{userId}", distRevoked, TimeSpan.FromMinutes(10));
                            }
                        }
                    }

                    if (revokedUnix.HasValue)
                    {
                        // Lấy thời điểm token được cấp phát (iat)
                        var iatClaim = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat)?.Value
                                    ?? context.Principal?.FindFirst("iat")?.Value;

                        if (long.TryParse(iatClaim, out long iatUnix))
                        {
                            if (iatUnix <= revokedUnix.Value)
                            {
                                context.Fail("Phiên đăng nhập (JWT Token) đã bị thu hồi do người dùng đăng xuất trên mọi thiết bị.");
                                return;
                            }
                        }
                        else
                        {
                            var validFrom = context.SecurityToken?.ValidFrom;
                            if (validFrom.HasValue && validFrom.Value.ToUniversalTime() <= DateTimeOffset.FromUnixTimeSeconds(revokedUnix.Value).UtcDateTime)
                            {
                                context.Fail("Phiên đăng nhập (JWT Token) đã bị thu hồi do người dùng đăng xuất trên mọi thiết bị.");
                                return;
                            }
                        }
                    }
                }
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

// ===== ĐĂNG KÝ SALARY SERVICE =====
// ISalaryService → SalaryService: Tính lương động real-time, không lưu vào DB
builder.Services.AddScoped<MinhTamHair.Admin.Services.ISalaryService, MinhTamHair.Admin.Services.SalaryService>();
// ===== END SALARY SERVICE =====
builder.Services.AddTransient<IEmailService, EmailService>();
builder.Services.AddScoped<IQrEmailService, QrEmailService>();
builder.Services.AddHttpClient("ZaloPersonalBridgeProbe", client => client.Timeout = TimeSpan.FromSeconds(4));

builder.Services.AddHttpClient<IZaloService, ZaloService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.Configure<EInvoiceOptions>(builder.Configuration.GetSection("EInvoice"));
builder.Services.AddScoped<IEInvoiceService, EInvoiceService>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

builder.Services.Configure<CloudinarySetings>(builder.Configuration.GetSection("Cloudinary"));
//                  DI CLOUDINARY
builder.Services.AddScoped<IUploadIMG, UploadIMG>();
builder.Services.AddScoped<ILightXService, LightXService>();
builder.Services.AddHttpClient<GeminiService>();
//                  DI CLOUDINARY

//                  DI CLOUDINARY DVSP (Dịch Vụ / Sản Phẩm) — cloud riêng, tách khỏi Cloudinary ở trên
builder.Services.Configure<CloudinaryDVSPSettings>(builder.Configuration.GetSection("CloudinaryDVSP"));
builder.Services.AddScoped<IUploadDVSP, UploadDVSP>();
//                  DI CLOUDINARY DVSP

// ===== FIREBASE FIRESTORE SERVICE =====
// Dùng REST API - không cần Service Account JSON, đăng ký Scoped
builder.Services.AddScoped<IFirestoreService, FirestoreService>();
// ===== END FIREBASE FIRESTORE SERVICE =====

// ===== PRODUCT CACHE SERVICE (Redis) =====
builder.Services.AddScoped<IProductCacheService, ProductCacheService>();
// ===== END PRODUCT CACHE SERVICE =====

// ===== SERVICE CACHE SERVICE (Redis) — module Dịch vụ / Combo / Tiêu đề =====
builder.Services.AddScoped<IServiceCacheService, ServiceCacheService>();
// ===== END SERVICE CACHE SERVICE =====
builder.Services.AddHostedService<VoucherExpirationBackgroundService>();
builder.Services.AddHostedService<AttendanceBackgroundService>();
//=== cleanup booking service
builder.Services.AddHostedService<BookingCleanupService>();

var app = builder.Build();

//      Cấu hình CORS để cho phép Vue App truy cập API

app.Use((context, next) =>
{
    context.Response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin-allow-popups");
    return next();
});




// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI();

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