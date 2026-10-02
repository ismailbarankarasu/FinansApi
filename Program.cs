using System.Text;
using FinansApi.Auth;
using FinansApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

if (args is ["--backup-db", var backupSource, var backupTarget])
{
    DatabaseUpgrade.Backup(backupSource, backupTarget);
    return;
}

if (args is ["--import-legacy", var importSource, var importTarget])
{
    await DatabaseUpgrade.Import(importSource, importTarget);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(options => options.Filters.Add<FinansApi.Infrastructure.Errors.ProblemResultFilter>()).ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(context.ModelState)
        {
            Status = 400,
            Title = "Gönderilen alanları kontrol edin."
        };
        problem.Extensions["message"] = problem.Title;
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problem);
    });
builder.Services.AddProblemDetails();
FinansApi.Infrastructure.Pdf.AccountingPdfService.Configure();
builder.Services.AddSingleton<FinansApi.Infrastructure.Pdf.AccountingPdfService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<FinansApi.Services.Accounting.InventoryOpeningService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<FinansApi.Infrastructure.Email.SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<FinansApi.Infrastructure.Email.IEmailSender, FinansApi.Infrastructure.Email.SmtpEmailSender>();
builder.Services.AddScoped<FinansApi.BackgroundJobs.AutomationService>();
builder.Services.AddHostedService<FinansApi.BackgroundJobs.OutboxDispatcher>();
builder.Services.AddScoped<FinansApi.Services.Accounting.LegacyScope>();
builder.Services.AddScoped<FinansApi.Services.Accounting.AccountingContext>();
builder.Services.AddScoped<FinansApi.Services.Accounting.CompanyService>();
builder.Services.AddScoped<FinansApi.Services.Accounting.MasterDataService>();
builder.Services.AddScoped<FinansApi.Services.Accounting.JournalService>();
builder.Services.AddScoped<FinansApi.Services.Accounting.InvoiceService>();
builder.Services.AddScoped<FinansApi.Services.Accounting.PaymentService>();
builder.Services.AddScoped<FinansApi.Services.Accounting.ReportService>();
builder.Services.AddScoped<FinansApi.Services.Accounting.ReconciliationService>();
builder.Services.AddScoped<FinansApi.Services.Accounting.ClosingService>();
builder.Services.AddExceptionHandler<FinansApi.Infrastructure.Errors.ApiExceptionHandler>();
builder.Services.AddScoped<FinansApi.Services.Categories.CategoryService>();
builder.Services.AddScoped<FinansApi.Services.Transactions.TransactionService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Finans API",
                    Version = "v1",
                    Description = "Şirket ve dönem bazlı ön muhasebe, stok, fatura, ödeme ve raporlama API"

                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes.Add(
                    "Bearer",
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        In = ParameterLocation.Header,
                        Description = "Örnek: Bearer {token}"
                    });
                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
                return Task.CompletedTask;
            });
    });
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
jwtSettings.Validate();
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))

        };
    });
builder.Services.AddAuthorization();
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=data/finans-accounting.db";
var dbFile = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
var dbDir = Path.GetDirectoryName(dbFile);
if (!string.IsNullOrWhiteSpace(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy => policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["http://localhost:5173", "http://127.0.0.1:5173"]).AllowAnyHeader().AllowAnyMethod().SetPreflightMaxAge(TimeSpan.FromHours(1)));
    });
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseUpgrade.Initialize(database, builder.Configuration.GetValue<bool>("Seed:Demo"));
}

app.UseCors();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("Finans API").AddPreferredSecuritySchemes("Bearer").AddHttpAuthentication("Bearer", auth =>
        {
        }));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.Run();
public partial class Program
{
}
