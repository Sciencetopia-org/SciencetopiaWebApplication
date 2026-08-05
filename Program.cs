using Microsoft.AspNetCore.HttpOverrides;
using Neo4j.Driver;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Services.KnowledgeGraph;
using Sciencetopia.Services.Messaging;
using Sciencetopia.Services.ContentSafety;
using Sciencetopia.Models;
using Sciencetopia.Hubs;
using Sciencetopia.Authorization;
using OpenAI.Extensions;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IO.Compression;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.RateLimiting;
// Modular backend moved into its own project

var builder = WebApplication.CreateBuilder(args);
var enableOptionalStartupTasks =
    builder.Configuration.GetValue<bool?>("StartupTasks:EnableOptionalTasksOnStartup")
    ?? !builder.Environment.IsDevelopment();

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
        sp.GetRequiredService<Sciencetopia.Services.Plans.IPersonalPlanEnrollmentService>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<ITagRepository>(),
        sp.GetRequiredService<ITagResolutionService>()));
builder.Services.AddScoped<StudyGroupService>();
builder.Services.AddScoped<Sciencetopia.Services.StudyGroupDiscovery.IStudyGroupDiscoveryService, Sciencetopia.Services.StudyGroupDiscovery.StudyGroupDiscoveryService>();
builder.Services.AddScoped<Sciencetopia.Services.StudyGroupDiscovery.Vectors.IEmbeddingService, Sciencetopia.Services.StudyGroupDiscovery.Vectors.NoopEmbeddingService>();
builder.Services.AddScoped<Sciencetopia.Services.StudyGroupDiscovery.Vectors.IVectorSearchService, Sciencetopia.Services.StudyGroupDiscovery.Vectors.NoopVectorSearchService>();
builder.Services.AddScoped<LearningService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<KnowledgeGraphService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<EmailTemplateService>();
builder.Services.AddScoped<UserActivityService>();
builder.Services.AddScoped<DailySummaryService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<Sciencetopia.Services.SearchEngine.SearchVectorService>();

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
builder.Services.AddScoped<Sciencetopia.Services.Plans.IPersonalPlanEnrollmentService, Sciencetopia.Services.Plans.PersonalPlanEnrollmentService>();
builder.Services.AddScoped<IStudyPlanRepository, StudyPlanRepository>();
builder.Services.AddScoped<Sciencetopia.Services.PlanSharingService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("PythonService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("PythonService:TimeoutSeconds") ?? 30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SciencetopiaBackend/1.0");
});
builder.Services.AddHttpClient("LinkPreview", client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("LinkPreview:TimeoutSeconds") ?? 5);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("GeneralApi", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetRateLimitPartitionKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Environment.IsDevelopment() ? 600 : 300,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 20
        }));

    options.AddPolicy("Authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetRateLimitPartitionKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Environment.IsDevelopment() ? 30 : 10,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 5
        }));

    options.AddPolicy("ExpensiveOperations", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetRateLimitPartitionKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Environment.IsDevelopment() ? 20 : 5,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2
        }));

    options.AddPolicy("AiOperations", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetRateLimitPartitionKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Environment.IsDevelopment() ? 30 : 10,
            Window = TimeSpan.FromMinutes(10),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2
        }));

    options.AddPolicy("LinkPreview", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetRateLimitPartitionKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Environment.IsDevelopment() ? 120 : 30,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 5
        }));
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.AddScoped<Sciencetopia.Services.PermissionService>();
builder.Services.AddScoped<Sciencetopia.Services.Cohorts.ICohortService, Sciencetopia.Services.Cohorts.CohortService>();
builder.Services.AddScoped<IVersioningService, VersioningService>();
builder.Services.AddScoped<IKnowledgeGraphWorkflowService, KnowledgeGraphWorkflowService>();
builder.Services.AddScoped<IGraphSyncService, GraphSyncService>();
builder.Services.AddScoped<StudyPlanVersioningService>();
// L10n services
builder.Services.Configure<Sciencetopia.Services.L10n.L10nOptions>(builder.Configuration.GetSection("L10n"));
// Ontology V2 feature flags (Phase 1: bound from config, all default false; nothing reads them yet).
builder.Services.Configure<Sciencetopia.Services.Ontology.OntologyOptions>(builder.Configuration.GetSection("Ontology"));
builder.Services.AddScoped<Sciencetopia.Services.Ontology.Phase3.OntologyPhase3DryRunService>();
builder.Services.AddScoped<Sciencetopia.Services.Ontology.TagRepair.MalformedTagRepairService>();
builder.Services.AddScoped<Sciencetopia.Services.L10n.IL10nService, Sciencetopia.Services.L10n.L10nService>();
builder.Services.AddSingleton<Sciencetopia.Middleware.ILanguageContext, Sciencetopia.Middleware.LanguageContext>();
builder.Services.Configure<DraftFreezeOptions>(builder.Configuration.GetSection("KnowledgeGraph:DraftFreeze"));
// Progress tracking services
builder.Services.AddScoped<Sciencetopia.Repositories.Neo4j.INeo4jProgressRepository, Sciencetopia.Repositories.Neo4j.Neo4jProgressRepository>();
builder.Services.AddScoped<Sciencetopia.Services.Progress.IResourceProgressService, Sciencetopia.Services.Progress.ResourceProgressService>();

