using WeatherApp.Application.Clima;
using WeatherApp.Domain.Clima;

namespace WeatherApp.Application.Tests.Clima;

/// <summary>
/// Testes da agregação de previsão — a lógica mais delicada do projeto.
///
/// <para>A cidade de referência é <b>São José do Rio Preto (SP)</b>, offset <c>-10800</c>
/// confirmado contra a API ao vivo. Duas observações reais do mesmo endpoint, em horários
/// diferentes do mesmo dia, devolveram distribuições <b>diferentes</b> dos 40 blocos:</para>
/// <code>
/// consulta às 12:00Z -> 5 / 8 / 8 / 8 / 8 / 3
/// consulta às 15:00Z -> 4 / 8 / 8 / 8 / 8 / 4
/// </code>
/// <para>Ou seja: o recorte de cabeça e cauda <b>varia conforme a hora da consulta</b>, e o que
/// é invariante é sempre haver <b>6</b> datas locais, nunca 5. Por isso os dois formatos são
/// testados: a agregação não pode depender de um split específico.</para>
/// </summary>
public class PrevisaoDiariaAggregatorTests
{
    private const string CidadeTeste = "São José do Rio Preto";

    /// <summary>
    /// São José do Rio Preto (SP): UTC-3 em segundos, verificado na resposta da API
    /// (<c>timezone = -10800</c>). Offset negativo é justamente o caso que expõe bug de fuso.
    /// </summary>
    private const int OffsetSaoJoseDoRioPreto = -10800;

    /// <summary>Âncora da observação real das 12:00Z (distribuição 5/8/8/8/8/3).</summary>
    private static readonly DateTimeOffset PrimeiroBloco12h =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Âncora da observação real das 15:00Z (distribuição 4/8/8/8/8/4).</summary>
    private static readonly DateTimeOffset PrimeiroBloco15h =
        new(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Amostra_real_das_12h_cai_em_6_datas_locais()
    {
        // Sanidade do cenário: se esta premissa mudar, os testes abaixo perdem sentido.
        ContarBlocosPorDataLocal(PrimeiroBloco12h).ShouldBe([5, 8, 8, 8, 8, 3]);
    }

    [Fact]
    public void Amostra_real_das_15h_tambem_cai_em_6_datas_locais_com_split_diferente()
    {
        // Mesma cidade, mesmo dia, hora de consulta diferente: cabeça e cauda mudam,
        // mas continuam sendo 6 buckets.
        ContarBlocosPorDataLocal(PrimeiroBloco15h).ShouldBe([4, 8, 8, 8, 8, 4]);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    public void Agregar_devolve_exatamente_5_dias_nos_dois_formatos_reais(int horaUtcDoPrimeiroBloco)
    {
        var primeiro = new DateTimeOffset(2026, 7, 30, horaUtcDoPrimeiroBloco, 0, 0, TimeSpan.Zero);
        var previsao = ConstruirPrevisao(primeiro, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, primeiro);

        dias.Count.ShouldBe(5);
    }

    [Fact]
    public void Agregar_devolve_datas_locais_consecutivas_comecando_hoje()
    {
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);

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
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);

        dias.ShouldNotContain(d => d.Data == new DateOnly(2026, 8, 4));
    }

