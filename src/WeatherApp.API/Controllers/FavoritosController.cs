using Microsoft.AspNetCore.Mvc;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;

namespace WeatherApp.API.Controllers;

[ApiController]
[Route("api/favoritos")]
[Produces("application/json")]
public sealed class FavoritosController(FavoritosService favoritos) : ControllerBase
{
    /// <summary>Lista os favoritos do usuário atual (header X-Usuario-Id).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CidadeFavoritaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CidadeFavoritaDto>>> Listar(CancellationToken ct)
        => Ok(await favoritos.ListarAsync(ct));

    [HttpPost]
    [ProducesResponseType<CidadeFavoritaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CidadeFavoritaDto>> Adicionar(CriarFavoritoRequest request, CancellationToken ct)
    {
        var criado = await favoritos.AdicionarAsync(request, ct);
        return CreatedAtAction(nameof(Listar), criado);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remover(Guid id, CancellationToken ct)
    {
        await favoritos.RemoverAsync(id, ct);
        return NoContent();
    }
}
