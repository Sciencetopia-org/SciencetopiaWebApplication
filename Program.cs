using Microsoft.AspNetCore.HttpOverrides;
using Neo4j.Driver;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Models;
using Sciencetopia.Hubs;
using Sciencetopia.Authorization;
using OpenAI.Extensions;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// 注册编码提供程序
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Add services to the container.
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddTransient<ISmsSender, SmsSender>();
builder.Services.AddScoped<StudyPlanService>();
builder.Services.AddScoped<StudyGroupService>();
builder.Services.AddScoped<LearningService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<KnowledgeGraphService>();

builder.Services.AddScoped<GroupManagerAuthorizeAttribute>(); // Register the custom authorization attribute

// Register the custom IUserIdProvider
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();

// Add SignalR service
builder.Services.AddSignalR();

// // 从 appsettings.json 或环境变量获取 Elasticsearch 配置
// var elasticsearchUrl = builder.Configuration["Elasticsearch:Url"];
// var defaultIndex = builder.Configuration["Elasticsearch:DefaultIndex"];

// // 验证配置
// if (string.IsNullOrEmpty(elasticsearchUrl))
// {
//     throw new Exception("Elasticsearch URL is not configured.");
// }

// var settings = new ElasticsearchClientSettings(new Uri(elasticsearchUrl))
//     .DefaultIndex(defaultIndex);

// // 注册 ElasticsearchClient 到服务容器
// builder.Services.AddSingleton<ElasticsearchClient>(new ElasticsearchClient(settings));

// Integrate Neo4j configuration
var neo4jConfig = builder.Configuration.GetSection("Neo4j");
builder.Services.AddSingleton(x => GraphDatabase.Driver(neo4jConfig["Uri"], AuthTokens.Basic(neo4jConfig["User"], neo4jConfig["Password"])));
builder.Services.AddSingleton(x =>
{
    var configuration = x.GetRequiredService<IConfiguration>();
    var connectionString = configuration["AzureBlobStorage:ConnectionString"];
    return new BlobServiceClient(connectionString);
});

builder.Services.AddScoped(x => x.GetService<IDriver>().AsyncSession());
builder.Services.AddScoped<IUserValidator<ApplicationUser>, CustomUserValidator>();

// // 注册您的 DataSyncService 作为后台服务
// builder.Services.AddHostedService<DataSyncService>();

// Add ASP.NET Core Identity
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 5,               // Maximum number of retries
            maxRetryDelay: TimeSpan.FromSeconds(30), // Maximum delay between retries
            errorNumbersToAdd: null         // SQL error numbers to consider for retry
    ))
);

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Controllers/ Swagger/OpenAPI configurations
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Sciencetopia API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });

    // Remove any global security requirements if present
    // This ensures that security is only applied where explicitly specified
    c.OperationFilter<AuthorizeCheckOperationFilter>(); // Ensure this is added
});


// Setup CORS in .NET Web API
builder.Services.AddCors(options =>
{
    options.AddPolicy("VueCorsPolicy", builder =>
    {
        builder.WithOrigins("http://localhost:8080") // Replace with the URL of your Vue.js app
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
    options.AddPolicy("AdminCorsPolicy", builder =>
    {
        builder.WithOrigins("http://localhost:8848") // Replace with the URL of your desired origin
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Add authentication and authorization
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Add authorization service with role policy
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministratorRole", policy => policy.RequireRole("Administrator"));
});


// Add logging service
builder.Services.AddLogging();

// Add OpenAI Service
builder.Services.AddOpenAIService(options =>
{
    options.ApiKey = builder.Configuration["OpenAIServiceOptions:ApiKey"] ?? string.Empty;
    options.DefaultModelId = OpenAI.ObjectModels.Models.Davinci;
});

// Integrate other services like distributed memory cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// Use CORS policy
app.UseCors("VueCorsPolicy");
app.UseCors("AdminCorsPolicy");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
});
}

// Ensure you create roles before running the application
using var scope = app.Services.CreateScope();
var services = scope.ServiceProvider;
var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
if (!await roleManager.RoleExistsAsync("Administrator"))
{
    await roleManager.CreateAsync(new IdentityRole("Administrator"));
}

app.UseMiddleware<UserActivityMiddleware>();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapHub<ChatHub>("/chathub"); // Map your ChatHub
app.MapHub<NotificationHub>("/notificationhub");

app.Run();
