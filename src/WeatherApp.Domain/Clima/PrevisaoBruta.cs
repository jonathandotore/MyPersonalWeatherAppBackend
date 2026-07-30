namespace WeatherApp.Domain.Clima;

/// <summary>
/// Previsão como o provedor realmente entrega: uma lista plana de blocos de 3 horas
/// </summary>
public sealed record PrevisaoBruta(string Cidade, string? PaisCodigo, int OffsetSegundos, IReadOnlyList<BlocoPrevisao> Blocos);

/// <summary>Um bloco de 3 horas da previsão.</summary>
/// <param name="InstanteUtc">Instante do bloco, em UTC. O provedor também manda uma string
/// formatada, mas ela está em UTC — usá-la como se fosse hora local desloca todos os dias.</param>
/// <param name="EhDiurno">
/// Se o bloco é diurno. Usado para escolher um ícone representativo: um ícone de chuva noturna
/// ("10n") num card que resume o dia inteiro fica visualmente errado.
/// </param>
public sealed record BlocoPrevisao(
    DateTimeOffset InstanteUtc,
    decimal Temperatura,
    decimal TemperaturaMinima,
    decimal TemperaturaMaxima,
    int Umidade,
    string Condicao,
    string Icone,
    bool EhDiurno,
    decimal ProbabilidadeChuva);
