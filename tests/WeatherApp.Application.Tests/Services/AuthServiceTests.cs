using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WeatherApp.Application.Abstractions;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;
using WeatherApp.Domain.Entities;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Application.Tests.Services;

public class AuthServiceTests
{
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IUsuarioAtualProvider _usuarioAtual = Substitute.For<IUsuarioAtualProvider>();
    private readonly IPasswordHasher<Usuario> _hasher = Substitute.For<IPasswordHasher<Usuario>>();
    private readonly IJwtTokenService _tokenService = Substitute.For<IJwtTokenService>();
    private readonly AuthService _sut;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public AuthServiceTests()
    {
        _hasher.HashPassword(Arg.Any<Usuario>(), Arg.Any<string>()).Returns("hash-fake");
        _tokenService.GerarToken(Arg.Any<Usuario>())
            .Returns(("token-fake", DateTime.UtcNow.AddHours(1)));
        _sut = new AuthService(_usuarios, _usuarioAtual, _hasher, _tokenService,
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task RegistrarAsync_lanca_email_ja_cadastrado()
    {
        _usuarios.ObterPorEmailAsync("ja@existe.com", Arg.Any<CancellationToken>())
            .Returns(Usuario.CriarRegistrado("Existente", "ja@existe.com"));

        await Should.ThrowAsync<EmailJaCadastradoException>(() => _sut.RegistrarAsync(
            new RegistrarRequest { Nome = "Novo", Email = "ja@existe.com", Senha = "Senha1234" }, _ct));
    }

    [Fact]
    public async Task RegistrarAsync_promove_usuario_anonimo_preservando_o_id()
    {
        var anonimoId = Guid.NewGuid();
        var anonimo = Usuario.CriarAnonimo(anonimoId);

        _usuarios.ObterPorEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Usuario?)null);
        _usuarioAtual.TentarObterUsuarioId(out Arg.Any<Guid>())
            .Returns(x => { x[0] = anonimoId; return true; });
        _usuarios.ObterPorIdAsync(anonimoId, Arg.Any<CancellationToken>()).Returns(anonimo);

        await _sut.RegistrarAsync(
            new RegistrarRequest { Nome = "Bruno", Email = "bruno@teste.com", Senha = "SenhaForte1" }, _ct);

        anonimo.EhAnonimo.ShouldBeFalse();
        anonimo.Id.ShouldBe(anonimoId); // Id preservado -> favoritos já ligados a ele continuam válidos
        await _usuarios.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
        await _usuarios.DidNotReceive().AdicionarAsync(Arg.Any<Usuario>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarAsync_cria_usuario_novo_quando_nao_ha_anonimo_para_promover()
    {
        _usuarios.ObterPorEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Usuario?)null);
        _usuarioAtual.TentarObterUsuarioId(out Arg.Any<Guid>()).Returns(false);

        await _sut.RegistrarAsync(
            new RegistrarRequest { Nome = "Ana", Email = "ana@teste.com", Senha = "SenhaForte1" }, _ct);

        await _usuarios.Received(1).AdicionarAsync(
            Arg.Is<Usuario>(u => u!.Email == "ana@teste.com" && !u.EhAnonimo), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_lanca_credenciais_invalidas_quando_email_nao_existe()
    {
        _usuarios.ObterPorEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Usuario?)null);

        await Should.ThrowAsync<CredenciaisInvalidasException>(() => _sut.LoginAsync(
            new LoginRequest { Email = "ninguem@teste.com", Senha = "qualquer" }, _ct));
    }

    [Fact]
    public async Task LoginAsync_lanca_credenciais_invalidas_quando_senha_esta_errada()
    {
        var usuario = Usuario.CriarRegistrado("Bruno", "bruno@teste.com");
        usuario.DefinirSenha("hash-real");
        _usuarios.ObterPorEmailAsync("bruno@teste.com", Arg.Any<CancellationToken>()).Returns(usuario);
        _hasher.VerifyHashedPassword(usuario, "hash-real", "errada")
            .Returns(PasswordVerificationResult.Failed);

        await Should.ThrowAsync<CredenciaisInvalidasException>(() => _sut.LoginAsync(
            new LoginRequest { Email = "bruno@teste.com", Senha = "errada" }, _ct));
    }

    [Fact]
    public async Task LoginAsync_devolve_token_quando_as_credenciais_estao_corretas()
    {
        var usuario = Usuario.CriarRegistrado("Bruno", "bruno@teste.com");
        usuario.DefinirSenha("hash-real");
        _usuarios.ObterPorEmailAsync("bruno@teste.com", Arg.Any<CancellationToken>()).Returns(usuario);
        _hasher.VerifyHashedPassword(usuario, "hash-real", "correta")
            .Returns(PasswordVerificationResult.Success);

        var resultado = await _sut.LoginAsync(
            new LoginRequest { Email = "bruno@teste.com", Senha = "correta" }, _ct);

        resultado.Token.ShouldBe("token-fake");
    }
}
