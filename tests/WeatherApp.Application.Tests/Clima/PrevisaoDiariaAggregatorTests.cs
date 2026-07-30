using WeatherApp.Application.Clima;
using WeatherApp.Domain.Clima;

namespace WeatherApp.Application.Tests.Clima;

/// <summary>
/// Testes da agregação de previsão — a lógica mais delicada do projeto.
///
/// <para>Os cenários reproduzem a distribuição <b>real</b> medida contra a API da
/// OpenWeatherMap para Curitiba (offset -10800): 40 blocos de 3 h a partir de
/// 2026-07-30T12:00Z, que caem em <b>6</b> datas locais (5/8/8/8/8/3) — não 5.</para>
/// </summary>
public class PrevisaoDiariaAggregatorTests
{
    /// <summary>Curitiba: UTC-3, em segundos. Offset negativo é o caso que expõe bug de fuso.</summary>
    private const int OffsetCuritiba = -10800;

    /// <summary>Primeiro bloco da amostra real capturada da API.</summary>
    private static readonly DateTimeOffset PrimeiroBlocoReal =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Amostra_real_de_40_blocos_cai_em_6_datas_locais()
    {
        // Sanidade do próprio cenário: se esta premissa mudar, os testes abaixo perdem sentido.
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);
        var offset = TimeSpan.FromSeconds(OffsetCuritiba);

        var contagemPorData = previsao.Blocos
            .GroupBy(b => DateOnly.FromDateTime(b.InstanteUtc.ToOffset(offset).DateTime))
            .OrderBy(g => g.Key)
            .Select(g => g.Count())
            .ToArray();

