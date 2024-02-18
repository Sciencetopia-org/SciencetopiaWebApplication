using Microsoft.AspNetCore.HttpOverrides;
using Neo4j.Driver;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Models;
using OpenAI.Extensions;
using System.Text;
using Azure.Storage.Blobs;

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
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
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
});

// Add authentication and authorization
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Add authorization service
builder.Services.AddAuthorization();

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

app.UseMiddleware<UserActivityMiddleware>();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
