namespace WeatherApp.Application.DTOs;

public sealed record RegistrarRequest
{
    public string Nome { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Senha { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Senha { get; init; } = string.Empty;
}

public sealed record TokenResponse
{
    public required string Token { get; init; }
    public required DateTime ExpiraEmUtc { get; init; }
}
