using WeatherApp.Application.DTOs;
using WeatherApp.Domain.Clima;

namespace WeatherApp.Application.Clima;

/// <summary>
/// Colapsa os blocos de 3 horas da OpenWeatherMap em dias. É a única lógica realmente
/// não-trivial do projeto, então é uma <b>função pura</b> (sem I/O, sem relógio ambiente):
/// o instante "agora" entra por parâmetro, o que a torna determinística e testável.
///
/// <para><b>Por que não é um simples GroupBy.</b> Medido contra a API real (Curitiba,
/// offset -10800), os 40 blocos caem em <b>6</b> datas locais, não 5:</para>
/// <code>
/// hoje       -> 5 blocos  (parcial: começa no meio do dia)
/// hoje+1..+4 -> 8 blocos cada
/// hoje+5     -> 3 blocos  (cauda parcial)
/// </code>
/// <para>Um <c>GroupBy</c> ingênuo devolveria 6 dias, o último com máxima/mínima calculada
/// sobre 3 blocos. Daí o recorte explícito em 5.</para>
/// </summary>
public static class PrevisaoDiariaAggregator
{
    /// <summary>Quantos dias o contrato exige.</summary>
    public const int DiasRetornados = 5;

    /// <summary>
    /// Cobertura mínima (em blocos de 3 h) para que o dia corrente seja considerado
    /// representativo. Abaixo disso — uma consulta feita à noite, quando só resta 1 bloco —
    /// a "máxima do dia" seria calculada sobre uma janela de 3 horas e enganaria o usuário,
    /// então preferimos começar a lista em amanhã.
    /// </summary>
    private const int BlocosMinimosParaODiaCorrente = 3;

    private const string BaseUrlIcone = "https://openweathermap.org/img/wn/";

    /// <summary>
    /// Agrega a previsão em exatamente <see cref="DiasRetornados"/> dias (ou menos, se o
    /// provedor devolver pouca coisa — nesse caso retorna o que há, sem lançar).
    /// </summary>
    /// <param name="previsao">Blocos crus vindos do provedor.</param>
    /// <param name="agoraUtc">Instante corrente, em UTC. Parâmetro para permitir teste.</param>
    public static IReadOnlyList<PrevisaoDiaDto> Agregar(PrevisaoBruta previsao, DateTimeOffset agoraUtc)
    {
        ArgumentNullException.ThrowIfNull(previsao);

        var offset = TimeSpan.FromSeconds(previsao.OffsetSegundos);
        var hojeLocal = DateOnly.FromDateTime(agoraUtc.ToOffset(offset).DateTime);

        var grupos = ParaHorarioLocal(previsao.Blocos, offset)
            .GroupBy(b => b.DataLocal)
            .Where(g => g.Key >= hojeLocal)
            .OrderBy(g => g.Key)
            .Select(g => new GrupoDia(g.Key, [.. g.OrderBy(b => b.InstanteLocal)]))
            .ToList();

        // Descarta o dia corrente quando ele já está quase vencido (ver constante acima).
        if (grupos.Count > 0
            && grupos[0].Data == hojeLocal
            && grupos[0].Blocos.Count < BlocosMinimosParaODiaCorrente)
        {
            grupos.RemoveAt(0);
        }

        return [.. grupos.Take(DiasRetornados).Select(MapearDia)];
    }

    /// <summary>
    /// Máxima e mínima reais de uma data local específica, a partir dos blocos da previsão.
    /// Usado pelo clima atual para não depender do <c>temp_max</c>/<c>temp_min</c> da leitura
    /// instantânea, que não representam a amplitude do dia.
    /// </summary>
    /// <returns><c>null</c> quando não há nenhum bloco para aquela data.</returns>
    public static (decimal Maxima, decimal Minima)? ObterMaximaMinimaDoDia(
        PrevisaoBruta previsao,
        DateOnly dataLocal)
    {
        ArgumentNullException.ThrowIfNull(previsao);

        var offset = TimeSpan.FromSeconds(previsao.OffsetSegundos);

        var blocosDoDia = ParaHorarioLocal(previsao.Blocos, offset)
            .Where(b => b.DataLocal == dataLocal)
            .ToList();

        if (blocosDoDia.Count == 0)
        {
            return null;
        }

        return (blocosDoDia.Max(b => b.Bloco.TemperaturaMaxima),
                blocosDoDia.Min(b => b.Bloco.TemperaturaMinima));
    }

