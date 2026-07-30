namespace WeatherApp.Domain.Clima;

/// <summary>
/// Leitura de clima atual já traduzida do formato do provedor para um modelo neutro.
/// É o <i>anti-corruption layer</i> do Adapter: nenhum JSON da OpenWeatherMap atravessa
/// esta fronteira, então trocar de provedor não toca em regra de negócio.
/// </summary>
/// <param name="Cidade">Nome canônico devolvido pelo provedor (ex.: "Curitiba").</param>
/// <param name="PaisCodigo">Código ISO do país (ex.: "BR").</param>
/// <param name="Temperatura">Temperatura no instante da medição, em °C.</param>
/// <param name="TemperaturaMinimaInstantanea">
/// ⚠️ <b>Não é a mínima do dia.</b> A OpenWeatherMap documenta este campo como a variação de
/// temperatura entre estações meteorológicas <i>no momento atual</i> dentro do polígono da
/// cidade — numa amostra real de Curitiba a faixa foi de apenas 1,4 °C. Usar isso como
/// "mínima do dia" produz um card exibindo "Máx 12° / Mín 11°" o dia inteiro. A mínima real do
/// dia é derivada dos blocos da previsão; ver <c>PrevisaoDiariaAggregator</c>. Mantemos o campo
/// só como fallback para quando a previsão estiver indisponível.
/// </param>
/// <param name="TemperaturaMaximaInstantanea">Ver a observação de <paramref name="TemperaturaMinimaInstantanea"/>.</param>
/// <param name="OffsetSegundos">
/// Deslocamento do fuso local em relação a UTC, em segundos (ex.: -10800 para Curitiba).
/// Indispensável para agrupar a previsão por data <i>local</i> em vez de UTC.
/// </param>
public sealed record ClimaAtualBruto(
    string Cidade,
    string? PaisCodigo,
    decimal Temperatura,
    decimal SensacaoTermica,
    decimal TemperaturaMinimaInstantanea,
    decimal TemperaturaMaximaInstantanea,
    int Umidade,
    string Condicao,
    string Icone,
    decimal? Latitude,
    decimal? Longitude,
    int OffsetSegundos,
    DateTimeOffset InstanteUtc);