    [Fact]
    public void Agrupamento_usa_data_LOCAL_e_nao_UTC()
    {
        // O bloco de 00:00Z de 31/07 é 21:00 do dia 30 em São José do Rio Preto (UTC-3).
        // Se o agrupamento usasse a data UTC, ele cairia no dia 31 e deslocaria todos os
        // cards em um dia inteiro.
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);

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
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);
        var offset = TimeSpan.FromSeconds(OffsetSaoJoseDoRioPreto);

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

    [Theory]
    [InlineData(12)] // 5 blocos hoje
    [InlineData(15)] // 4 blocos hoje
    public void Dia_corrente_e_mantido_quando_tem_cobertura_suficiente(int horaUtcDoPrimeiroBloco)
    {
        var primeiro = new DateTimeOffset(2026, 7, 30, horaUtcDoPrimeiroBloco, 0, 0, TimeSpan.Zero);
        var previsao = ConstruirPrevisao(primeiro, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, primeiro);

        // Nos dois formatos reais hoje tem >= 3 blocos, então hoje entra na lista.
        dias[0].Data.ShouldBe(new DateOnly(2026, 7, 30));
    }

    [Fact]
    public void Dias_anteriores_a_hoje_sao_ignorados()
    {
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        // "Agora" três dias à frente: só devem sobrar os dias >= 02/08.
        var agora = new DateTimeOffset(2026, 8, 2, 15, 0, 0, TimeSpan.Zero);
        var dias = PrevisaoDiariaAggregator.Agregar(previsao, agora);

        dias.ShouldAllBe(d => d.Data >= new DateOnly(2026, 8, 2));
    }

    [Fact]
    public void Icone_escolhido_e_o_do_bloco_mais_proximo_do_meio_dia_local()
    {
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);

        // ConstruirPrevisao marca o bloco de 12:00 local com o código "01d".
        dias[1].Icone.ShouldBe("01d");
    }

    [Fact]
    public void Icone_noturno_e_convertido_para_a_variante_diurna()
    {
        // Um único bloco, às 23:00 local, com ícone noturno.
        var primeiro = new DateTimeOffset(2026, 7, 31, 2, 0, 0, TimeSpan.Zero); // 23:00 local dia 30
        var previsao = new PrevisaoBruta(CidadeTeste, "BR", OffsetSaoJoseDoRioPreto,
        [
            NovoBloco(primeiro, 15m, 14m, 16m, icone: "10n", ehDiurno: false)
        ]);

        // "Agora" no dia anterior, para que o único dia disponível não seja descartado
        // pela regra de cobertura mínima do dia corrente.
        var dias = PrevisaoDiariaAggregator.Agregar(
            previsao, agoraUtc: new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));

        dias.ShouldHaveSingleItem();
        dias[0].Icone.ShouldBe("10d");
        dias[0].IconeUrl.ShouldBe("https://openweathermap.org/img/wn/10d@2x.png");
    }

    [Fact]
    public void Lista_vazia_nao_estoura()
    {
        var previsao = new PrevisaoBruta(CidadeTeste, "BR", OffsetSaoJoseDoRioPreto, []);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);

        dias.ShouldBeEmpty();
    }

    [Fact]
    public void Menos_de_5_dias_disponiveis_retorna_o_que_houver_sem_lancar()
    {
        // 8 blocos = 1 dia local cheio + resto.
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 8);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);

        dias.Count.ShouldBeLessThan(5);
        dias.ShouldNotBeEmpty();
    }

    [Fact]
    public void Fuso_fracionario_da_India_agrupa_corretamente()
    {
        // +05:30 faz as horas locais caírem em :30, exercitando o cálculo fracionário.
        // Não é o caso de São José do Rio Preto, mas garante que a lógica não assume
        // offsets múltiplos de uma hora.
        const int offsetIndia = 19800;
        var previsao = ConstruirPrevisao(
            PrimeiroBloco12h, quantidade: 40, offsetSegundos: offsetIndia);

        var dias = PrevisaoDiariaAggregator.Agregar(previsao, PrimeiroBloco12h);

        dias.Count.ShouldBe(5);
        dias.Select(d => d.Data).ShouldBeInOrder();
        // Sem datas repetidas — sintoma clássico de agrupamento por instante em vez de data.
        dias.Select(d => d.Data).Distinct().Count().ShouldBe(dias.Count);
    }

    [Fact]
    public void ObterMaximaMinimaDoDia_retorna_null_para_data_sem_blocos()
    {
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        var resultado = PrevisaoDiariaAggregator
            .ObterMaximaMinimaDoDia(previsao, new DateOnly(2030, 1, 1));

        resultado.ShouldBeNull();
    }

    [Fact]
    public void ObterMaximaMinimaDoDia_usa_amplitude_dos_blocos_do_dia()
    {
        var previsao = ConstruirPrevisao(PrimeiroBloco12h, quantidade: 40);

        var resultado = PrevisaoDiariaAggregator
            .ObterMaximaMinimaDoDia(previsao, new DateOnly(2026, 7, 30));

        resultado.ShouldNotBeNull();
        // Amplitude real do dia, muito maior que a faixa instantânea que o endpoint de clima
        // atual reportaria. Medido ao vivo para São José do Rio Preto, temp_min e temp_max de
        // /data/2.5/weather vieram AMBOS iguais a 23,92 — faixa de exatamente 0 °C.
        (resultado!.Value.Maxima - resultado.Value.Minima).ShouldBeGreaterThan(5m);
    }

    // ---------------------------------------------------------------------------------
    // Construção do cenário
    // ---------------------------------------------------------------------------------

    private static int[] ContarBlocosPorDataLocal(DateTimeOffset primeiroBlocoUtc)
    {
        var previsao = ConstruirPrevisao(primeiroBlocoUtc, quantidade: 40);
        var offset = TimeSpan.FromSeconds(OffsetSaoJoseDoRioPreto);

        return [.. previsao.Blocos
            .GroupBy(b => DateOnly.FromDateTime(b.InstanteUtc.ToOffset(offset).DateTime))
            .OrderBy(g => g.Key)
            .Select(g => g.Count())];
    }

    /// <summary>
    /// Gera blocos de 3 em 3 horas a partir de um instante UTC, com temperaturas determinísticas
    /// que variam ao longo do dia (para que máxima/mínima sejam distinguíveis) e o bloco de
    /// 12:00 local marcado com o ícone "01d".
    /// </summary>
    private static PrevisaoBruta ConstruirPrevisao(
        DateTimeOffset primeiroBlocoUtc,
        int quantidade,
        int offsetSegundos = OffsetSaoJoseDoRioPreto)
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

        return new PrevisaoBruta(CidadeTeste, "BR", offsetSegundos, blocos);
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
