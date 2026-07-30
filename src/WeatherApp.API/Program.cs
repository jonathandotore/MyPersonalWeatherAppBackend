using Scalar.AspNetCore;
using WeatherApp.API.ErrorHandling;
using WeatherApp.Application;
using WeatherApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

const string PoliticaCors = "Frontend";

builder.Services.AddControllers();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ProblemDetails para todas as respostas de erro, com traceId para correlacionar com o log.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
});

builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// O frontend Angular manda o header customizado X-Usuario-Id; sem AllowAnyHeader (ou o header
// listado explicitamente) o preflight o rejeita e o erro que chega ao browser é um CORS opaco,
// difícil de diagnosticar.
var origens = builder.Configuration.GetSection("Cors:OrigensPermitidas").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(PoliticaCors, p => p
    .WithOrigins(origens)
    .AllowAnyMethod()
    .AllowAnyHeader()));

builder.Services.AddOpenApi();

var app = builder.Build();

// Registrado SEMPRE, inclusive em Development. Se ficasse atrás de um IsDevelopment(), em dev a
// Developer Exception Page devolveria HTML e o frontend quebraria ao tentar parsear ProblemDetails
// — justamente no ambiente em que o frontend roda.
app.UseExceptionHandler();

// Faz 404 de rota inexistente também sair como ProblemDetails, e não com corpo vazio.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // O .NET 10 gera o documento OpenAPI nativamente, mas não traz UI (os templates deixaram de
    // incluir Swashbuckle desde o .NET 9). Scalar fornece a UI navegável.
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors(PoliticaCors);
app.UseAuthorization();
app.MapControllers();

app.Run();
