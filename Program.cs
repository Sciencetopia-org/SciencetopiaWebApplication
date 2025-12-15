using Microsoft.AspNetCore.HttpOverrides;
using Neo4j.Driver;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Services.KnowledgeGraph;
using Sciencetopia.Services.Messaging;
using Sciencetopia.Models;
using Sciencetopia.Hubs;
using Sciencetopia.Authorization;
using OpenAI.Extensions;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
// Modular backend moved into its own project

var builder = WebApplication.CreateBuilder(args);

// 注册编码提供程序
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Add services to the container.
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddTransient<ISmsSender, SmsSender>();
builder.Services.AddScoped<StudyPlanService>(sp =>
    new StudyPlanService(
        sp.GetRequiredService<IDriver>(),
        sp.GetRequiredService<ILogger<StudyPlanService>>(),
        sp.GetRequiredService<IStudyPlanRepository>(),
        sp.GetRequiredService<ITagRepository>(),
        sp.GetRequiredService<ITagResolutionService>()));
builder.Services.AddScoped<StudyGroupService>();
builder.Services.AddScoped<LearningService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<KnowledgeGraphService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<EmailTemplateService>();
builder.Services.AddScoped<UserActivityService>();
builder.Services.AddScoped<DailySummaryService>();
builder.Services.AddScoped<SearchService>();

builder.Services.AddScoped<GroupManagerAuthorizeAttribute>(); // Register the custom authorization attribute

// Register the custom IUserIdProvider
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();

builder.Services.AddScoped<IGraphRepository, GraphRepository>();
builder.Services.AddScoped<IKnowledgeNodeRepository, KnowledgeNodeRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();
builder.Services.AddScoped<ITagResolutionService, TagResolutionService>();
builder.Services.AddScoped<IResourceRepository, ResourceRepository>();
builder.Services.AddSingleton<Sciencetopia.Services.Region.IRegionService, Sciencetopia.Services.Region.RegionService>();
builder.Services.AddSingleton<IDraftFreezeService, DraftFreezeService>();
builder.Services.AddScoped(x => x.GetService<IDriver>().AsyncSession());
builder.Services.AddScoped<IUserValidator<ApplicationUser>, CustomUserValidator>();
builder.Services.AddScoped<IStudyPlanRepository, StudyPlanRepository>();
builder.Services.AddScoped<Sciencetopia.Services.PlanSharingService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<Sciencetopia.Services.PermissionService>();
builder.Services.AddScoped<Sciencetopia.Services.Cohorts.ICohortService, Sciencetopia.Services.Cohorts.CohortService>();
builder.Services.AddScoped<IVersioningService, VersioningService>();
builder.Services.AddScoped<IKnowledgeGraphWorkflowService, KnowledgeGraphWorkflowService>();
builder.Services.AddScoped<IGraphSyncService, GraphSyncService>();
builder.Services.AddScoped<StudyPlanVersioningService>();
// L10n services
builder.Services.Configure<Sciencetopia.Services.L10n.L10nOptions>(builder.Configuration.GetSection("L10n"));
builder.Services.AddScoped<Sciencetopia.Services.L10n.IL10nService, Sciencetopia.Services.L10n.L10nService>();
builder.Services.AddScoped<Sciencetopia.Services.L10n.L10nMigrationRunner>();
builder.Services.AddSingleton<Sciencetopia.Middleware.ILanguageContext, Sciencetopia.Middleware.LanguageContext>();
builder.Services.Configure<DraftFreezeOptions>(builder.Configuration.GetSection("KnowledgeGraph:DraftFreeze"));
// Progress tracking services
builder.Services.AddScoped<Sciencetopia.Repositories.Neo4j.INeo4jProgressRepository, Sciencetopia.Repositories.Neo4j.Neo4jProgressRepository>();
builder.Services.AddScoped<Sciencetopia.Services.Progress.IResourceProgressService, Sciencetopia.Services.Progress.ResourceProgressService>();

// Add SignalR service
builder.Services.AddSignalR();

