using System.Text;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Dollars2.Api.Data;
using Dollars2.Api.Json;
using Dollars2.Api.Logging;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;
using Going.Plaid;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

var logFilePath = builder.Configuration["Logging:File:Path"] ?? "logs/dollars2-.log";
var elasticsearchUri = builder.Configuration["Elasticsearch:Uri"];
var minimumLevel = builder.Configuration.GetValue("Serilog:MinimumLevel", LogEventLevel.Information);
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ConfigureDollars2Logging(logFilePath, elasticsearchUri, minimumLevel));

builder.Services.AddControllers()
    .AddJsonOptions(options => Dollars2JsonOptions.Configure(options.JsonSerializerOptions));
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
builder.Services.AddScoped<TransactionRawHistoryService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccountSyncArchiveService>();
builder.Services.AddScoped<SyncLogRepository>();
builder.Services.AddScoped<AccountBalanceRepository>();

// Sync archive store. This is always the dynamodb-local container — in production as well as in
// development — so there is no AWS account, no IAM, and nothing to bill.
var dynamoDbOptions = builder.Configuration.GetSection(DynamoDbOptions.SectionName).Get<DynamoDbOptions>()
    ?? new DynamoDbOptions();
builder.Services.AddSingleton(dynamoDbOptions);
builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
    // dynamodb-local validates neither of these, and -sharedDb makes the region meaningless, but the
    // SDK refuses to sign a request without them. Hardcoded rather than configured on purpose: there
    // is no account behind this endpoint, so there is nothing for a deployer to fill in and no secret
    // to protect. Surfacing them as config would imply otherwise and invite pointing this at real AWS.
    new AmazonDynamoDBClient(
        new BasicAWSCredentials("local", "local"),
        new AmazonDynamoDBConfig
        {
            ServiceURL = dynamoDbOptions.ServiceUrl,
            AuthenticationRegion = "us-east-1",
        }));
// Singleton rather than scoped like the Dapper repositories: it holds no per-request state and both of
// its dependencies are singletons. It is also the one repository that must never touch DbSession — its
// writes are deliberately outside the MSSQL transaction — and the lifetime says so.
builder.Services.AddSingleton<SyncArchiveRepository>();
// Registered ahead of BankSyncHostedService so the archive table is settled before the first sync.
builder.Services.AddHostedService<SyncArchiveTableInitializer>();
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
        if(frontendUrl.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase))
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {   
            policy.WithOrigins(frontendUrl).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Dollars2";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Dollars2";

// AuthService reads this with GetValue<int>, so a missing or unparseable key silently yields 0 and
// every token it mints is already expired. Fail startup instead.
if (builder.Configuration.GetValue<int>("Jwt:ExpirationDays") <= 0)
{
    throw new InvalidOperationException("Jwt:ExpirationDays is not configured.");
}

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
builder.Services.AddExceptionHandler<DollarsExceptionHandler>();

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
