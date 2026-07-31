using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using WeatherApp.Domain.Clima;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;
using WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap.Contracts;

namespace WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap;

/// <summary>
/// Adapter concreto para a OpenWeatherMap (endpoints 2.5 do plano free).
///
/// <para>Responsabilidade estritamente limitada a: montar a URL, desserializar e traduzir para
/// os modelos neutros do domínio. Nenhuma agregação por dia acontece aqui — isso fica no
/// <c>PrevisaoDiariaAggregator</c>, na Application, para poder ser testado sem HTTP.</para>
///
/// <para>Usamos <c>q={cidade}</c> em vez de geocodificar via <c>/geo/1.0/direct</c>: metade das
/// chamadas e um único ponto de tratamento de "não encontrado". A OpenWeatherMap marca a busca
/// por nome como <i>deprecated</i> (funcional, mas sem correções futuras); a mitigação é
/// persistir lat/long dos favoritos, o que permite migrar para consulta por coordenada sem
/// mexer em regra de negócio.</para>
/// </summary>
public sealed class OpenWeatherMapProvider(
    HttpClient http,
    IOptions<OpenWeatherMapSettings> opcoes,
    ILogger<OpenWeatherMapProvider> logger) : IWeatherProvider
{
    private static readonly JsonSerializerOptions JsonOpcoes = new(JsonSerializerDefaults.Web);

    private readonly OpenWeatherMapSettings _cfg = opcoes.Value;

    public async Task<ClimaAtualBruto?> ObterClimaAtualAsync(string cidade, CancellationToken ct = default)
    {
        var payload = await ObterAsync<OwmClimaAtualResponse>(
            "data/2.5/weather", QueryPorNome(cidade), cidade, ct);
        return payload is null ? null : MapearClimaAtual(payload, cidade);
    }

    public async Task<PrevisaoBruta?> ObterPrevisaoAsync(string cidade, CancellationToken ct = default)
    {
        var payload = await ObterAsync<OwmPrevisaoResponse>(
            "data/2.5/forecast", QueryPorNome(cidade), cidade, ct);
        return payload is null ? null : MapearPrevisao(payload, cidade);
    }

    public async Task<ClimaAtualBruto?> ObterClimaAtualPorCoordenadasAsync(
        decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        var rotulo = FormatarCoordenadas(latitude, longitude);
        var payload = await ObterAsync<OwmClimaAtualResponse>(
            "data/2.5/weather", QueryPorCoordenadas(latitude, longitude), rotulo, ct);
        return payload is null ? null : MapearClimaAtual(payload, rotulo);
    }

    public async Task<PrevisaoBruta?> ObterPrevisaoPorCoordenadasAsync(
        decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        var rotulo = FormatarCoordenadas(latitude, longitude);
        var payload = await ObterAsync<OwmPrevisaoResponse>(
            "data/2.5/forecast", QueryPorCoordenadas(latitude, longitude), rotulo, ct);
        return payload is null ? null : MapearPrevisao(payload, rotulo);
    }

    private static ClimaAtualBruto MapearClimaAtual(OwmClimaAtualResponse payload, string rotuloFallback)
    {
        var condicao = payload.Weather?.FirstOrDefault();

        return new ClimaAtualBruto(
            Cidade: payload.Name ?? rotuloFallback,
            PaisCodigo: payload.Sys?.Country,
            Temperatura: payload.Main?.Temp ?? 0m,
            SensacaoTermica: payload.Main?.FeelsLike ?? 0m,
            TemperaturaMinimaInstantanea: payload.Main?.TempMin ?? 0m,
            TemperaturaMaximaInstantanea: payload.Main?.TempMax ?? 0m,
            Umidade: payload.Main?.Humidity ?? 0,
            Condicao: condicao?.Description ?? "indisponível",
            Icone: condicao?.Icon ?? "01d",
            Latitude: payload.Coord?.Lat,
            Longitude: payload.Coord?.Lon,
            OffsetSegundos: payload.Timezone,
            InstanteUtc: DateTimeOffset.FromUnixTimeSeconds(payload.Dt));
    }

    private static PrevisaoBruta MapearPrevisao(OwmPrevisaoResponse payload, string rotuloFallback)
    {
        var blocos = (payload.List ?? [])
            .Select(b =>
            {
                var condicao = b.Weather?.FirstOrDefault();
                return new BlocoPrevisao(
                    InstanteUtc: DateTimeOffset.FromUnixTimeSeconds(b.Dt),
                    Temperatura: b.Main?.Temp ?? 0m,
                    TemperaturaMinima: b.Main?.TempMin ?? 0m,
                    TemperaturaMaxima: b.Main?.TempMax ?? 0m,
                    Umidade: b.Main?.Humidity ?? 0,
                    Condicao: condicao?.Description ?? "indisponível",
                    Icone: condicao?.Icon ?? "01d",
                    EhDiurno: !string.Equals(b.Sys?.Pod, "n", StringComparison.OrdinalIgnoreCase),
                    ProbabilidadeChuva: b.Pop);
            })
            .ToList();

        return new PrevisaoBruta(
            Cidade: payload.City?.Name ?? rotuloFallback,
            PaisCodigo: payload.City?.Country,
            OffsetSegundos: payload.City?.Timezone ?? 0,
            Blocos: blocos);
    }

    private async Task<T?> ObterAsync<T>(string rota, string queryLocalizacao, string rotulo, CancellationToken ct)
        where T : class
    {
        var url = MontarUrl(rota, queryLocalizacao);

        HttpResponseMessage resposta;
        try
        {
            resposta = await http.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                         or TimeoutRejectedException or BrokenCircuitException
                                      && !ct.IsCancellationRequested)
        {
            // Timeout de rede, DNS, conexão recusada (HttpRequestException/TaskCanceledException)
            // e timeout/circuito abertos pelo próprio Polly (TimeoutRejectedException,
            // BrokenCircuitException) — este último NÃO deriva de TaskCanceledException, embrulha
            // um TaskCanceledException por dentro, então precisa de captura própria.
            // A mensagem original nunca vai para o cliente: pode conter a URL com appid.
            logger.LogError(ex, "Falha de rede ao consultar a OpenWeatherMap em {Rota}.", rota);
            throw new ProvedorClimaIndisponivelException(
                "Não foi possível consultar o serviço de clima. Tente novamente em instantes.", ex);
        }

        if (resposta.StatusCode == HttpStatusCode.NotFound)
        {
            // "Cidade não encontrada" é um resultado de negócio esperado (usuário digitou algo
            // que o provedor não reconhece), não uma falha do sistema — retorna null em vez de
            // lançar, para que o 404 flua como um caminho normal do código até o controller,
            // em vez de por cima de uma exceção.
            logger.LogDebug("OpenWeatherMap não encontrou '{Rotulo}' em {Rota}.", rotulo, rota);
            return null;
        }

        if (resposta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError(
                "OpenWeatherMap recusou a credencial ({Status}) em {Rota}. Verifique OpenWeatherMap:ApiKey.",
                (int)resposta.StatusCode, 
                rota);

            throw new FalhaIntegracaoProvedorException("A integração com o serviço de clima está mal configurada.");
        }

        if (!resposta.IsSuccessStatusCode)
        {
            logger.LogError(
                "OpenWeatherMap respondeu {Status} em {Rota}.", 
                (int)resposta.StatusCode, 
                rota);
            
            throw new ProvedorClimaIndisponivelException("O serviço de clima está instável no momento. Tente novamente em instantes.");
        }

        var payload = await resposta.Content.ReadFromJsonSafeAsync<T>(JsonOpcoes, ct);

        return payload ?? throw new ProvedorClimaIndisponivelException(
            "O serviço de clima devolveu uma resposta vazia.");
    }

    private string MontarUrl(string rota, string queryLocalizacao) => string.Create(CultureInfo.InvariantCulture,
        $"{rota}?{queryLocalizacao}&units={_cfg.Unidades}&lang={_cfg.Idioma}&appid={_cfg.ApiKey}");

    private static string QueryPorNome(string cidade) => $"q={Uri.EscapeDataString(cidade.Trim())}";

    private static string QueryPorCoordenadas(decimal latitude, decimal longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"lat={latitude}&lon={longitude}");

    // Coordenadas cruas na mensagem de log/fallback — nunca a URL completa, que carrega o appid.
    private static string FormatarCoordenadas(decimal latitude, decimal longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"{latitude},{longitude}");
}

internal static class HttpContentJsonExtensions
{
    /// <summary>
    /// Desserializa traduzindo JSON malformado em erro de provedor, em vez de deixar uma
    /// <see cref="JsonException"/> crua escapar como 500.
    /// </summary>
    internal static async Task<T?> ReadFromJsonSafeAsync<T>(this HttpContent content, JsonSerializerOptions opcoes, CancellationToken ct)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, opcoes, ct);
        }
        catch (JsonException ex)
        {
            throw new ProvedorClimaIndisponivelException(
                "O serviço de clima devolveu uma resposta em formato inesperado.", ex);
        }
    }
}
