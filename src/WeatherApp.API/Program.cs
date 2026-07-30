using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using WeatherApp.API.Auth;
using WeatherApp.API.ErrorHandling;
using WeatherApp.API.Filters;
using WeatherApp.API.OpenApi;
using WeatherApp.Application;
using WeatherApp.Application.Abstractions;
using WeatherApp.Infrastructure;
using WeatherApp.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

const string PoliticaCors = "Frontend";

builder.Services.AddScoped<ValidacaoActionFilter>();
builder.Services.AddControllers(o => o.Filters.AddService<ValidacaoActionFilter>());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioAtualProvider, HttpUsuarioAtualProvider>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var jwt = builder.Configuration.GetSection(JwtSettings.SecaoConfiguracao).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção 'Jwt' não configurada.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // "sub" fica literal na claim, sem o remapeamento padrão para ClaimTypes.NameIdentifier
        // — simétrico com o que HttpUsuarioAtualProvider lê e com o que JwtTokenService emite.
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Emissor,
            ValidateAudience = true,
            ValidAudience = jwt.Audiencia,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.Chave)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// ProblemDetails para todas as respostas de erro, com traceId para correlacionar com o log.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
});

builder.Services.AddExceptionHandler<DomainExceptionHandler>();

var origens = builder.Configuration.GetSection("Cors:OrigensPermitidas").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(PoliticaCors, p => p
    .WithOrigins(origens)
    .AllowAnyMethod()
    .AllowAnyHeader()));

builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors(PoliticaCors);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
