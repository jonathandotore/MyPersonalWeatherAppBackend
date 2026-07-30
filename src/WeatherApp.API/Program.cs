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

var origens = builder.Configuration.GetSection("Cors:OrigensPermitidas").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(PoliticaCors, p => p
    .WithOrigins(origens)
    .AllowAnyMethod()
    .AllowAnyHeader()));

builder.Services.AddOpenApi();

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
app.UseAuthorization();
app.MapControllers();

app.Run();
