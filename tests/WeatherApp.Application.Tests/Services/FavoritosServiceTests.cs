using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WeatherApp.Application.Abstractions;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;
using WeatherApp.Domain.Clima;
using WeatherApp.Domain.Entities;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Application.Tests.Services;

public class FavoritosServiceTests
{
    private static readonly Guid UsuarioId = Guid.NewGuid();

    private readonly ICidadeFavoritaRepository _favoritos = Substitute.For<ICidadeFavoritaRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IUsuarioAtualProvider _usuarioAtual = Substitute.For<IUsuarioAtualProvider>();
    private readonly IWeatherProvider _clima = Substitute.For<IWeatherProvider>();
    private readonly FavoritosService _sut;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public FavoritosServiceTests()
    {
        _usuarioAtual.ObterUsuarioId().Returns(UsuarioId);
        _sut = new FavoritosService(_favoritos, _usuarios, _usuarioAtual, _clima,
            NullLogger<FavoritosService>.Instance);
    }

    [Fact]
    public async Task AdicionarAsync_lanca_duplicado_e_nao_consulta_o_provedor_quando_ja_existe()
    {
        _favoritos.ExisteAsync(UsuarioId, "Recife", Arg.Any<CancellationToken>()).Returns(true);

        await Should.ThrowAsync<FavoritoDuplicadoException>(
            () => _sut.AdicionarAsync(new CriarFavoritoRequest { Nome = "Recife" }, _ct));

        await _clima.DidNotReceive().ObterClimaAtualAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _favoritos.DidNotReceive().AdicionarAsync(Arg.Any<CidadeFavorita>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdicionarAsync_propaga_cidade_nao_encontrada_sem_persistir()
    {
        _favoritos.ExisteAsync(UsuarioId, "asdfgh", Arg.Any<CancellationToken>()).Returns(false);
        _clima.ObterClimaAtualAsync("asdfgh", Arg.Any<CancellationToken>())
            .Returns<ClimaAtualBruto>(_ => throw new CidadeNaoEncontradaException("asdfgh"));

        await Should.ThrowAsync<CidadeNaoEncontradaException>(
            () => _sut.AdicionarAsync(new CriarFavoritoRequest { Nome = "asdfgh" }, _ct));

        await _favoritos.DidNotReceive().AdicionarAsync(Arg.Any<CidadeFavorita>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdicionarAsync_garante_o_usuario_anonimo_antes_de_persistir_o_favorito()
    {
        _clima.ObterClimaAtualAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(NovoClimaAtual("Recife", "BR"));

        await _sut.AdicionarAsync(new CriarFavoritoRequest { Nome = "Recife" }, _ct);

        await _usuarios.Received(1).GarantirAnonimoAsync(UsuarioId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdicionarAsync_usa_o_nome_canonico_devolvido_pelo_provedor()
    {
        // Usuário digita "sao jose do rio preto", provedor devolve o nome canônico.
        _clima.ObterClimaAtualAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(NovoClimaAtual("São José do Rio Preto", "BR"));

        var resultado = await _sut.AdicionarAsync(
            new CriarFavoritoRequest { Nome = "sao jose do rio preto" }, _ct);

        resultado.Nome.ShouldBe("São José do Rio Preto");
        await _favoritos.Received(1).AdicionarAsync(
            Arg.Is<CidadeFavorita>(f => f!.Nome == "São José do Rio Preto"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoverAsync_lanca_nao_encontrado_quando_ausente_ou_de_outro_usuario()
    {
        var id = Guid.NewGuid();
        _favoritos.ObterPorIdAsync(id, UsuarioId, Arg.Any<CancellationToken>())
            .Returns((CidadeFavorita?)null);

        await Should.ThrowAsync<FavoritoNaoEncontradoException>(() => _sut.RemoverAsync(id, _ct));

        await _favoritos.DidNotReceive().RemoverAsync(Arg.Any<CidadeFavorita>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoverAsync_remove_quando_o_favorito_pertence_ao_usuario()
    {
        var favorita = CidadeFavorita.Criar(UsuarioId, "Recife", "BR");
        _favoritos.ObterPorIdAsync(favorita.Id, UsuarioId, Arg.Any<CancellationToken>())
            .Returns(favorita);

        await _sut.RemoverAsync(favorita.Id, _ct);

        await _favoritos.Received(1).RemoverAsync(favorita, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListarAsync_devolve_os_favoritos_do_usuario_atual()
    {
        var favorita = CidadeFavorita.Criar(UsuarioId, "Recife", "BR");
        _favoritos.ListarPorUsuarioAsync(UsuarioId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<CidadeFavorita>)[favorita]);

        var resultado = await _sut.ListarAsync(_ct);

        resultado.ShouldHaveSingleItem();
        resultado[0].Nome.ShouldBe("Recife");
    }

    private static ClimaAtualBruto NovoClimaAtual(string cidade, string pais) => new(
        Cidade: cidade,
        PaisCodigo: pais,
        Temperatura: 25m,
        SensacaoTermica: 25m,
        TemperaturaMinimaInstantanea: 24m,
        TemperaturaMaximaInstantanea: 26m,
        Umidade: 50,
        Condicao: "céu limpo",
        Icone: "01d",
        Latitude: -8.05m,
        Longitude: -34.9m,
        OffsetSegundos: -10800,
        InstanteUtc: DateTimeOffset.UtcNow);
}
