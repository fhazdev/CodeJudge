using System.Text.Json.Serialization;
using CodeJudge.Api.Auth;
using CodeJudge.Api.Filters;
using CodeJudge.Application;
using CodeJudge.Application.Abstractions;
using CodeJudge.Infrastructure;
using CodeJudge.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;

var builder = WebApplication.CreateBuilder(args);

// Keep the short claim names the v2.0 endpoint actually issues. With mapping on, "oid"
// silently becomes a schemas.microsoft.com URI and every lookup by short name returns null.
JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

// ---------------------------------------------------------------------------
// Authentication
// ---------------------------------------------------------------------------
//
// The multi-tenant detail that decides whether anyone but the author can sign in.
// With "common" as the tenant there is no single issuer to pin: a user from Contoso
// arrives as https://login.microsoftonline.com/<contoso-tid>/v2.0 and a personal account
// as the fixed MSA tenant. Microsoft.Identity.Web's AadIssuerValidator accepts any
// Microsoft issuer whose tenant segment matches the token's own tid claim.
//
// Hand-rolling JwtBearer here is what leads people to ValidateIssuer = false, which is a
// real hole rather than a shortcut.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        // MSAL may present the scope as either the bare client id or the api:// URI
        // depending on how it was requested, and both are legitimately this API.
        var clientId = builder.Configuration["AzureAd:ClientId"];
        options.TokenValidationParameters.ValidAudiences =
            [clientId!, $"api://{clientId}"];
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// CORS
// ---------------------------------------------------------------------------
//
// The SPA is served from a different origin than the API (Static Web Apps vs Container
// Apps), so this is load-bearing rather than a development convenience.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
var connectionString =
    builder.Configuration.GetConnectionString("CodeJudge")
    ?? Environment.GetEnvironmentVariable("CODEJUDGE_CONNECTION")
    ?? DesignTimeDbContextFactory.LocalConnectionString;

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationExceptionFilter>())
    .AddJsonOptions(options =>
    {
        // Enums as names, not integers. The default would put `"difficulty": 0` on the
        // wire, which forces every client to hardcode the ordinal and silently breaks
        // the moment a value is inserted into the middle of the enum.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();

// After authentication, before authorization: it needs a populated principal, and it
// should run even for requests that a policy will later reject, so that a user exists
// the first time they touch anything at all.
app.UseUserProvisioning();

app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>Exposed so WebApplicationFactory can boot this host in integration tests.</summary>
public partial class Program;