    /// <summary>Monta a URL do ícone a partir do código do provedor.</summary>
    public static string MontarUrlIcone(string icone) => $"{BaseUrlIcone}{icone}@2x.png";

    /// <summary>
    /// Projeta cada bloco para o fuso da cidade <b>uma única vez</b>, carregando o instante
    /// local junto. Recalcular ou tentar reconstruir a hora local depois é fonte clássica de
    /// erro de fuso; aqui ela é computada na entrada e só lida adiante.
    ///
    /// <para>Agrupar pela data UTC deslocaria os cards em um dia inteiro para fusos negativos:
    /// para Curitiba (-3 h), o bloco de 00:00 UTC pertence ao dia anterior local.</para>
    /// </summary>
    private static IEnumerable<BlocoLocal> ParaHorarioLocal(
        IReadOnlyList<BlocoPrevisao> blocos,
        TimeSpan offset) =>
        blocos.Select(b =>
        {
            var instanteLocal = b.InstanteUtc.ToOffset(offset);
            return new BlocoLocal(b, instanteLocal, DateOnly.FromDateTime(instanteLocal.DateTime));
        });

    private static PrevisaoDiaDto MapearDia(GrupoDia grupo)
    {
        var representativo = EscolherBlocoRepresentativo(grupo);
        var icone = ForcarVarianteDiurna(representativo.Bloco.Icone);

        return new PrevisaoDiaDto
        {
            Data = grupo.Data,
            TemperaturaMaxima = grupo.Blocos.Max(b => b.Bloco.TemperaturaMaxima),
            TemperaturaMinima = grupo.Blocos.Min(b => b.Bloco.TemperaturaMinima),
            Condicao = representativo.Bloco.Condicao,
            Icone = icone,
            IconeUrl = MontarUrlIcone(icone),
            ProbabilidadeChuva = grupo.Blocos.Max(b => b.Bloco.ProbabilidadeChuva)
        };
    }

    /// <summary>
    /// Escolhe o bloco que melhor representa o dia: o mais próximo do meio-dia local,
    /// preferindo um bloco diurno em caso de empate.
    ///
    /// <para>Alternativa avaliada e descartada: usar a condição <i>modal</i> (mais frequente)
    /// do dia. É defensável, mas exige uma regra de desempate arbitrária quando duas condições
    /// empatam; o critério do meio-dia é determinístico e corresponde ao que o usuário entende
    /// como "o tempo daquele dia".</para>
    ///
    /// <para>Fusos que não são múltiplos de uma hora (Índia +5:30, Nepal +5:45) fazem os blocos
    /// caírem em horas quebradas — por isso a distância ao meio-dia é fracionária.</para>
    /// </summary>
    private static BlocoLocal EscolherBlocoRepresentativo(GrupoDia grupo) =>
        grupo.Blocos
            .OrderBy(b => Math.Abs(b.InstanteLocal.TimeOfDay.TotalHours - 12d))
            .ThenByDescending(b => b.Bloco.EhDiurno)
            .ThenBy(b => b.InstanteLocal)
            .First();

    /// <summary>
    /// Troca o sufixo noturno do ícone pelo diurno ("10n" -> "10d"). Num card que resume o dia
    /// inteiro, um ícone noturno fica visualmente incoerente.
    /// </summary>
    private static string ForcarVarianteDiurna(string icone) =>
        icone.EndsWith('n') ? string.Concat(icone.AsSpan(0, icone.Length - 1), "d") : icone;

    /// <summary>Bloco com o instante já convertido para o fuso da cidade.</summary>
    private sealed record BlocoLocal(
        BlocoPrevisao Bloco,
        DateTimeOffset InstanteLocal,
        DateOnly DataLocal);

    private sealed record GrupoDia(DateOnly Data, IReadOnlyList<BlocoLocal> Blocos);
}
