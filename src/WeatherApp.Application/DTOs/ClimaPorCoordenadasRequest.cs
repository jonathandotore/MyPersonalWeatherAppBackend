namespace WeatherApp.Application.DTOs;

/// <summary>Contrato de <c>GET /api/clima/coordenadas</c> e <c>GET /api/clima/coordenadas/previsao</c>.
/// Nulável de propósito para que a ausência do parâmetro produza uma mensagem de validação clara
/// ("é obrigatória") em vez de cair silenciosamente em (0,0) — a "null island" no Atlântico.</summary>
public sealed record ClimaPorCoordenadasRequest
{
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
}
