using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using WeatherApp.Application.Abstractions;
using WeatherApp.Application.DTOs;
using WeatherApp.Domain.Entities;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Application.Services;

public sealed class AuthService(
    IUsuarioRepository usuarios,
    IUsuarioAtualProvider usuarioAtual,
    IPasswordHasher<Usuario> hasher,
    IJwtTokenService tokenService,
    ILogger<AuthService> logger)
{
    public async Task<TokenResponse> RegistrarAsync(RegistrarRequest request, CancellationToken ct = default)
    {
        var email = Usuario.NormalizarEmail(request.Email);
        if (await usuarios.ObterPorEmailAsync(email, ct) is not null)
        {
            throw new EmailJaCadastradoException(email);
        }

        Usuario usuario;

        // Se o cliente já tinha um GUID anônimo (favoritos criados antes do login), promove
        // essa linha em vez de criar uma nova — os favoritos são preservados sem migration.
        if (usuarioAtual.TentarObterUsuarioId(out var anonimoId)
            && await usuarios.ObterPorIdAsync(anonimoId, ct) is { EhAnonimo: true } anonimo)
        {
            anonimo.Promover(request.Nome, email, hasher.HashPassword(anonimo, request.Senha));
            usuario = anonimo;
            await usuarios.SalvarAsync(ct);
        }
        else
        {
            usuario = Usuario.CriarRegistrado(request.Nome, email);
            usuario.DefinirSenha(hasher.HashPassword(usuario, request.Senha));
            await usuarios.AdicionarAsync(usuario, ct);
        }

        logger.LogInformation("Usuário {Email} registrado.", email);
        return CriarResposta(usuario);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = Usuario.NormalizarEmail(request.Email);
        var usuario = await usuarios.ObterPorEmailAsync(email, ct);

        // Mesma exceção para e-mail inexistente e senha errada: distinguir os dois casos
        // entregaria um oráculo de enumeração de usuários.
        if (usuario?.SenhaHash is null || hasher.VerifyHashedPassword(
                usuario, usuario.SenhaHash, request.Senha) == PasswordVerificationResult.Failed)
        {
            throw new CredenciaisInvalidasException();
        }

        return CriarResposta(usuario);
    }

    private TokenResponse CriarResposta(Usuario usuario)
    {
        var (token, expiraEm) = tokenService.GerarToken(usuario);
        return new TokenResponse { Token = token, ExpiraEmUtc = expiraEm };
    }
}
