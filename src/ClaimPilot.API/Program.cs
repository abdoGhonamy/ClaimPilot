using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

using ClaimPilot.API.Auth;
using ClaimPilot.API.Middleware;
using ClaimPilot.Application;
using ClaimPilot.Application.Interfaces.Documents;
using ClaimPilot.Infrastructure;
using System.Text.Json.Serialization;

using ClaimPilot.Infrastructure.Configuration;
using ClaimPilot.Infrastructure.Data;
using ClaimPilot.Infrastructure.Data.Seed;
using ClaimPilot.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ClaimPilot API",
        Version = "v1",
        Description = "Insurance claims co-pilot with deterministic adjudication, hybrid RAG and a human review queue."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT from POST /api/auth/login as: Bearer <token>"
    });
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc, string.Empty), new List<string>() }
    });

    var xmlDocs = Path.Combine(AppContext.BaseDirectory, "ClaimPilot.API.xml");
    if (File.Exists(xmlDocs))
    {
        options.IncludeXmlComments(xmlDocs);
    }
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// Identity + JWT bearer auth. Roles: Adjuster, Supervisor, Director, Viewer.
// ---------------------------------------------------------------------------
var jwt = builder.Configuration.GetSection("Jwt");
var issuer = jwt["Issuer"] ?? "claimpilot";
var audience = jwt["Audience"] ?? "claimpilot-api";
var signingKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwt["Key"] ?? "ClaimPilotDevSigningKeyChangeMe_0123456789ABCDEF"));

builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<SeedData>();

// ---------------------------------------------------------------------------
// Claim intake storage (outside wwwroot; never serves files to the web).
// ---------------------------------------------------------------------------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddScoped<IStorageService, FileStorageService>();

// ---------------------------------------------------------------------------
// ClaimPilot layers
// ---------------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);

// ---------------------------------------------------------------------------
// Health checks: Postgres + Redis
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=claimpilot;Username=claimpilot;Password=claimpilot";
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "db" })
    .AddRedis(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379", name: "redis");

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin()));

var app = builder.Build();

// Apply migrations and seed demo data on startup (local dev friendly).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<SeedData>();
    await seeder.SeedAsync(CancellationToken.None);

    var demoData = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await demoData.SeedAsync(CancellationToken.None);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "ClaimPilot API v1"));
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
