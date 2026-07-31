using WeatherApp.Domain.Clima;

namespace WeatherApp.Domain.Interfaces;

/// <summary>
/// Porta para o provedor externo de clima (padrão Adapter). Os dois métodos mapeiam 1:1 os
/// endpoints que o provedor realmente expõe, sem regra de negócio embutida — a composição
/// (inclusive derivar a máxima/mínima do dia) fica na Application.
///
/// <para>O enunciado permite OpenWeatherMap <i>ou</i> WeatherAPI; esta interface é o que
/// impede a escolha de vazar para o resto do sistema. É também o ponto de extensão onde o
/// <c>CachedWeatherProvider</c> se encaixa como Decorator.</para>
/// </summary>
public interface IWeatherProvider
{
    /// <summary>Devolve <c>null</c> quando o provedor não reconhece a cidade — "não encontrada"
    /// é um resultado de negócio esperado, não uma falha do sistema, então não é sinalizado por
    /// exceção. Ver <see cref="Exceptions.ProvedorClimaIndisponivelException"/> para quando o
    /// provedor está de fato indisponível.</summary>
    Task<ClimaAtualBruto?> ObterClimaAtualAsync(string cidade, CancellationToken ct = default);

    /// <summary>
    /// Devolve os blocos de 3 horas crus, <b>sem</b> agregar por dia — deliberadamente, para
    /// que a agregação (a lógica mais delicada do projeto) fique testável fora da camada de I/O.
    /// <c>null</c> pelo mesmo motivo do método acima.
    /// </summary>
    Task<PrevisaoBruta?> ObterPrevisaoAsync(string cidade, CancellationToken ct = default);
}
