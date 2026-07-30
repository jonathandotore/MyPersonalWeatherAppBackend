using Microsoft.AspNetCore.Mvc;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;

namespace WeatherApp.API.Controllers;

/// <summary>Consulta de clima. Endpoints públicos — não exigem autenticação.</summary>
[ApiController]
[Route("api/clima")]
[Produces("application/json")]
public sealed class ClimaController(ClimaService clima) : ControllerBase
{
    /// <summary>Clima atual da cidade: temperatura, condição, ícone, máxima/mínima do dia e umidade.</summary>
    /// <param name="cidade">Nome da cidade. Aceita "Cidade,PaisCodigo" (ex.: "Curitiba,BR") para desambiguar homônimas.</param>
    [HttpGet("{cidade}")]
    [ProducesResponseType<ClimaAtualDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ClimaAtualDto>> ObterClimaAtual(
        string cidade,
        CancellationToken ct)
        => Ok(await clima.ObterClimaAtualAsync(cidade, ct));

    /// <summary>Previsão para os próximos 5 dias, com máxima/mínima e ícone por dia.</summary>
    [HttpGet("{cidade}/previsao")]
    [ProducesResponseType<PrevisaoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PrevisaoDto>> ObterPrevisao(
        string cidade,
        CancellationToken ct)
        => Ok(await clima.ObterPrevisao5DiasAsync(cidade, ct));
}
