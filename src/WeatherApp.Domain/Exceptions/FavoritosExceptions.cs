namespace WeatherApp.Domain.Exceptions;

/// <summary>
/// Viola a regra UQ_Usuario_Cidade: o usuário já favoritou essa cidade. Vira HTTP 409.
/// </summary>
public sealed class FavoritoDuplicadoException(string cidade)
    : DomainException($"A cidade '{cidade}' já está nos seus favoritos.")
{
    public override string Titulo => "Cidade já favoritada";
}

/// <summary>
/// Favorito inexistente <b>ou</b> pertencente a outro usuário. Vira HTTP 404 nos dois casos,
/// deliberadamente: responder 403 quando o recurso é de outra pessoa confirmaria que aquele
/// Id existe, o que é vazamento de informação.
/// </summary>
public sealed class FavoritoNaoEncontradoException(Guid id)
    : DomainException($"Favorito '{id}' não encontrado.")
{
    public override string Titulo => "Favorito não encontrado";
}

/// <summary>
/// Não foi possível identificar o usuário: sem JWT e sem o header <c>X-Usuario-Id</c>,
/// ou com um valor que não é um GUID válido. Vira HTTP 400 antes do JWT existir e
/// HTTP 401 depois que os endpoints passam a exigir <c>[Authorize]</c>.
/// </summary>
public sealed class UsuarioNaoIdentificadoException()
    : DomainException(
        "Não foi possível identificar o usuário. Envie um GUID válido no header 'X-Usuario-Id' " +
        "ou autentique-se para obter um token.")
{
    public override string Titulo => "Usuário não identificado";
}
