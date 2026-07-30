namespace WeatherApp.Domain.Entities;

/// <summary>
/// Usuário da aplicação, em duas variantes que compartilham a mesma tabela e a mesma PK:
///
/// <list type="bullet">
///   <item><b>Anônimo</b> — criado a partir do GUID estável que o frontend guarda no
///   LocalStorage, antes de existir autenticação. <see cref="Email"/> e
///   <see cref="SenhaHash"/> são <c>null</c>.</item>
///   <item><b>Registrado</b> — tem e-mail e hash de senha, e pode autenticar via JWT.</item>
/// </list>
///
/// O ponto dessa modelagem é que <see cref="Promover"/> converte um anônimo em registrado
/// <b>sem trocar o Id</b>. Como <c>CidadesFavoritas.UsuarioId</c> aponta para essa PK, os
/// favoritos criados antes do login são preservados automaticamente, e a entrada do JWT
/// (fase 3) não exige nenhuma migration de schema.
/// </summary>
public sealed class Usuario
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;

    /// <summary>Nulo em usuário anônimo. Ver índice único filtrado em <c>UsuarioConfiguration</c>.</summary>
    public string? Email { get; private set; }

    /// <summary>Nulo em usuário anônimo — ninguém autentica sem se registrar.</summary>
    public string? SenhaHash { get; private set; }

    public DateTime DataCriacao { get; private set; }

    public bool EhAnonimo => Email is null;

    // Construtor sem parâmetros exigido pelo EF Core para materializar a entidade.
    private Usuario() { }

    /// <summary>
    /// Cria a linha do "usuário implícito" a partir do GUID anônimo enviado pelo frontend.
    /// O Id vem de fora justamente para que ele sobreviva a um futuro registro.
    /// </summary>
    public static Usuario CriarAnonimo(Guid id) => new()
    {
        Id = id,
        Nome = "Anônimo",
        Email = null,
        SenhaHash = null,
        DataCriacao = DateTime.UtcNow
    };

    public static Usuario CriarRegistrado(string nome, string email, string senhaHash) => new()
    {
        Id = Guid.NewGuid(),
        Nome = nome,
        Email = NormalizarEmail(email),
        SenhaHash = senhaHash,
        DataCriacao = DateTime.UtcNow
    };

    /// <summary>
    /// Converte um usuário anônimo em registrado preservando o Id — e, por consequência,
    /// os favoritos já criados.
    /// </summary>
    public void Promover(string nome, string email, string senhaHash)
    {
        if (!EhAnonimo)
        {
            throw new InvalidOperationException(
                "Este usuário já está registrado; use o fluxo de login.");
        }

        Nome = nome;
        Email = NormalizarEmail(email);
        SenhaHash = senhaHash;
    }

    /// <summary>E-mail é comparado case-insensitive; guardamos já normalizado.</summary>
    public static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();
}
