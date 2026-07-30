using Microsoft.Extensions.Logging;
using WeatherApp.Application.Abstractions;
using WeatherApp.Application.DTOs;
using WeatherApp.Domain.Entities;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Application.Services;

public sealed class FavoritosService(
    ICidadeFavoritaRepository favoritos,
    IUsuarioRepository usuarios,
    IUsuarioAtualProvider usuarioAtual,
    IWeatherProvider clima,
    ILogger<FavoritosService> logger)
{
    public async Task<IReadOnlyList<CidadeFavoritaDto>> ListarAsync(CancellationToken ct = default)
    {
        var usuarioId = usuarioAtual.ObterUsuarioId();
        var itens = await favoritos.ListarPorUsuarioAsync(usuarioId, ct);
        return [.. itens.Select(Mapear)];
    }

    public async Task<CidadeFavoritaDto> AdicionarAsync(CriarFavoritoRequest request, CancellationToken ct = default)
    {
        var usuarioId = usuarioAtual.ObterUsuarioId();
        var nome = request.Nome.Trim();

        if (await favoritos.ExisteAsync(usuarioId, nome, ct))
        {
            throw new FavoritoDuplicadoException(nome);
        }

        // Valida a cidade no provedor (404 se não existir) e captura nome canônico + coordenadas.
        var consulta = string.IsNullOrWhiteSpace(request.PaisCodigo) || nome.Contains(',')
            ? nome
            : $"{nome},{request.PaisCodigo.Trim()}";
        var resolvida = await clima.ObterClimaAtualAsync(consulta, ct);

        await usuarios.GarantirAnonimoAsync(usuarioId, ct);

        var favorita = CidadeFavorita.Criar(
            usuarioId, resolvida.Cidade, request.PaisCodigo ?? resolvida.PaisCodigo,
            resolvida.Latitude, resolvida.Longitude);

        // UQ_Usuario_Cidade no banco é o backstop final contra corrida ou nome canônico divergente.
        await favoritos.AdicionarAsync(favorita, ct);

        logger.LogInformation("Favorito {Cidade} criado para {UsuarioId}.", favorita.Nome, usuarioId);
        return Mapear(favorita);
    }

    public async Task RemoverAsync(Guid id, CancellationToken ct = default)
    {
        var usuarioId = usuarioAtual.ObterUsuarioId();
        var favorita = await favoritos.ObterPorIdAsync(id, usuarioId, ct)
            ?? throw new FavoritoNaoEncontradoException(id);

        await favoritos.RemoverAsync(favorita, ct);
    }

    private static CidadeFavoritaDto Mapear(CidadeFavorita c) => new()
    {
        Id = c.Id,
        Nome = c.Nome,
        PaisCodigo = c.PaisCodigo,
        Latitude = c.Latitude,
        Longitude = c.Longitude,
        DataCriacao = c.DataCriacao
    };
}