// Register the hosted service
builder.Services.AddHostedService<DailySummaryHostedService>();

// Configure JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
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
});

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
builder.Services.AddSingleton<MessageAttachmentService>();

// // 注册您的 DataSyncService 作为后台服务
// builder.Services.AddHostedService<DataSyncService>();

// Add ASP.NET Core Identity
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 50,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: new int[] { 40925, 40613, 40197, 40501, 10928, 10929, 10054, 233, 64, 20 }
        )
    )
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
    // Avoid schemaId collisions for nested types with same simple name
    c.CustomSchemaIds(type =>
    {
        var full = type.FullName;
        return string.IsNullOrEmpty(full) ? type.Name : full.Replace('+', '.');
    });
    // In case two actions map to the same route/method, pick the first
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

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
    c.OperationFilter<AuthorizeCheckOperationFilter>();
    c.OperationFilter<SciencetopiaWebApplication.Filters.LangParameterOperationFilter>();
});


// Setup CORS in .NET Web API
builder.Services.AddCors(options =>
{
    options.AddPolicy("VueCorsPolicy", builder =>
    {
        builder.WithOrigins("http://localhost:8088", "http://localhost:8848")  // Allow both origins
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials();
    });
});


// Add authentication and authorization
builder.Services.ConfigureApplicationCookie(options =>
{
    // These MVC paths are not used by our API controllers; avoid redirects.
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";

    // For API requests, return proper status codes instead of redirecting
    // to non-existent MVC pages which caused 404 responses.
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});

// Add authorization service with role policy
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministratorRole", policy => policy.RequireRole("administrator"));

    // Plan/Cohort granular policies
    options.AddPolicy("Plan.Edit", policy =>
        policy.Requirements.Add(new Sciencetopia.Authorization.PlanPermissionRequirement(Sciencetopia.Authorization.PlanPermissionAction.PlanEdit)));
    options.AddPolicy("Plan.Publish", policy =>
        policy.Requirements.Add(new Sciencetopia.Authorization.PlanPermissionRequirement(Sciencetopia.Authorization.PlanPermissionAction.PlanPublish)));
    options.AddPolicy("Cohort.Manage", policy =>
        policy.Requirements.Add(new Sciencetopia.Authorization.PlanPermissionRequirement(Sciencetopia.Authorization.PlanPermissionAction.CohortManage)));
    options.AddPolicy("Cohort.Invite", policy =>
        policy.Requirements.Add(new Sciencetopia.Authorization.PlanPermissionRequirement(Sciencetopia.Authorization.PlanPermissionAction.CohortInvite)));
});


// Add logging service
builder.Services.AddLogging();

// Modular backend is no longer discovered in this app

// Add OpenAI Service
builder.Services.AddOpenAIService(options =>
{
    options.ApiKey = builder.Configuration["OpenAIServiceOptions:ApiKey"] ?? string.Empty;
    options.DefaultModelId = OpenAI.ObjectModels.Models.Davinci;
});

// Integrate other services like distributed memory cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Sciencetopia.Authorization.PlanPermissionHandler>();
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

// Apply CORS dynamically based on request path or origin
app.UseCors("VueCorsPolicy");
app.UseMiddleware<Sciencetopia.Middleware.LanguageResolutionMiddleware>();

app.Use(async (context, next) =>
{
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization");
         context.Response.Headers.Append("Access-Control-Allow-Origin", "http://localhost:8088");
        context.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
        context.Response.StatusCode = 204; // No Content
        return;
    }
    await next();
});

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
try
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roleManager.RoleExistsAsync("administrator"))
    {
        await roleManager.CreateAsync(new IdentityRole("administrator"));
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogError(ex, "Failed to seed initial roles. Continuing startup. Verify database connectivity and state.");
}

app.UseMiddleware<UserActivityMiddleware>();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapHub<ChatHub>("/chathub"); // Map your ChatHub
app.MapHub<Sciencetopia.Hubs.StudyHub>("/hubs/study");

app.Run();
