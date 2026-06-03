using Chat_App;
using Chat_App.Repositories;

using Chat_App.Services;
using Chat_App.Services.Implementations;
using Chat_App.Services.Interfaces;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Resend;
var builder = WebApplication.CreateBuilder(args);

// ===================== REDIS =====================

var redisconnect = "definite-dingo-70327.upstash.io:6379,password=gQAAAAAAARK3AAIgcDEyM2ZlZjMyZWFjNTg0MGNkYWZjY2UxZTY2NWFjMTkyZA,ssl=True,abortConnect=False";



var redis = ConnectionMultiplexer.Connect(redisconnect);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisconnect);

//builder.Services.AddDataProtection()
//    .PersistKeysToStackExchangeRedis(redis, "DataProtectionKeys");
// ===================== CLOUDINARY =====================
var cloudinarysection = builder.Configuration.GetSection("CloudinarySection");

var cloudinaryAccount = new Account(
    cloudinarysection["CloudName"],
    cloudinarysection["ApiKey"],
    cloudinarysection["ApiSecret"]
);

var cloudinary = new Cloudinary(cloudinaryAccount)
{
    Api = { Secure = true }
};

builder.Services.AddSingleton(cloudinary);


// ===================== RESEND (FIXED) =====================
// IMPORTANT: Avoid AddResend() version mismatch issues
//builder.Services.AddSingleton<IResend>(_ =>
//    new ResendClient(builder.Configuration["Resend:ApiKey"]));


// ===================== MVC =====================
builder.Services.AddControllersWithViews();


// ===================== DB =====================
builder.Services.AddDbContext<ApplicationDBContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));


// ===================== AUTH =====================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });

builder.Services.AddAuthorization();


// ===================== SESSION =====================
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});


// ===================== HTTP CLIENT =====================
builder.Services.AddHttpClient();


// ===================== SERVICES =====================
builder.Services.AddSingleton<WebPushService>();
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IChatIndexService, ChatIndexService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IFileUploadService, FileUploadService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IBlockService, BlockService>();
builder.Services.AddScoped<IChatSettingsService, ChatSettingsService>();
builder.Services.AddScoped<IPushService, PushService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IMomentService, MomentService>();
builder.Services.AddScoped<MomentCleanupService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IGroupRepository, GroupRepository>();
builder.Services.AddScoped<IGroupMessageRepository, GroupMessageRepository>();
builder.Services.AddScoped<IGroupMessageRecipientRepository, GroupMessageRecipientRepository>();
builder.Services.AddScoped<IHiddenRepository, HiddenRepository>();
builder.Services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
builder.Services.AddScoped<IFriendRequestRepository, FriendRequestRepository>();
builder.Services.AddScoped<IChatBackgroundRepository, ChatBackgroundRepository>();
builder.Services.AddScoped<IMomentRepository, MomentRepository>();

builder.Services.AddScoped<MomentCleanupService>();
var app = builder.Build();


// ===================== PIPELINE =====================
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();


// ===================== ROUTES =====================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");


// ===================== SIGNALR =====================
app.MapHub<ChatHub>("/chatHub");

app.Run();