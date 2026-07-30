using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeatherApp.Domain.Clima;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap;

/// <summary>Decorator de cache sobre o provider real. TTL curto por design: o clima muda e o
/// free tier da OpenWeatherMap tem rate limit agressivo (60 req/min) — a segunda chamada que
/// ClimaService faz para derivar a máxima/mínima do dia (ver ClimaService) sai praticamente de
/// graça porque cai na mesma entrada da tela de previsão.
///
/// Registrado como Scoped, nunca Singleton: capturaria para sempre o HttpClient transient do
/// provider real (captive dependency). O estado do cache não se perde — mora no IMemoryCache,
/// que é singleton.</summary>
public sealed class CachedWeatherProvider(
    OpenWeatherMapProvider inner,
    IMemoryCache cache,
    IOptions<WeatherCacheSettings> opcoes,
    ILogger<CachedWeatherProvider> logger) : IWeatherProvider
{
    private readonly WeatherCacheSettings _cfg = opcoes.Value;

    public Task<ClimaAtualBruto> ObterClimaAtualAsync(string cidade, CancellationToken ct = default) =>
        ObterOuCriarAsync(
            ChaveCache.DeClimaAtual(cidade),
            TimeSpan.FromMinutes(_cfg.TtlClimaAtualMinutos),
            () => inner.ObterClimaAtualAsync(cidade, ct));

    public Task<PrevisaoBruta> ObterPrevisaoAsync(string cidade, CancellationToken ct = default) =>
        ObterOuCriarAsync(
            ChaveCache.DePrevisao(cidade),
            TimeSpan.FromMinutes(_cfg.TtlPrevisaoMinutos),
            () => inner.ObterPrevisaoAsync(cidade, ct));

    private async Task<T> ObterOuCriarAsync<T>(string chave, TimeSpan ttl, Func<Task<T>> buscar)
    {
        if (cache.TryGetValue(chave, out T? valor) && valor is not null)
        {
            logger.LogDebug("Cache hit: {Chave}", chave);
            return valor;
        }

        logger.LogDebug("Cache miss: {Chave}", chave);
        var resultado = await buscar();

        // AbsoluteExpirationRelativeToNow, nunca SlidingExpiration: com sliding, uma cidade
        // consultada com frequência nunca expiraria e serviria dado arbitrariamente velho.
        cache.Set(chave, resultado, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });

        return resultado;
    }
}
