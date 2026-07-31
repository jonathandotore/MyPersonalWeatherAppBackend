using Microsoft.AspNetCore.Mvc;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;

namespace WeatherApp.API.Controllers;

/// <summary>Consulta de clima. Endpoints públicos — não exigem autenticação.
/// Sem <c>[Produces("application/json")]</c> de propósito: essa anotação força o content-type
/// mesmo nas respostas de <c>Problem()</c>, sobrepondo o <c>application/problem+json</c>
/// que o próprio ASP.NET Core aplicaria — sem ela, a negociação de conteúdo funciona como
/// esperado nos dois casos.</summary>
[ApiController]
[Route("api/clima")]
public sealed class ClimaController(ClimaService clima) : ControllerBase
{
    /// <summary>Clima atual da cidade: temperatura, condição, ícone, máxima/mínima do dia e umidade.</summary>
    /// <param name="cidade">Nome da cidade. Aceita "Cidade,PaisCodigo" (ex.: "Curitiba,BR") para desambiguar homônimas.</param>
    [HttpGet("{cidade}")]
    [ProducesResponseType<ClimaAtualDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ClimaAtualDto>> ObterClimaAtual(string cidade, CancellationToken ct)
    {
        var resultado = await clima.ObterClimaAtualAsync(cidade, ct);
        return resultado is not null ? Ok(resultado) : CidadeNaoEncontrada(cidade);
    }

    /// <summary>Previsão para os próximos 5 dias, com máxima/mínima e ícone por dia.</summary>
    [HttpGet("{cidade}/previsao")]
    [ProducesResponseType<PrevisaoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PrevisaoDto>> ObterPrevisao(string cidade, CancellationToken ct)
    {
        var resultado = await clima.ObterPrevisao5DiasAsync(cidade, ct);
        return resultado is not null ? Ok(resultado) : CidadeNaoEncontrada(cidade);
    }

    /// <summary>Mesmo título/mensagem que a antiga <c>CidadeNaoEncontradaException</c> produzia —
    /// o contrato de erro na rede não muda, só deixou de ser sinalizado por exceção.</summary>
    private ObjectResult CidadeNaoEncontrada(string cidade) => Problem(
        title: "Cidade não encontrada",
        detail: $"Não foi encontrada nenhuma cidade com o nome '{cidade}'.",
        statusCode: StatusCodes.Status404NotFound);
}
