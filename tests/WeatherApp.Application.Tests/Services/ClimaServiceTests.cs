using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WeatherApp.Application.Services;
using WeatherApp.Domain.Clima;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Application.Tests.Services;

public class ClimaServiceTests
{
    private const string Cidade = "São José do Rio Preto";
    private const int Offset = -10800; // UTC-3

    private readonly IWeatherProvider _provider = Substitute.For<IWeatherProvider>();
    private readonly ClimaService _sut;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ClimaServiceTests()
    {
        _sut = new ClimaService(_provider, TimeProvider.System, NullLogger<ClimaService>.Instance);
    }

    [Fact]
    public async Task ObterClimaAtual_deriva_maxima_minima_do_forecast_nao_da_leitura_instantanea()
    {
        // Anti-regressão da armadilha real: main.temp_min/temp_max de /weather vieram, numa
        // amostra ao vivo, ambos 23,92 (faixa de 0 °C) — não são a amplitude do dia.
        var agora = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
        _provider.ObterClimaAtualAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(NovoClimaAtual(temp: 23.92m, minInstantanea: 23.92m, maxInstantanea: 23.92m, agora));
        _provider.ObterPrevisaoAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(NovaPrevisaoComBlocoHoje(agora, minima: 18m, maxima: 31.6m));

        var resultado = await _sut.ObterClimaAtualAsync(Cidade, _ct);

        resultado.ShouldNotBeNull();
        resultado.TemperaturaMinima.ShouldBe(18);
        resultado.TemperaturaMaxima.ShouldBe(32); // 31,6 arredondado
        resultado.FonteMaxMin.ShouldBe("previsao");
    }

    [Fact]
    public async Task ObterClimaAtual_inclui_a_temperatura_atual_no_calculo_de_maxima_e_minima()
    {
        // Sem isso seria possível exibir "atual 33°, máxima 31°" — inconsistência óbvia na tela.
        var agora = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
        _provider.ObterClimaAtualAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(NovoClimaAtual(temp: 33m, minInstantanea: 33m, maxInstantanea: 33m, agora));
        _provider.ObterPrevisaoAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(NovaPrevisaoComBlocoHoje(agora, minima: 18m, maxima: 31m));

        var resultado = await _sut.ObterClimaAtualAsync(Cidade, _ct);

        resultado.ShouldNotBeNull();
        resultado.TemperaturaMaxima.ShouldBe(33);
    }

    [Fact]
    public async Task ObterClimaAtual_devolve_null_para_cidade_nao_encontrada_sem_chamar_a_previsao()
    {
        // "Não encontrada" é um resultado normal, não uma exceção: o provider devolve null
        // e o serviço propaga null, sem lançar nada.
        _provider.ObterClimaAtualAsync("inexistente", Arg.Any<CancellationToken>())
            .Returns((ClimaAtualBruto?)null);

        var resultado = await _sut.ObterClimaAtualAsync("inexistente", _ct);

        resultado.ShouldBeNull();
        await _provider.DidNotReceive().ObterPrevisaoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ObterClimaAtual_degrada_para_leitura_instantanea_quando_a_previsao_falha()
    {
        var agora = DateTimeOffset.UtcNow;
        _provider.ObterClimaAtualAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(NovoClimaAtual(temp: 25m, minInstantanea: 24m, maxInstantanea: 26m, agora));
        _provider.ObterPrevisaoAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns<PrevisaoBruta?>(_ => throw new ProvedorClimaIndisponivelException("indisponível"));

        var resultado = await _sut.ObterClimaAtualAsync(Cidade, _ct);

        resultado.ShouldNotBeNull();
        resultado.FonteMaxMin.ShouldBe("leitura-atual");
        resultado.TemperaturaMinima.ShouldBe(24);
        resultado.TemperaturaMaxima.ShouldBe(26);
    }

    [Fact]
    public async Task ObterClimaAtual_degrada_quando_nao_ha_bloco_de_previsao_para_hoje()
    {
        // Ex.: consulta tarde da noite, quando o último bloco de hoje já passou.
        var agora = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
        _provider.ObterClimaAtualAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(NovoClimaAtual(temp: 20m, minInstantanea: 19m, maxInstantanea: 21m, agora));
        _provider.ObterPrevisaoAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(new PrevisaoBruta(Cidade, "BR", Offset,
            [
                NovoBloco(agora.AddDays(1), 22m, 20m, 24m) // só amanhã, nada de hoje
            ]));

        var resultado = await _sut.ObterClimaAtualAsync(Cidade, _ct);

        resultado.ShouldNotBeNull();
        resultado.FonteMaxMin.ShouldBe("leitura-atual");
        resultado.TemperaturaMinima.ShouldBe(19);
        resultado.TemperaturaMaxima.ShouldBe(21);
    }

    [Fact]
    public async Task ObterPrevisao5Dias_delega_a_agregacao_para_o_provider_e_devolve_5_dias()
    {
        var primeiro = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var blocos = Enumerable.Range(0, 40)
            .Select(i => NovoBloco(primeiro.AddHours(3 * i), 20m, 19m, 21m))
            .ToList();
        _provider.ObterPrevisaoAsync(Cidade, Arg.Any<CancellationToken>())
            .Returns(new PrevisaoBruta(Cidade, "BR", Offset, blocos));

        var relogio = Substitute.For<TimeProvider>();
        relogio.GetUtcNow().Returns(primeiro);
        var sut = new ClimaService(_provider, relogio, NullLogger<ClimaService>.Instance);

        var resultado = await sut.ObterPrevisao5DiasAsync(Cidade, _ct);

        resultado.ShouldNotBeNull();
        resultado.Dias.Count.ShouldBe(5);
        resultado.Cidade.ShouldBe(Cidade);
    }

    [Fact]
    public async Task ObterPrevisao5Dias_devolve_null_para_cidade_nao_encontrada()
    {
        _provider.ObterPrevisaoAsync("inexistente", Arg.Any<CancellationToken>())
            .Returns((PrevisaoBruta?)null);

        var resultado = await _sut.ObterPrevisao5DiasAsync("inexistente", _ct);

        resultado.ShouldBeNull();
    }

    private static ClimaAtualBruto NovoClimaAtual(
        decimal temp, decimal minInstantanea, decimal maxInstantanea, DateTimeOffset instante) => new(
        Cidade: Cidade,
        PaisCodigo: "BR",
        Temperatura: temp,
        SensacaoTermica: temp,
        TemperaturaMinimaInstantanea: minInstantanea,
        TemperaturaMaximaInstantanea: maxInstantanea,
        Umidade: 50,
        Condicao: "céu limpo",
        Icone: "01d",
        Latitude: -20.8197m,
        Longitude: -49.3794m,
        OffsetSegundos: Offset,
        InstanteUtc: instante);

    private static PrevisaoBruta NovaPrevisaoComBlocoHoje(DateTimeOffset agora, decimal minima, decimal maxima) =>
        new(Cidade, "BR", Offset, [NovoBloco(agora, (minima + maxima) / 2, minima, maxima)]);

    private static BlocoPrevisao NovoBloco(DateTimeOffset instanteUtc, decimal temp, decimal min, decimal max) =>
        new(instanteUtc, temp, min, max, Umidade: 50, Condicao: "céu limpo", Icone: "01d",
            EhDiurno: true, ProbabilidadeChuva: 0m);
}
