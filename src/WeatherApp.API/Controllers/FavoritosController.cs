using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;

namespace WeatherApp.API.Controllers;

// Sem [Produces("application/json")]: força o content-type mesmo nas respostas de Problem(),
// sobrepondo o application/problem+json que o ASP.NET Core aplicaria por conta própria.
[ApiController]
[Route("api/favoritos")]
[Authorize]
public sealed class FavoritosController(FavoritosService favoritos) : ControllerBase
{
    /// <summary>Lista os favoritos do usuário autenticado.</summary>
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

        return criado is not null
            ? CreatedAtAction(nameof(Listar), criado)
            : Problem(
                title: "Cidade não encontrada",
                detail: $"Não foi encontrada nenhuma cidade com o nome '{request.Nome}'.",
                statusCode: StatusCodes.Status404NotFound);
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
