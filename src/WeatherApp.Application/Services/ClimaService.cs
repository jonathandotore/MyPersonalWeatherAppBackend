using Microsoft.Extensions.Logging;
using WeatherApp.Application.Clima;
using WeatherApp.Application.DTOs;
using WeatherApp.Domain.Clima;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Application.Services;

/// <summary>
/// Orquestra as consultas de clima, compondo os dois endpoints do provedor.
/// </summary>
public sealed class ClimaService(
    IWeatherProvider provider,
    TimeProvider relogio,
    ILogger<ClimaService> logger)
{
    /// <summary>
    /// Clima atual da cidade, com a máxima/mínima <b>reais do dia</b>. Devolve <c>null</c> quando
    /// a cidade não é reconhecida pelo provedor — o controller traduz isso em 404 diretamente,
    /// sem exceção envolvida.
    ///
    /// <para><b>Por que duas chamadas ao provedor.</b> O endpoint de clima atual expõe
    /// <c>temp_min</c>/<c>temp_max</c>, mas a própria OpenWeatherMap documenta esses campos como
    /// a variação entre estações meteorológicas <i>no instante da leitura</i> — numa amostra real
    /// de Curitiba, uma faixa de 1,4 °C. Usá-los renderia um card exibindo "Máx 12° / Mín 11°"
    /// o dia inteiro: um erro visível na tela. A amplitude verdadeira do dia é derivada dos
    /// blocos de 3 h da previsão.</para>
    ///
    /// <para>O custo da segunda chamada é praticamente nulo porque o
    /// <c>CachedWeatherProvider</c> compartilha a entrada de previsão com a tela de 5 dias —
    /// é o argumento mais forte a favor do Decorator neste projeto.</para>
    /// </summary>
    public async Task<ClimaAtualDto?> ObterClimaAtualAsync(string cidade, CancellationToken ct = default)
    {
        var atual = await provider.ObterClimaAtualAsync(cidade, ct);
        if (atual is null)
        {
            return null;
        }

        var (maxima, minima, fonte) = await ResolverMaximaMinimaDoDiaAsync(cidade, atual, ct);

        var offset = TimeSpan.FromSeconds(atual.OffsetSegundos);

        return new ClimaAtualDto
        {
            Cidade = atual.Cidade,
            PaisCodigo = atual.PaisCodigo,
            Temperatura = ArredondarTemperatura(atual.Temperatura),
            SensacaoTermica = ArredondarTemperatura(atual.SensacaoTermica),
            TemperaturaMaxima = ArredondarTemperatura(maxima),
            TemperaturaMinima = ArredondarTemperatura(minima),
            Umidade = atual.Umidade,
            Condicao = atual.Condicao,
            Icone = atual.Icone,
            IconeUrl = PrevisaoDiariaAggregator.MontarUrlIcone(atual.Icone),
            Latitude = atual.Latitude,
            Longitude = atual.Longitude,
            DataHoraLocal = atual.InstanteUtc.ToOffset(offset),
            FonteMaxMin = fonte
        };
    }

    /// <summary>Previsão agregada em 5 dias. Devolve <c>null</c> quando a cidade não é reconhecida
    /// pelo provedor.</summary>
    public async Task<PrevisaoDto?> ObterPrevisao5DiasAsync(string cidade, CancellationToken ct = default)
    {
        var previsao = await provider.ObterPrevisaoAsync(cidade, ct);
        if (previsao is null)
        {
            return null;
        }

        return new PrevisaoDto
        {
            Cidade = previsao.Cidade,
            PaisCodigo = previsao.PaisCodigo,
            Dias = PrevisaoDiariaAggregator.Agregar(previsao, relogio.GetUtcNow())
        };
    }

    /// <summary>
    /// Deriva máxima/mínima do dia a partir da previsão, degradando para os campos da leitura
    /// instantânea sempre que a previsão não ajudar — indisponível, sem bloco para hoje, ou (caso
    /// raro) sem encontrar a mesma cidade que o clima atual acabou de resolver. A tela continua
    /// funcionando, e o campo <c>fonteMaxMin</c> do DTO deixa a degradação explícita em vez de
    /// silenciosa.
    /// </summary>
    private async Task<(decimal Maxima, decimal Minima, string Fonte)> ResolverMaximaMinimaDoDiaAsync(
        string cidade,
        ClimaAtualBruto atual,
        CancellationToken ct)
    {
        (decimal Maxima, decimal Minima, string Fonte) DegradarParaLeituraAtual() =>
            (atual.TemperaturaMaximaInstantanea, atual.TemperaturaMinimaInstantanea, "leitura-atual");

        try
        {
            var previsao = await provider.ObterPrevisaoAsync(cidade, ct);
            if (previsao is null)
            {
                logger.LogDebug(
                    "Previsão não encontrou {Cidade} logo após o clima atual resolvê-la; usando a leitura instantânea.",
                    cidade);
                return DegradarParaLeituraAtual();
            }

            var offset = TimeSpan.FromSeconds(atual.OffsetSegundos);
            var hojeLocal = DateOnly.FromDateTime(atual.InstanteUtc.ToOffset(offset).DateTime);

            var doDia = PrevisaoDiariaAggregator.ObterMaximaMinimaDoDia(previsao, hojeLocal);

            if (doDia is null)
            {
                // Consulta no fim do dia local: já não há bloco de previsão para hoje.
                logger.LogDebug(
                    "Sem blocos de previsão para hoje em {Cidade}; usando a leitura instantânea.",
                    cidade);
                return DegradarParaLeituraAtual();
            }

            // A temperatura atual entra no cálculo: sem isso é possível exibir "atual 26°,
            // máx 24°", uma inconsistência óbvia para quem está olhando a tela.
            var maxima = Math.Max(doDia.Value.Maxima, atual.Temperatura);
            var minima = Math.Min(doDia.Value.Minima, atual.Temperatura);

            return (maxima, minima, "previsao");
        }
        catch (Exception ex) when (ex is ProvedorClimaIndisponivelException
                                      or FalhaIntegracaoProvedorException)
        {
            // A previsão é complementar aqui: se ela falhar, ainda entregamos o clima atual.
            // Diferente de "não encontrada", isto é uma falha real do provedor — continua exceção.
            logger.LogWarning(ex,
                "Previsão indisponível para {Cidade}; máxima/mínima cairão para a leitura instantânea.",
                cidade);

            return DegradarParaLeituraAtual();
        }
    }

    /// <summary>Arredondamento comercial (0,5 sempre para longe do zero) — o que se espera ao
    /// ver "26°" numa tela de clima, diferente do arredondamento bancário do <c>Math.Round</c> padrão.</summary>
    private static int ArredondarTemperatura(decimal valor) =>
        (int)Math.Round(valor, MidpointRounding.AwayFromZero);
}
