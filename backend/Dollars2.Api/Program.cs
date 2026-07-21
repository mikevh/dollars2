using System.Text;
using Dollars2.Api.Data;
using Dollars2.Api.Logging;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;
using Going.Plaid;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logFilePath = builder.Configuration["Logging:File:Path"] ?? "logs/dollars2-.log";
var elasticsearchUri = builder.Configuration["Elasticsearch:Uri"];
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ConfigureDollars2Logging(logFilePath, elasticsearchUri));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
var cs = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(cs) || cs == "<dotnet user secret>")
{
    throw new ApplicationException("ConnectionStrings:DefaultConnection is not initialized");
}

builder.Services.AddScoped(sp => new DbSession(new SqlConnection(cs)));
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<RefreshTokenRepository>();
builder.Services.AddScoped<BudgetRepository>();
builder.Services.AddScoped<BudgetGroupRepository>();
builder.Services.AddScoped<LineItemRepository>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<TransactionRepository>();
builder.Services.AddScoped<TransactionAssignmentRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<SyncLogRepository>();
builder.Services.AddHttpClient("simplefin", client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IBankSyncProvider, SimplefinProvider>();
builder.Services.AddPlaid(builder.Configuration);
builder.Services.AddScoped<IBankSyncProvider, PlaidProvider>();
builder.Services.AddScoped<BankSyncService>();
builder.Services.AddSingleton<SyncLockService>();
builder.Services.AddHostedService<BankSyncHostedService>();

var frontendUrl = builder.Configuration["Cors:FrontendUrl"]
    ?? throw new InvalidOperationException("Cors:FrontendUrl is not configured.");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Dollars2";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Dollars2";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