        contagemPorData.ShouldBe([5, 8, 8, 8, 8, 3]);
    }

    [Fact]
    public void Agregar_devolve_exatamente_5_dias_apesar_dos_6_buckets()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        dias.Count.ShouldBe(5);
    }

    [Fact]
    public void Agregar_devolve_datas_locais_consecutivas_comecando_hoje()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        dias.Select(d => d.Data).ShouldBe(
        [
            new DateOnly(2026, 7, 30),
            new DateOnly(2026, 7, 31),
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 2),
            new DateOnly(2026, 8, 3)
        ]);
    }

    [Fact]
    public void Agregar_descarta_a_cauda_parcial_do_sexto_dia()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        dias.ShouldNotContain(d => d.Data == new DateOnly(2026, 8, 4));
    }

    [Fact]
    public void Agrupamento_usa_data_LOCAL_e_nao_UTC()
    {
        // O bloco de 00:00Z de 31/07 é 21:00 do dia 30 em Curitiba. Se o agrupamento usasse a
        // data UTC, ele cairia no dia 31 e deslocaria todos os cards em um dia.
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        // A mínima é o que discrimina os dois agrupamentos:
        //   - por data LOCAL, 30/07 tem 5 blocos (local 09,12,15,18,21) e o de 21:00 — que é
        //     00:00Z do dia 31 — é o mais frio, com mínima 19,5;
        //   - por data UTC, 30/07 teria só 4 blocos (12,15,18,21Z = local 09..18), e a mínima
        //     seria 21,0.
        // A máxima NÃO serve como asserção aqui: nos dois agrupamentos o bloco do meio-dia
        // entra, então ela daria 26 em ambos os casos.
        dias[0].TemperaturaMinima.ShouldBe(19.5m);
        dias[0].TemperaturaMaxima.ShouldBe(26m);

        // E o mesmo valor deve sair do helper usado pelo endpoint de clima atual.
        var maxMinDia30 = PrevisaoDiariaAggregator
            .ObterMaximaMinimaDoDia(previsao, new DateOnly(2026, 7, 30));
        maxMinDia30!.Value.Minima.ShouldBe(19.5m);
    }

    [Fact]
    public void Maxima_e_minima_do_dia_vem_dos_blocos_daquele_dia()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);
        var offset = TimeSpan.FromSeconds(OffsetCuritiba);

        foreach (var dia in dias)
        {
            var blocosDoDia = previsao.Blocos
                .Where(b => DateOnly.FromDateTime(b.InstanteUtc.ToOffset(offset).DateTime) == dia.Data)
                .ToList();

            dia.TemperaturaMaxima.ShouldBe(blocosDoDia.Max(b => b.TemperaturaMaxima));
            dia.TemperaturaMinima.ShouldBe(blocosDoDia.Min(b => b.TemperaturaMinima));
        }
    }

    [Fact]
    public void Dia_corrente_e_descartado_quando_tem_menos_de_3_blocos()
    {
        // Blocos começando 00:00Z de 31/07 => 21:00 local do dia 30: só 1 bloco para "hoje".
        var primeiro = new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);
        var previsao = ConstruirPrevisao(primeiro, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, agoraUtc: primeiro);

        // "Hoje" local é 30/07 (21:00). Com 1 bloco só, é descartado e a lista começa em 31/07.
        dias.Count.ShouldBe(5);
        dias[0].Data.ShouldBe(new DateOnly(2026, 7, 31));
        dias.ShouldNotContain(d => d.Data == new DateOnly(2026, 7, 30));
    }

    [Fact]
    public void Dia_corrente_e_mantido_quando_tem_cobertura_suficiente()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        // 5 blocos hoje >= 3, então hoje entra.
        dias[0].Data.ShouldBe(new DateOnly(2026, 7, 30));
    }

    [Fact]
    public void Dias_anteriores_a_hoje_sao_ignorados()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        // "Agora" três dias à frente: só devem sobrar os dias >= 02/08.
        var agora = new DateTimeOffset(2026, 8, 2, 15, 0, 0, TimeSpan.Zero);
        var dias = PrevisaoDiariaAggregator.Agregar(previsao, agora);

        dias.ShouldAllBe(d => d.Data >= new DateOnly(2026, 8, 2));
    }

    [Fact]
    public void Icone_escolhido_e_o_do_bloco_mais_proximo_do_meio_dia_local()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        // ConstruirPrevisao marca o bloco de 12:00 local com o código "01d".
        dias[1].Icone.ShouldBe("01d");
    }

    [Fact]
    public void Icone_noturno_e_convertido_para_a_variante_diurna()
    {
        // Um único bloco, às 23:00 local, com ícone noturno.
        var primeiro = new DateTimeOffset(2026, 7, 31, 2, 0, 0, TimeSpan.Zero); // 23:00 local dia 30
        var previsao = new PrevisaoBruta("Curitiba", "BR", OffsetCuritiba,
        [
            NovoBloco(primeiro, 15m, 14m, 16m, icone: "10n", ehDiurno: false)
        ]);

        // agoraUtc no mesmo dia local, e o dia tem < 3 blocos... então usamos "ontem" como agora
        // para que o único dia disponível não seja descartado por ser o dia corrente.
        var dias = PrevisaoDiariaAggregator.Agregar(
            previsao, agoraUtc: new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));

        dias.ShouldHaveSingleItem();
        dias[0].Icone.ShouldBe("10d");
        dias[0].IconeUrl.ShouldBe("https://openweathermap.org/img/wn/10d@2x.png");
    }

    [Fact]
    public void Lista_vazia_nao_estoura()
    {
        var previsao = new PrevisaoBruta("Curitiba", "BR", OffsetCuritiba, []);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        dias.ShouldBeEmpty();
    }

    [Fact]
    public void Menos_de_5_dias_disponiveis_retorna_o_que_houver_sem_lancar()
    {
        // 8 blocos = 1 dia local cheio + resto.
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 8);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        dias.Count.ShouldBeLessThan(5);
        dias.ShouldNotBeEmpty();
    }

    [Fact]
    public void Fuso_fracionario_da_India_agrupa_corretamente()
    {
        // +05:30 faz as horas locais caírem em :30, exercitando o cálculo fracionário.
        const int offsetIndia = 19800;
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40, offsetSegundos: offsetIndia);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBlocoReal);

        dias.Count.ShouldBe(5);
        dias.Select(d => d.Data).ShouldBeInOrder();
        // Sem datas repetidas — sintoma clássico de agrupamento por instante em vez de data.
        dias.Select(d => d.Data).Distinct().Count().ShouldBe(dias.Count);
    }

    [Fact]
    public void ObterMaximaMinimaDoDia_retorna_null_para_data_sem_blocos()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var resultado = PrevisaoDiariaAggregator
            .ObterMaximaMinimaDoDia(previsao, new DateOnly(2030, 1, 1));

        resultado.ShouldBeNull();
    }

    [Fact]
    public void ObterMaximaMinimaDoDia_usa_amplitude_dos_blocos_do_dia()
    {
        var previsao = ConstruirPrevisao(PrimeiroBlocoReal, quantidade: 40);

        var resultado = PrevisaoDiariaAggregator
            .ObterMaximaMinimaDoDia(previsao, new DateOnly(2026, 7, 30));

        resultado.ShouldNotBeNull();
        // Amplitude real (planted) do dia 30 local, bem maior que a faixa de ~1,4 °C que o
        // endpoint de clima atual reportaria em temp_min/temp_max.
        (resultado!.Value.Maxima - resultado.Value.Minima).ShouldBeGreaterThan(5m);
    }

    // ---------------------------------------------------------------------------------
    // Construção do cenário
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Gera blocos de 3 em 3 horas a partir de um instante UTC, com temperaturas determinísticas
    /// que variam ao longo do dia (para que máxima/mínima sejam distinguíveis) e o bloco de
    /// 12:00 local marcado com o ícone "01d".
    /// </summary>
    private static PrevisaoBruta ConstruirPrevisao(
        DateTimeOffset primeiroBlocoUtc,
        int quantidade,
        int offsetSegundos = OffsetCuritiba)
    {
        var offset = TimeSpan.FromSeconds(offsetSegundos);
        var blocos = new List<BlocoPrevisao>(quantidade);

        for (var i = 0; i < quantidade; i++)
        {
            var instante = primeiroBlocoUtc.AddHours(3 * i);
            var horaLocal = instante.ToOffset(offset).TimeOfDay.TotalHours;

            // Curva diária simples: mais quente por volta do meio-dia local.
            var temp = 20m + (decimal)(10d - Math.Abs(horaLocal - 12d)) / 2m;
            var ehMeioDia = Math.Abs(horaLocal - 12d) < 0.01;

            blocos.Add(NovoBloco(
                instante,
                temperatura: temp,
                minima: temp - 1m,
                maxima: temp + 1m,
                icone: ehMeioDia ? "01d" : (horaLocal is >= 6 and < 18 ? "03d" : "03n"),
                ehDiurno: horaLocal is >= 6 and < 18));
        }

        return new PrevisaoBruta("Curitiba", "BR", offsetSegundos, blocos);
    }

    private static BlocoPrevisao NovoBloco(
        DateTimeOffset instanteUtc,
        decimal temperatura,
        decimal minima,
        decimal maxima,
        string icone,
        bool ehDiurno) =>
        new(instanteUtc, temperatura, minima, maxima,
            Umidade: 80,
            Condicao: "nuvens dispersas",
            Icone: icone,
            EhDiurno: ehDiurno,
            ProbabilidadeChuva: 0.1m);
}
