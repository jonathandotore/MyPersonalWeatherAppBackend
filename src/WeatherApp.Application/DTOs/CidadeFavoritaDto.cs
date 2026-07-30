namespace WeatherApp.Application.DTOs;

public sealed record CidadeFavoritaDto
{
    public required Guid Id { get; init; }
    public required string Nome { get; init; }
    public string? PaisCodigo { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public required DateTime DataCriacao { get; init; }
}

/// <summary>Sem campo UsuarioId de propósito: o dono vem sempre de IUsuarioAtualProvider,
/// nunca do corpo da requisição.</summary>
public sealed record CriarFavoritoRequest
{
    public string Nome { get; init; } = string.Empty;
    public string? PaisCodigo { get; init; }
}