// Add SignalR service
builder.Services.AddSignalR();

// Register the hosted services that warm external dependencies only when enabled.
if (enableOptionalStartupTasks)
{
    builder.Services.AddHostedService<DailySummaryHostedService>();
    builder.Services.AddHostedService<Sciencetopia.Services.KnowledgeGraph.KnowledgeGraphWarmupHostedService>();
}

builder.Services.AddHostedService<Sciencetopia.Services.KnowledgeGraph.Neo4jSchemaHostedService>();

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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured."))),
        ClockSkew = TimeSpan.FromMinutes(2)
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
builder.Services.Configure<ContentModerationOptions>(builder.Configuration.GetSection("ContentModeration"));
builder.Services.AddScoped<IContentModerationService, ContentModerationService>();

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
    options.AddPolicy("VueCorsPolicy", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        var allowedOrigins = configuredOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .ToArray();

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins("http://localhost:8088", "http://localhost:8848")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
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

var malformedTagRepairPlanMode = args.Contains("--ontology-repair-malformed-tags-plan", StringComparer.OrdinalIgnoreCase);
var malformedTagRepairApplyMode = args.Contains("--ontology-repair-malformed-tags-apply", StringComparer.OrdinalIgnoreCase);
if (malformedTagRepairPlanMode || malformedTagRepairApplyMode)
{
    if (malformedTagRepairPlanMode && malformedTagRepairApplyMode)
    {
        Console.Error.WriteLine("Choose exactly one malformed-tag repair mode: plan or apply.");
        Environment.ExitCode = 2;
        return;
    }

    var preflight = Sciencetopia.Services.Ontology.TagRepair.MalformedTagRepairCli.ValidateConfiguration(app.Configuration);
    if (!preflight.IsValid)
    {
        Console.Error.WriteLine("Malformed-tag repair configuration error:");
        foreach (var error in preflight.Errors) Console.Error.WriteLine($"- {error}");
        Console.Error.WriteLine("No SQL or Neo4j writes were attempted. The repair service was not resolved.");
        Environment.ExitCode = 2;
        return;
    }

    var outputRoot = GetArgValue(args, "--output-root")
        ?? Path.Combine(app.Environment.ContentRootPath, "..", "artifacts");
    outputRoot = Path.GetFullPath(outputRoot);

    if (malformedTagRepairApplyMode)
    {
        var planFile = GetArgValue(args, "--plan-file");
        var suppliedHash = GetArgValue(args, "--plan-hash") ?? string.Empty;
        var confirmed = args.Contains("--confirm-neo4j-write", StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(planFile))
        {
            Console.Error.WriteLine("Apply requires --plan-file <path>.");
            Console.Error.WriteLine("No SQL or Neo4j writes were attempted. The repair service was not resolved.");
            Environment.ExitCode = 2;
            return;
        }

        planFile = Path.GetFullPath(planFile);
        Sciencetopia.Services.Ontology.TagRepair.MalformedTagRepairPlan plan;
        try
        {
            plan = Sciencetopia.Services.Ontology.TagRepair.MalformedTagRepairArtifacts.ReadPlan(planFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unable to read repair plan: {ex.Message}");
            Console.Error.WriteLine("No SQL or Neo4j writes were attempted. The repair service was not resolved.");
            Environment.ExitCode = 2;
            return;
        }

        var request = new Sciencetopia.Services.Ontology.TagRepair.TagRepairApplyRequest(planFile, suppliedHash, confirmed);
        var applyGuard = Sciencetopia.Services.Ontology.TagRepair.MalformedTagRepairCli.ValidateApplyRequest(request, plan);
        if (!applyGuard.IsValid)
        {
            Console.Error.WriteLine("Malformed-tag repair apply was blocked:");
            foreach (var error in applyGuard.Errors) Console.Error.WriteLine($"- {error}");
            Console.Error.WriteLine("No SQL or Neo4j writes were attempted. The repair service was not resolved.");
            Environment.ExitCode = 2;
            return;
        }

        using var applyScope = app.Services.CreateScope();
        var applyService = applyScope.ServiceProvider.GetRequiredService<Sciencetopia.Services.Ontology.TagRepair.MalformedTagRepairService>();
        var outcome = await applyService.ApplyAsync(request);
        Console.WriteLine(outcome.Succeeded ? "Malformed-tag repair applied and verified." : "Malformed-tag repair failed closed; the Neo4j transaction was rolled back.");
        Console.WriteLine($"Result: {outcome.ResultPath}");
        Environment.ExitCode = outcome.ExitCode;
        return;
    }

    using var planScope = app.Services.CreateScope();
    var planService = planScope.ServiceProvider.GetRequiredService<Sciencetopia.Services.Ontology.TagRepair.MalformedTagRepairService>();
    var (repairPlan, repairOutputDirectory) = await planService.PlanAsync(outputRoot);
    Console.WriteLine("Malformed-tag repair plan complete. No database writes were performed.");
    Console.WriteLine($"Output: {repairOutputDirectory}");
    Console.WriteLine($"Plan hash: {repairPlan.PlanHash}");
    Console.WriteLine($"Records: {repairPlan.Summary.PlannedRecordCount}; blocking errors: {repairPlan.Summary.BlockingValidationErrorCount}");
    return;
}

if (args.Contains("--ontology-phase3-dry-run", StringComparer.OrdinalIgnoreCase))
{
    var preflight = Sciencetopia.Services.Ontology.Phase3.Phase3CliPreflight.ValidateNeo4jConfiguration(app.Configuration);
    if (!preflight.IsValid)
    {
        Console.Error.WriteLine("Ontology Phase 3 dry-run configuration error.");
        Console.Error.WriteLine("Neo4j configuration is required before the dry-run service can be resolved:");
        foreach (var error in preflight.Errors)
        {
            Console.Error.WriteLine($"- {error}");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("PowerShell setup example (placeholders only):");
        Console.Error.WriteLine("$env:Neo4j__Uri = \"bolt://localhost:7687\"");
        Console.Error.WriteLine("$env:Neo4j__User = \"neo4j\"");
        Console.Error.WriteLine("$env:Neo4j__Password = \"<password>\"");
        Console.Error.WriteLine();
        Console.Error.WriteLine("No SQL or Neo4j writes were attempted. The dry-run service was not resolved.");
        Environment.ExitCode = 2;
        return;
    }

    var outputRoot = GetArgValue(args, "--output-root")
        ?? Path.Combine(app.Environment.ContentRootPath, "..", "artifacts");
    outputRoot = Path.GetFullPath(outputRoot);

    using var scope = app.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<Sciencetopia.Services.Ontology.Phase3.OntologyPhase3DryRunService>();
    var (summary, outputDir) = await runner.RunAsync(outputRoot);

    Console.WriteLine("Ontology Phase 3 dry-run complete.");
    Console.WriteLine($"Output: {outputDir}");
    Console.WriteLine($"SQL tags: {summary.SqlTagsTotal}");
    Console.WriteLine($"Neo4j tags: {summary.Neo4jTagsTotal}");
    Console.WriteLine($"Quarantine records: {summary.QuarantineCount}");
    Console.WriteLine("No SQL writes, Neo4j writes, migrations, API routes, or frontend changes were performed by this command.");
    return;
}

// Apply CORS dynamically based on request path or origin
app.UseCors("VueCorsPolicy");
if (!app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}
app.UseMiddleware<Sciencetopia.Middleware.LanguageResolutionMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    var csp = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; " +
              "script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https:; " +
              "font-src 'self' data:; connect-src 'self' ws: wss:; upgrade-insecure-requests";
    if (app.Environment.IsDevelopment())
    {
        context.Response.Headers.TryAdd("Content-Security-Policy-Report-Only", csp);
    }
    else
    {
        context.Response.Headers.TryAdd("Content-Security-Policy", csp);
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

// Ensure you create roles before running the application when optional startup tasks are enabled.
if (enableOptionalStartupTasks)
{
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
}
else
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation("Optional startup tasks are disabled. Skipping startup role seeding and warmup tasks for this environment.");
}

app.UseMiddleware<UserActivityMiddleware>();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers().RequireRateLimiting("GeneralApi");

app.MapHub<ChatHub>("/chathub").RequireRateLimiting("GeneralApi"); // Map your ChatHub
app.MapHub<Sciencetopia.Hubs.StudyHub>("/hubs/study").RequireRateLimiting("GeneralApi");

app.Run();

static string GetRateLimitPartitionKey(HttpContext context)
{
    var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}";
    }

    return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

static string? GetArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return i + 1 < args.Length ? args[i + 1] : null;
    }

    return null;
}
