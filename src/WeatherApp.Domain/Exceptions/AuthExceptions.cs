namespace WeatherApp.Domain.Exceptions;

/// <summary>
/// E-mail inexistente <b>ou</b> senha incorreta. Vira HTTP 401 com a mesma mensagem nos dois
/// casos, de propósito: distinguir "e-mail não cadastrado" de "senha errada" entrega ao
/// atacante um oráculo de enumeração de usuários.
/// </summary>
public sealed class CredenciaisInvalidasException() : DomainException("E-mail ou senha inválidos.")
{
    public override string Titulo => "Credenciais inválidas";
}

/// <summary>
/// E-mail já usado por outro usuário registrado. Vira HTTP 409.
/// </summary>
public sealed class EmailJaCadastradoException(string email) : DomainException($"O e-mail '{email}' já está cadastrado.")
{
    public override string Titulo => "E-mail já cadastrado";
}
