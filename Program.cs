using Microsoft.AspNetCore.HttpOverrides;
using Neo4j.Driver;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using OpenAI.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Integrate Neo4j configuration
var neo4jConfig = builder.Configuration.GetSection("Neo4j");
builder.Services.AddSingleton(x => GraphDatabase.Driver(neo4jConfig["Uri"], AuthTokens.Basic(neo4jConfig["User"], neo4jConfig["Password"])));
builder.Services.AddScoped(x => x.GetService<IDriver>().AsyncSession());

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
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

// Add authorization service
builder.Services.AddAuthorization();

// Add OpenAI Service
builder.Services.AddOpenAIService(options =>
{
    options.ApiKey = builder.Configuration["OpenAIServiceOptions:ApiKey"];
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
