# MyPersonalWeatherApp — Backend

API REST em **.NET 10** para consulta de clima e previsão do tempo, integrando com a
**OpenWeatherMap** e persistindo cidades favoritas em **SQL Server**, com autenticação JWT.

Teste técnico — nível pleno. O planejamento de arquitetura que originou este código está
versionado em [`planejamento-backend-dotnet.md`](./planejamento-backend-dotnet.md); este README
documenta o que foi **efetivamente implementado** e, principalmente, **o porquê de cada decisão**.

> O frontend Angular que consome esta API vive em repositório separado:
> [MyPersonalWeatherAppFrontend](https://github.com/jonathandotore/MyPersonalWeatherAppFrontend).

---

## Sumário

- [Status de implementação](#status-de-implementação)
- [Stack e tecnologias](#stack-e-tecnologias)
- [Arquitetura](#arquitetura)
- [Design patterns aplicados](#design-patterns-aplicados)
- [Modelagem de dados](#modelagem-de-dados)
- [Contrato da API](#contrato-da-api)
- [Decisões técnicas relevantes](#decisões-técnicas-relevantes)
- [Estratégia de testes](#estratégia-de-testes)
- [Desvios deliberados do planejamento](#desvios-deliberados-do-planejamento)
- [Como rodar](#como-rodar)
- [Fora de escopo / evolução futura](#fora-de-escopo--evolução-futura)

---

## Status de implementação

| Fase | Entrega | Status |
|---|---|:--:|
| 0 | Estrutura da solution, 4 camadas, configuração, segredos | ✅ |
| 1 | Domínio, EF Core + migration, integração OpenWeatherMap, clima atual | ✅ |
| 2 | Previsão de 5 dias, CRUD de favoritos, validação, usuário implícito | ✅ |
| 3 | JWT (bônus) | ✅ |
| 4 | Cache (Decorator) + resiliência (Polly) | ✅ |
| 5 | Testes de serviços, OpenAPI completo | ✅ |

**Checklist do enunciado, tudo entregue:** busca de clima por cidade, previsão de 5 dias, CRUD de
favoritos persistido em SQL Server, tratamento de erros e validação de entrada, JWT protegendo
favoritos (bônus), Swagger/OpenAPI documentado, README com decisões de arquitetura.

**43 testes unitários passando**, banco criado por migration, cache e resiliência confirmados ao
vivo (não só configurados) — ver [Decisões técnicas relevantes](#decisões-técnicas-relevantes) e
[Estratégia de testes](#estratégia-de-testes).

---

## Stack e tecnologias

| Tecnologia | Versão | Por que |
|---|---|---|
| **.NET / ASP.NET Core** | 10.0 (`net10.0`) | Versão pedida no enunciado; SDK 10.0.302 |
| **C#** | `latest` | `record`, primary constructors, collection expressions — reduzem cerimônia em DTOs e entidades |
| **Entity Framework Core** | 10.0.10 | Produtividade em CRUD e, principalmente, **migrations versionadas**: o schema nasce do código e é reproduzível por quem clona o repositório |
| **SQL Server Express** | 2022 | Exigido pelo enunciado. Docker não estava disponível na máquina de desenvolvimento, então a instância local substitui o `docker-compose` sugerido no planejamento |
| **Microsoft.AspNetCore.OpenApi** | 10.0.10 | Geração nativa do documento OpenAPI no .NET 10 |
| **Scalar.AspNetCore** | 2.16.16 | UI navegável para o documento OpenAPI, com suporte a Bearer ([ver justificativa](#1-scalar-em-vez-de-swagger-ui)) |
| **Microsoft.AspNetCore.Authentication.JwtBearer** | 10.0.10 | Validação de token nos endpoints protegidos |
| **Microsoft.IdentityModel.JsonWebTokens** | 8.19.2 | `JsonWebTokenHandler` — handler atual de emissão de JWT, mais leve que o `JwtSecurityTokenHandler` legado |
| **Microsoft.Extensions.Identity.Core** | 10.0.10 | `PasswordHasher<Usuario>` (PBKDF2 com salt por usuário), sem subir o ASP.NET Core Identity inteiro |
| **Microsoft.Extensions.Http.Resilience** | 10.8.0 | Wrapper oficial sobre Polly v8: `AddStandardResilienceHandler` dá retry + circuit breaker + timeouts numa linha |
| **Microsoft.Extensions.Caching.Memory** | 10.0.10 | `IMemoryCache` para o Decorator de cache do provedor de clima |
| **FluentValidation** | 12.1.1 | Validação de entrada expressiva, independente de DataAnnotations |
| **xUnit v3** | 3.2.2 | Linha atual do xUnit |
| **Shouldly** | 4.3.0 | Asserções legíveis ([ver justificativa](#3-shouldly-em-vez-de-fluentassertions)) |
| **NSubstitute** | 6.0.0 | Substitutos de teste com sintaxe enxuta, sem `.Object` em toda linha |
| **dotnet-ef** | 10.0.10 | Instalada como **ferramenta local** (`dotnet-tools.json`), não global — quem clona roda `dotnet tool restore` e obtém exatamente a mesma versão |

### Configuração transversal (`Directory.Build.props`)

Centralizar evita repetir cinco vezes e evita drift entre projetos:

- `Nullable=enable` — nullability como parte do contrato dos tipos, não como convenção
- `TreatWarningsAsErrors=true` — **decisão que já se pagou** três vezes: transformou o `NU1903`
  (vulnerabilidade em pacote transitivo) em falha de build, pegou um `InvariantGlobalization`
  incompatível com o `Microsoft.Data.SqlClient`, e forçou tratar `Polly.Timeout.TimeoutRejectedException`
  no provider antes que o código chegasse a produção
- `ImplicitUsings=enable`, `LangVersion=latest`
- `InvariantGlobalization=false` — **obrigatório**: com globalização invariante o
  `Microsoft.Data.SqlClient` aborta no startup com *"Globalization Invariant Mode is not supported"*

---

## Arquitetura

**Clean Architecture pragmática em 4 projetos.** A regra que sustenta tudo é a direção das
dependências: elas só apontam para dentro.

```
┌──────────────────────────────────────────────────────────┐
│  WeatherApp.API            Controllers, Program, DI,     │
│                            JWT, ProblemDetails, CORS,     │
│                            OpenAPI                        │
└───────────────┬──────────────────────────┬───────────────┘
                │                          │
                ▼                          ▼
┌───────────────────────────┐  ┌───────────────────────────┐
│  WeatherApp.Application   │  │ WeatherApp.Infrastructure │
│  Casos de uso, DTOs,      │◄─┤ EF Core, DbContext,       │
│  agregação de previsão    │  │ OpenWeatherMapProvider,   │
│                           │  │ cache, resiliência, JWT   │
└───────────────┬───────────┘  └───────────┬───────────────┘
                │                          │
                ▼                          ▼
┌──────────────────────────────────────────────────────────┐
│  WeatherApp.Domain                                       │
│  Entidades, interfaces (portas), exceções de negócio     │
│  ZERO ProjectReference · ZERO dependência de framework   │
└──────────────────────────────────────────────────────────┘
```

O `Domain` não tem **nenhuma** `ProjectReference` e nenhum `PackageReference`. Não é detalhe
estético: é o que se pode verificar objetivamente para saber se a inversão de dependência é real
ou só um desenho no README.

```bash
dotnet list src/WeatherApp.Domain reference   # deve retornar vazio
```

### Por que 4 camadas e não menos

O domínio aqui é pequeno — duas entidades. A complexidade real não está em regra de negócio, está
na **integração externa**. As camadas existem para isolar exatamente isso: `IWeatherProvider` mora
no `Domain`, a implementação concreta mora na `Infrastructure`, e a `Application` nunca sabe qual
provedor está em uso. Trocar OpenWeatherMap por WeatherAPI, ou EF Core por Dapper, não toca em
regra de negócio.

### Por que NÃO usei CQRS, MediatR, Domain Events ou Specification

Foram avaliados e descartados por **desproporção ao problema**. Esses padrões se pagam em domínios
com múltiplos agregados e invariantes complexas. Aqui teríamos:

- **MediatR/CQRS**: um handler, um command e um request por operação, para orquestrar 2 chamadas
  HTTP e 1 insert. Indireção sem ganho — mais arquivos para ler e um salto de navegação a mais
  para entender qualquer fluxo.
- **Domain Events**: não há reação a mudança de estado que justifique.
- **Specification**: as consultas são `Where` por `UsuarioId` e por nome. `IQueryable` já resolve.

Empilhar padrão para demonstrar repertório é, numa avaliação, sinal de **falta de calibragem entre
solução e problema**. A escolha consciente de não usá-los é a decisão arquitetural mais importante
deste projeto, e é o que a estrutura em camadas já entrega sem eles: testabilidade e inversão de
dependência.

### Controllers em vez de Minimal APIs

O .NET 10 melhorou bastante as Minimal APIs (inclusive validação nativa via `AddValidation()`).
Ainda assim optei por Controllers: agrupam por recurso, deixam `[Authorize]` e
`[ProducesResponseType]` declarativos junto da action, e são mais fáceis de varrer para quem está
revisando o código. É preferência de time, não questão técnica — e vale registrar que
`AddValidation()` do .NET 10 **só funciona em Minimal APIs**, o que é o motivo do
`ValidacaoActionFilter` próprio descrito em [Design patterns aplicados](#design-patterns-aplicados).

---

## Design patterns aplicados

| Padrão | Onde | Por que |
|---|---|---|
| **Adapter** (*anti-corruption layer*) | `IWeatherProvider` → `OpenWeatherMapProvider` | O enunciado permite dois provedores. A interface devolve modelos neutros do domínio (`ClimaAtualBruto`, `PrevisaoBruta`), então **nenhum JSON da OpenWeatherMap atravessa para a Application** |
| **Decorator** | `CachedWeatherProvider` envolvendo `OpenWeatherMapProvider` | Cache transparente para quem consome `IWeatherProvider` — `ClimaService` e `FavoritosService` nem sabem que ele existe. Ver [decisão de cache](#11-cache-decorator-registrado-pelo-tipo-concreto-nunca-singleton) |
| **Repository** | `ICidadeFavoritaRepository`, `IUsuarioRepository` | Abstrai persistência e permite testar serviços sem banco. Detalhe deliberado: **toda** assinatura recebe `usuarioId` — não existe "listar todos" nem "obter por id" sem escopo de usuário, então vazamento de dados entre usuários fica difícil de escrever por acidente |
| **DTO** | `Application/DTOs` | Desacopla o contrato HTTP dos modelos de domínio/EF. Evita que uma mudança de entidade quebre o frontend, e impede vazar campo interno |
| **Options Pattern** | `OpenWeatherMapSettings`, `JwtSettings`, `WeatherCacheSettings`, todos com `ValidateDataAnnotations().ValidateOnStart()` | Configuração tipada, sem *magic strings*. O `ValidateOnStart` é o ponto importante: sem ele, uma `ApiKey`/`Jwt:Chave` ausente só apareceria como erro na primeira requisição — com ele, a aplicação **não sobe** e diz exatamente o que falta |
| **Dependency Injection** | Nativo, em todas as camadas | Inversão de dependência e testabilidade |
| **Exception Handling centralizado** | `IExceptionHandler` → `DomainExceptionHandler` | Um único ponto traduz exceção de domínio em status HTTP. Zero `try/catch` repetido em controller |
| **Unit of Work** (implícito) | `WeatherAppDbContext` | O `SaveChangesAsync` já comita as mudanças de todos os repositórios na mesma transação — inclusive o usuário anônimo criado junto com o primeiro favorito. Formalizar um `IUnitOfWork` separado só se pagaria com múltiplos repositórios em transações distintas — não é o caso |
| **Função pura / Strategy de agregação** | `PrevisaoDiariaAggregator` | A lógica mais delicada do projeto, isolada **sem I/O e sem relógio ambiente** (o "agora" entra por parâmetro). É o que permite testá-la de forma determinística |
| **Retry + Circuit Breaker** | `AddStandardResilienceHandler` no `HttpClient` de `OpenWeatherMapProvider` | Protege contra instabilidade real do provedor. Tuning explícito, não os defaults — ver [decisão 12](#12-circuit-breaker-com-os-defaults-nunca-abre-numa-demonstração) |
| **Validação com filtro próprio** | `ValidacaoActionFilter` + `IValidator<T>` do FluentValidation | `FluentValidation.AspNetCore` está descontinuado e `AddValidation()` do .NET 10 só cobre Minimal APIs — um `IAsyncActionFilter` de ~40 linhas resolve sem dependência extra |

---

## Modelagem de dados

Duas tabelas, criadas por uma **única** migration (`InicialSchema`) — nenhuma fase seguinte
(incluindo o JWT) exigiu alterar o schema.

```
Usuarios                            CidadesFavoritas
─────────────────────────────       ────────────────────────────────────
Id           uniqueidentifier PK    Id          uniqueidentifier PK
Nome         nvarchar(150)          Nome        nvarchar(150)
Email        nvarchar(200) NULL     PaisCodigo  nvarchar(5)     NULL
SenhaHash    nvarchar(300) NULL     Latitude    decimal(9,6)    NULL
DataCriacao  datetime2              Longitude   decimal(9,6)    NULL
                                    UsuarioId   uniqueidentifier FK → Usuarios.Id
UQ_Usuarios_Email (único, FILTRADO) DataCriacao datetime2
  WHERE [Email] IS NOT NULL
                                    UQ_Usuario_Cidade (único: UsuarioId + Nome)
```

### A entidade `Usuario` tem duas variantes na mesma tabela

- **Anônimo** — criado a partir do GUID estável que o frontend guarda no LocalStorage, antes de
  existir autenticação. `Email` e `SenhaHash` são `NULL`.
- **Registrado** — tem e-mail e hash de senha, autentica via JWT.

`Usuario.Promover()` converte um anônimo em registrado **sem trocar o `Id`**. Como
`CidadesFavoritas.UsuarioId` referencia essa PK, favoritos criados antes do login são preservados
automaticamente. Confirmado com `dotnet ef migrations has-pending-model-changes` depois do JWT
entrar: **nenhuma alteração pendente** — a promessa do planejamento se cumpriu.

> ⚠️ **Nuance descoberta na fase 3, vale registrar por não ser óbvia:** com `[Authorize]` agora
> protegendo `FavoritosController`, não é mais possível criar um favorito 100% anônimo *pela API* —
> o pipeline de autenticação barra a requisição antes de chegar no controller. `Promover()` continua
> correto e testado (inclusive [ao vivo](#3-jwt-mapinboundclaims-e-promoção-de-usuário-anônimo)),
> mas hoje ele serve para **dado legado** (um usuário que favoritou cidades numa versão anterior da
> API, antes do JWT existir), não para o fluxo normal desta versão. Documentado aqui porque não dá
> para perceber isso só lendo o código dos dois controllers separadamente.

### Por que o índice único de `Email` é FILTRADO

Este é o detalhe que quebraria a aplicação em produção se passasse batido:

> No SQL Server, um índice `UNIQUE` comum trata `NULL`s como **iguais entre si** e aceita apenas
> **UM** `NULL` na coluna.

Como todo usuário anônimo tem `Email NULL`, o **segundo visitante do site** violaria a constraint.
O filtro `WHERE [Email] IS NOT NULL` remove os `NULL`s do índice: e-mails seguem únicos entre
usuários registrados, e anônimos convivem sem restrição.

Verificado diretamente no banco — dois anônimos inseridos com sucesso, e-mail duplicado
corretamente rejeitado.

> ⚠️ **Efeito colateral prático:** tabelas com índice filtrado exigem `QUOTED_IDENTIFIER ON`.
> O EF Core e o `SqlClient` já fazem isso, mas o `sqlcmd` **não** — para `INSERT` manual use
> `sqlcmd -I`, senão o erro (`Msg 1934`) parece um problema de schema quando não é.

### Por que `Latitude`/`Longitude` são persistidas

São opcionais, mas resolvem dois problemas: desambiguam cidades homônimas e permitem consultar o
provedor por coordenada via `GET /api/clima/coordenadas` (ver [Contrato da API](#contrato-da-api)).
Isso importa porque a OpenWeatherMap marca a **busca por nome como *deprecated*** (funcional, mas
sem correções futuras) — guardar as coordenadas de um favorito é o que permite ao frontend
consultá-lo de volta sem depender do nome digitado pelo usuário.

---

## Contrato da API

Base local: `https://localhost:7061` · `http://localhost:5239`
Documentação navegável: **`/scalar/v1`** · documento OpenAPI: **`/openapi/v1.json`**

### Endpoints implementados

| Método | Rota | Auth | Retorno |
|---|---|---|---|
| `GET` | `/api/clima/{cidade}` | pública | Temperatura atual, condição + ícone, **máx/mín do dia**, umidade |
| `GET` | `/api/clima/{cidade}/previsao` | pública | Exatamente 5 dias: data, máx/mín, condição + ícone |
| `GET` | `/api/clima/coordenadas?latitude=&longitude=` | pública | Mesmo retorno de `/api/clima/{cidade}`, localizando por coordenada |
| `GET` | `/api/clima/coordenadas/previsao?latitude=&longitude=` | pública | Mesmo retorno da previsão, por coordenada |
| `GET` | `/api/favoritos` | **JWT** | Favoritos do usuário autenticado |
| `POST` | `/api/favoritos` | **JWT** | Cria um favorito; valida a cidade no provedor antes de persistir |
| `DELETE` | `/api/favoritos/{id}` | **JWT** | Remove um favorito; `204` |
| `POST` | `/api/auth/register` | pública | Cria conta (ou promove anônimo legado) e devolve token |
| `POST` | `/api/auth/login` | pública | Autentica e devolve token |

`{cidade}` aceita `"Nome"` ou `"Nome,PaisCodigo"` (ex.: `São José do Rio Preto,BR`) para
desambiguar homônimas — `PaisCodigo` é o código ISO do **país** (`BR`), não de estado/UF.

### `GET /api/clima/São José do Rio Preto` → `200`

Resposta real capturada da aplicação em execução:

```json
{
  "cidade": "São José do Rio Preto",
  "paisCodigo": "BR",
  "temperatura": 26.2,
  "sensacaoTermica": 26.2,
  "temperaturaMaxima": 31.62,
  "temperaturaMinima": 22.41,
  "umidade": 40,
  "condicao": "nublado",
  "icone": "04d",
  "iconeUrl": "https://openweathermap.org/img/wn/04d@2x.png",
  "latitude": -20.8197,
  "longitude": -49.3794,
  "dataHoraLocal": "2026-07-30T09:45:55-03:00",
  "fonteMaxMin": "previsao"
}
```

O campo **`fonteMaxMin`** existe para tornar uma limitação explícita em vez de silenciosa:

- `"previsao"` — caminho normal, amplitude real do dia derivada dos blocos de previsão;
- `"leitura-atual"` — degradação, quando a previsão está indisponível ou já não há bloco para hoje
  (consulta no fim do dia local). Os valores ficam bem mais estreitos, e o frontend pode decidir
  como sinalizar.

### `GET /api/clima/São José do Rio Preto/previsao` → `200`

```json
{
  "cidade": "São José do Rio Preto",
  "paisCodigo": "BR",
  "dias": [
    {
      "data": "2026-07-30",
      "temperaturaMaxima": 31.62,
      "temperaturaMinima": 22.41,
      "condicao": "nuvens dispersas",
      "icone": "03d",
      "iconeUrl": "https://openweathermap.org/img/wn/03d@2x.png",
      "probabilidadeChuva": 0
    }
  ]
}
```

`data` é a data **local** da cidade consultada, não UTC. `dias` tem sempre 5 itens em ordem
cronológica (ver [agregação](#2-cinco-dias-a-partir-de-seis-buckets)).

### `GET /api/clima/coordenadas?latitude=-25.4284&longitude=-49.2733` → `200`

Mesmo contrato de `GET /api/clima/{cidade}` — só muda como a localização é resolvida. Útil para
"clima da minha localização atual" no frontend (geolocalização do navegador) e para consultar um
favorito já persistido sem depender do nome digitado pelo usuário, já que `POST /api/favoritos`
guarda `latitude`/`longitude`. `GET /api/clima/coordenadas/previsao` segue o mesmo formato de
`GET /api/clima/{cidade}/previsao`.

`latitude`/`longitude` são obrigatórios e validados (`-90..90` / `-180..180`); ausência ou valor
fora da faixa devolve `400` com `ValidationProblemDetails`. Coordenada sem estação de clima
próxima devolve `404`, igual à busca por nome.

### `POST /api/auth/register` → `200`

```json
// request
{ "nome": "Maria Silva", "email": "maria@teste.com", "senha": "SenhaForte123" }
```
```json
// response — resposta real
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiraEmUtc": "2026-07-30T17:43:19.1831455Z"
}
```

Erros: `409` (`EmailJaCadastradoException`) se o e-mail já existir; `400` `ValidationProblemDetails`
para nome vazio, e-mail inválido ou senha com menos de 8 caracteres.

`POST /api/auth/login` tem o mesmo formato de resposta; `401` genérico (`CredenciaisInvalidasException`)
para e-mail inexistente **ou** senha errada — ver [tabela de mapeamento](#mapeamento-de-exceção--status-http)
para o porquê da mensagem única.

### `POST /api/favoritos` (autenticado) → `201`

```json
// request
{ "nome": "Fortaleza", "paisCodigo": "BR" }
```
```json
// response — resposta real
{
  "id": "325d7e51-d658-435b-8b1e-33e9e085e83d",
  "nome": "Fortaleza",
  "paisCodigo": "BR",
  "latitude": -3.7227,
  "longitude": -38.5247,
  "dataCriacao": "2026-07-30T16:43:30.4372233Z"
}
```

`nome`/`paisCodigo`/`latitude`/`longitude` vêm do que a OpenWeatherMap resolveu para a consulta —
não necessariamente o que o cliente digitou (ver [decisão de nome canônico](#4-nome-canônico-do-provedor-em-vez-do-digitado)).
Duplicata devolve `409`; cidade que o provedor não reconhece devolve `404`; sem o header
`Authorization` devolve `401`.

### Formato de erro

Todos os erros seguem **`ProblemDetails`** (RFC 9457), com `Content-Type: application/problem+json`.
O frontend pode tratar erro de forma uniforme.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Cidade não encontrada",
  "status": 404,
  "detail": "Não foi encontrada nenhuma cidade com o nome 'cidadeinexistente123'.",
  "instance": "/api/clima/cidadeinexistente123",
  "traceId": "0HNNE729CK3LC:00000001"
}
```

`traceId` é incluído em toda resposta de erro para correlacionar com o log do servidor.

> **Atenção, time de frontend:** existem **duas formas** de erro, e a diferença importa.
> Erros de **validação** usam `ValidationProblemDetails`, que tem um campo extra
> `errors` (`{ "campo": ["mensagem"] }`) produzido pelo `ValidacaoActionFilter`/`[ApiController]`.
> Os demais erros usam `ProblemDetails`, **sem** `errors`. Trate `errors` como opcional.
>
> Resposta real de validação (`POST /api/favoritos` com `nome` vazio):
> ```json
> {"title":"Dados inválidos","status":400,"instance":"/api/favoritos","errors":{"Nome":["O nome da cidade é obrigatório.","O nome da cidade deve ter ao menos 2 caracteres."]}}
> ```

### Mapeamento de exceção → status HTTP

Centralizado em `DomainExceptionHandler` — **exceto "cidade não encontrada"**, que deliberadamente
**não** é uma exceção (ver [decisão 16](#16-cidade-não-encontrada-deixou-de-ser-exceção)):

| Exceção de domínio | Status | Raciocínio |
|---|---|---|
| `FavoritoNaoEncontradoException` | `404` | Inexistente **ou de outro usuário** — deliberadamente 404 e não 403: responder 403 confirmaria que aquele `Id` existe, o que é vazamento de informação |
| `FavoritoDuplicadoException` | `409` | Conflito com o estado atual |
| `EmailJaCadastradoException` | `409` | Conflito |
| `CredenciaisInvalidasException` | `401` | Mensagem genérica para e-mail inexistente **e** senha errada — distinguir os casos entregaria um oráculo de enumeração de usuários |
| `UsuarioNaoIdentificadoException` | `401` | Na prática, hoje o middleware `[Authorize]` intercepta antes dessa exceção ser lançada — mantida como defesa em profundidade |
| `ProvedorClimaIndisponivelException` | `503` + `Retry-After: 60` | Indisponibilidade temporária de terceiro (inclusive timeout/circuito aberto pelo Polly — ver [decisão 10](#10-timeoutrejectedexception-e-brokencircuitexception-também-precisam-de-catch)), não erro do cliente |
| `FalhaIntegracaoProvedorException` | `502` | Chave inválida/ausente: **configuração nossa**, não culpa do cliente nem instabilidade do provedor |

"Cidade não encontrada" (`404`) é construída diretamente no controller via `Problem(...)`, com o
mesmo `title`/`detail` de antes — o corpo da resposta não muda, só deixou de passar por uma
exceção lançada e capturada.

---

## Decisões técnicas relevantes

As primeiras são armadilhas reais, descobertas medindo a API e o comportamento do runtime de
verdade — não deduzidas da documentação. Todas estão cobertas por teste para não regredirem.

### 1. `main.temp_max` / `temp_min` **não são** a máxima e a mínima do dia

A própria OpenWeatherMap documenta esses campos de `/data/2.5/weather` como a dispersão de
temperatura **entre estações meteorológicas no instante da leitura**, não a amplitude do dia.

Medições reais feitas contra a API:

| Cidade | `temp_min` / `temp_max` cru | Amplitude | Máx/mín reais do dia |
|---|---|---|---|
| São José do Rio Preto | 23,92 / 23,92 | **0,00 °C** | 22,41 / 31,62 → 9,21 °C |
| Curitiba | 11,47 / 12,32 | 0,85 °C | 11,02 / 21,64 → 10,6 °C |

Para São José do Rio Preto os dois campos vieram **idênticos**. Mapeá-los direto — a leitura óbvia
da API — produziria um card exibindo **"Máx 24° / Mín 24°"**: um bug visível na tela, e exatamente
o tipo de erro que passa em revisão porque o código *parece* certo.

**Solução:** `ClimaService.ObterClimaAtualAsync` chama **os dois** endpoints — `/weather` para
temperatura, condição, ícone e umidade; `/forecast` para derivar a amplitude verdadeira do dia a
partir dos blocos de 3 horas. A temperatura atual entra no cálculo de máx/mín, senão seria possível
exibir "atual 26°, máxima 24°" — inconsistência óbvia para quem está olhando a tela. Coberto por
teste anti-regressão em `ClimaServiceTests` reproduzindo exatamente os valores medidos acima.

O custo da segunda chamada é praticamente nulo graças ao `CachedWeatherProvider` (fase 4), que
compartilha a mesma entrada de cache entre a tela de clima atual e a de previsão — **este é o
argumento real a favor do Decorator neste projeto**, não só um enfeite de padrão.

### 2. Cinco dias a partir de **seis** buckets

`/data/2.5/forecast` devolve 40 blocos de 3 horas (~120 h) começando no próximo slot de 3 h em UTC.
Agrupando por data local, isso cai em **6** datas — não 5 — com cabeça e cauda parciais.

Duas observações reais do mesmo endpoint, mesma cidade, **mesmo dia**, horários diferentes:

```
consulta às 12:00Z  ->  5 / 8 / 8 / 8 / 8 / 3
consulta às 15:00Z  ->  4 / 8 / 8 / 8 / 8 / 4
```

Ou seja: **o recorte varia com a hora da consulta**, e o invariante é sempre haver 6 buckets. Um
`GroupBy` ingênuo devolveria 6 dias, sendo o último uma cauda cujo máx/mín é calculado sobre 3 ou 4
blocos — um número sem sentido apresentado como previsão.

**Algoritmo de `PrevisaoDiariaAggregator`:**

1. converte cada bloco para o fuso da cidade (`dt + city.timezone`) **uma única vez**, carregando o
   instante local junto — reconstruir a hora local depois é fonte clássica de erro de fuso;
2. agrupa por **data local**, descarta datas anteriores a hoje;
3. descarta o **dia corrente** se ele tiver menos de 3 blocos (menos de 9 h de cobertura): numa
   consulta noturna, a "máxima de hoje" sairia de uma janela de 3 horas e enganaria o usuário;
4. recorta em **5** dias;
5. por dia: `Max(temp_max)` e `Min(temp_min)` de todos os blocos daquele dia;
6. ícone representativo: bloco mais próximo do **meio-dia local**, com o sufixo forçado para a
   variante diurna (`10n` → `10d`) — um ícone de chuva noturna num card que resume o dia inteiro
   fica visualmente errado.

Sobre o passo 6, a alternativa avaliada foi usar a condição **modal** (mais frequente) do dia. É
defensável, mas exige uma regra de desempate arbitrária quando duas condições empatam. O critério
do meio-dia é determinístico e corresponde ao que o usuário entende como "o tempo daquele dia".

### 3. JWT: `MapInboundClaims` e promoção de usuário anônimo

Duas decisões da fase 3, testadas ao vivo, não só configuradas:

**`MapInboundClaims = false` dos dois lados (emissão e validação).** `JwtBearerOptions.MapInboundClaims`
tem default `true` e remapeia a claim `sub` para `ClaimTypes.NameIdentifier` — ler
`User.FindFirst("sub")` depois disso retorna `null`. `HttpUsuarioAtualProvider` já lia `sub`
literalmente desde a fase 2 (quando ainda vinha só do header anônimo); manter o mesmo nome de claim
na emissão do token evitou reescrever essa classe.

**Promoção de anônimo→registrado testada com dado legado de verdade.** Como
`[Authorize]` bloqueia a criação de favorito anônimo pela API (ver [nota na modelagem](#a-entidade-usuario-tem-duas-variantes-na-mesma-tabela)),
o teste ao vivo inseriu um usuário anônimo + favorito **direto no banco** via `sqlcmd` (simulando
alguém que usou o app antes do JWT existir) e então chamou `POST /api/auth/register` com o mesmo
GUID no header `X-Usuario-Id`. Resultado: o `sub` do token emitido foi o mesmo GUID do usuário
legado, o favorito apareceu no `GET /api/favoritos` autenticado, e a tabela `Usuarios` ficou com
**uma linha, não duas** — `Promover()` fez `UPDATE`, não `INSERT`. O mesmo cenário está coberto
sem I/O em `AuthServiceTests.RegistrarAsync_promove_usuario_anonimo_preservando_o_id`.

### 4. Nome canônico do provedor em vez do digitado

`FavoritosService.AdicionarAsync` consulta o provedor **antes** de persistir, por duas razões:
valida que a cidade existe (404 em vez de gravar lixo) e captura o nome canônico + coordenadas. Um
usuário que digita `"sao jose do rio preto"` tem o favorito salvo como `"São José do Rio Preto"` —
o que o provedor devolveu, não o que foi digitado. O índice `UQ_Usuario_Cidade` do banco é o
backstop final contra duas requisições concorrentes criando o mesmo favorito: `CidadeFavoritaRepository`
captura a violação (`SqlException` 2601/2627) e traduz para `FavoritoDuplicadoException` (409).

### 5. Agrupar por data **UTC** desloca todos os cards em um dia

Consequência direta de fusos negativos: em São José do Rio Preto (UTC−3), o bloco de `00:00Z` do
dia 31 é **21:00 do dia 30** local. Agrupar pela data UTC joga esse bloco no dia seguinte e
desalinha a previsão inteira — um bug silencioso, que ninguém percebe sem teste porque os números
continuam plausíveis.

O teste `Agrupamento_usa_data_LOCAL_e_nao_UTC` fixa isso escolhendo deliberadamente a asserção que
**discrimina** os dois comportamentos (a mínima do dia difere entre eles; a máxima, não).

### 6. Tratar `404` do provedor **antes** de `EnsureSuccessStatusCode`

Se `EnsureSuccessStatusCode()` roda primeiro, o 404 da OpenWeatherMap vira uma
`HttpRequestException` genérica e o cliente recebe **500** em vez de **404** — falhando exatamente
no requisito de "tratamento de erros". O provider testa o 404 explicitamente antes de qualquer
outra checagem e devolve `null` (ver [decisão 16](#16-cidade-não-encontrada-deixou-de-ser-exceção)
sobre por que não é mais uma exceção).

### 7. `IExceptionHandler` em vez de middleware escrito à mão

O planejamento mencionava as duas opções. Escolhi a abstração nativa (.NET 8+): integra com
`IProblemDetailsService`, é encadeável (vários handlers, em ordem de registro) e dispensa código de
middleware manual.

### 8. `UseExceptionHandler()` registrado **também** em Development

Os exemplos oficiais frequentemente colocam esse registro atrás de `if (!IsDevelopment())`. Isso é
uma armadilha aqui: em Development a *Developer Exception Page* devolveria **HTML**, e o frontend
Angular — que roda justamente contra Development — quebraria ao tentar parsear `ProblemDetails`.
Registrado sempre.

### 9. CORS com `AllowAnyHeader()`

O frontend envia o header customizado `X-Usuario-Id` e, quando autenticado, `Authorization`. Sem
liberar headers customizados, o *preflight* os rejeita e o erro que chega ao browser é um CORS
opaco, difícil de diagnosticar. Origens permitidas ficam em `appsettings.json`
(`Cors:OrigensPermitidas`), não hard-coded.

### 10. `TimeoutRejectedException` e `BrokenCircuitException` também precisam de `catch`

Achado real da fase 4, apontando a `BaseUrl` para uma porta morta. O `catch` original em
`OpenWeatherMapProvider` cobria `HttpRequestException`/`TaskCanceledException` — o que parecia
suficiente. Na prática, quando o Polly interrompe uma chamada por timeout total ou circuito aberto,
ele lança `Polly.Timeout.TimeoutRejectedException`/`Polly.CircuitBreaker.BrokenCircuitException`,
que **não derivam** de nenhuma das duas (a primeira embrulha um `TaskCanceledException` por dentro,
mas o tipo externo é outro). Sem tratá-las, a exceção escapava do handler de domínio e virava
**500** em vez de **503** — só apareceu testando contra um endpoint realmente inacessível, não
lendo a documentação do Polly.

### 11. Cache (Decorator) registrado pelo tipo concreto, nunca Singleton

`CachedWeatherProvider` é exposto como `IWeatherProvider`; o provider real
(`OpenWeatherMapProvider`) é registrado pelo **tipo concreto** via
`AddHttpClient<OpenWeatherMapProvider>`, não por `IWeatherProvider`. Isso é o que garante que só o
decorator seja visível ao resto da aplicação.

**Scoped, nunca Singleton**: `AddHttpClient<T>` registra `T` como *Transient*; se o decorator fosse
Singleton, capturaria para sempre o `HttpClient` transient do provider real (*captive dependency*),
derrotando a rotação de handler do `IHttpClientFactory`. O estado do cache não se perde com isso —
mora no `IMemoryCache`, que é singleton por conta própria.

**Confirmado por contagem real de chamadas**, não só por leitura de código: duas requisições
seguidas a `/api/clima/{mesma cidade}` geram apenas as 2 chamadas HTTP (`/weather` + `/forecast`)
da primeira — a segunda gera zero. TTL de 10 min (clima atual) e 15 min (previsão), configuráveis
em `WeatherCache` no `appsettings.json`. Chave normalizada (sem acento, minúscula, espaços
colapsados) para que `"São Paulo"`, `"sao  paulo"` e `"SAO PAULO"` compartilhem a mesma entrada —
só a chave é normalizada, a chamada ao provedor usa a string original.

### 12. Circuit breaker: com os defaults, nunca abre numa demonstração

`AddStandardResilienceHandler()` sem configuração usa `MinimumThroughput=100` em uma janela de
30 s — inatingível no tráfego de um teste técnico. Tunado explicitamente, e o tuning **também**
exigiu duas correções encontradas testando ao vivo, não deduzidas de antemão:

- **`MinimumThroughput=4` não funcionava.** Com `AttemptTimeout=5s` e `MaxRetryAttempts=3`, uma
  única requisição de entrada só completa **3** tentativas inteiras antes do `TotalRequestTimeout`
  (20 s) cortar a 4ª pela metade — e uma tentativa cortada no meio não conta como falha registrada
  pelo circuito. Ajustado para `MinimumThroughput=3`.
- **`SamplingDuration=10s` (o mínimo exigido pelo validador, `2 × AttemptTimeout`) ainda não abria.**
  Com backoff exponencial entre os 3 retries, as falhas de uma única requisição se espalham por
  quase os 20 s inteiros do `TotalRequestTimeout` — mais que a janela de 10 s aguentava: a 1ª falha
  "saía" da janela deslizante antes da 3ª acontecer. Ajustado para `SamplingDuration=20s`.

Com os dois ajustes, medido ao vivo contra uma `BaseUrl` morta: a 1ª chamada falha em ~20 s (log
mostra `OnCircuitOpened`); a 2ª chamada, imediatamente em seguida, falha em **59 ms** — o circuito
realmente evita bater na rede de novo, em vez de só existir na configuração sem nunca disparar.

### 13. Segredos fora do repositório

`ApiKey` e chave JWT vivem em **`dotnet user-secrets`** (`%APPDATA%\Microsoft\UserSecrets\`), nunca
em `appsettings.json`. O `appsettings.json` versionado contém só `BaseUrl`, unidades, idioma e TTLs.

Como user-secrets **só é carregado em Development**, `ValidateOnStart()` é o que evita o pior modo
de falha: subir em outro ambiente sem a chave e só descobrir na primeira requisição.

### 14. `q={cidade}` em vez de geocodificar com `/geo/1.0/direct`

Metade das chamadas e um único ponto de tratamento de "não encontrado". A busca por nome está
marcada como *deprecated* pela OpenWeatherMap (funcional, sem correções futuras); a mitigação é
persistir `lat`/`lon`. `/geo/1.0/direct` seria o caminho certo se houvesse requisito de
autocomplete ou desambiguação de homônimas — não há.

### 15. `Microsoft.OpenApi` fixado em 2.7.5

`Microsoft.AspNetCore.OpenApi 10.0.10` traz transitivamente `Microsoft.OpenApi 2.0.0`, afetada por
**CVE-2026-49451** (recursão descontrolada ao *parsear* documentos OpenAPI). Corrigido a partir da
2.7.5.

Fixei na **menor** versão corrigida, não na última 2.x: a `10.0.10` foi compilada contra a `2.0.0`,
então quanto menor o salto, menor o risco de divergência de API. Não subir para a 3.x, que esta
versão do ASP.NET Core não espera.

O impacto real aqui é nulo — a API **gera** documento, não parseia documento de terceiro. O motivo
de corrigir é outro: `TreatWarningsAsErrors` transforma `NU1903` em erro de build, e quem clonar o
repositório precisa de um `restore` limpo.

### 16. "Cidade não encontrada" deixou de ser exceção

Até aqui, um 404 do provedor virava `CidadeNaoEncontradaException`, capturada pelo
`DomainExceptionHandler` e traduzida em `404 ProblemDetails`. Funcionalmente correto — testado e
documentado — mas usar exceção para um resultado de negócio **esperado e comum** (usuário digita
algo que o provedor não reconhece) tem custo real: a exceção atravessa 3 camadas
(Infrastructure → Application → API) só para virar, no fim, um retorno de valor simples.

Refatorado para fluxo de retorno normal, de baixo para cima:

- `IWeatherProvider.ObterClimaAtualAsync`/`ObterPrevisaoAsync` (Domain) passam a devolver `T?` —
  `null` quando o provedor não reconhece a cidade;
- `OpenWeatherMapProvider` devolve `null` ao ver 404, em vez de lançar;
- `CachedWeatherProvider` propaga o `null` sem cachear (um typo do usuário não deve continuar
  "não encontrado" pelo TTL inteiro depois de corrigido);
- `ClimaService`/`FavoritosService` propagam `null` até o controller;
- `ClimaController`/`FavoritosController` checam `null` e chamam `Problem(...)` diretamente —
  **mesmo `title`/`detail`** que a exceção produzia, então o corpo da resposta na rede não mudou,
  só o caminho em C# que chega até ele.

**Efeito colateral encontrado ao verificar a correção, não previsto de antemão:** `Problem()`
retorna um `ActionResult` que passa pela negociação de conteúdo normal do MVC — diferente do
`IExceptionHandler`, que escreve direto na resposta via `IProblemDetailsService`, contornando essa
negociação. Como `ClimaController`/`FavoritosController` tinham `[Produces("application/json")]`,
essa anotação **sobrepunha** o `application/problem+json` que o `Problem()` tentava aplicar — o
erro passou a sair como `application/json` comum. `[Produces("application/json")]` era redundante
(não há outro formatter registrado) e foi removido dos três controllers; sem ela, a negociação de
conteúdo escolhe `application/problem+json` para erros e `application/json` para sucesso,
corretamente.

**Deliberadamente fora de escopo desta mudança:** `FavoritoDuplicadoException` (409),
`FavoritoNaoEncontradoException` (404 na remoção de favorito) e as exceções de auth continuam
exceções. São conflitos ou falhas reais de configuração/integração — diferente de "não encontrado",
que é uma saída de negócio comum. Convertê-las também seria replicar a mudança sem necessidade
relatada.

### 17. Busca por coordenada como rota literal irmã de `{cidade}`

`GET /api/clima/coordenadas` e `GET /api/clima/coordenadas/previsao`, em vez de sobrecarregar
`{cidade}` com um formato tipo `"lat,lon"`. Duas rotas com contratos de entrada diferentes (string
vs. par de números validados) ficam mais claras como endpoints distintos, e o roteamento do
ASP.NET Core já resolve a ambiguidade: um segmento literal (`coordenadas`) tem precedência sobre um
parâmetro (`{cidade}`) na mesma posição, então `GET /api/clima/coordenadas` nunca cai na rota de
nome (verificado ao vivo — `/api/clima/Curitiba` e `/api/clima/coordenadas?...` coexistem sem
conflito).

Reaproveita toda a pilha existente: mesmo `IWeatherProvider` (dois métodos novos,
`ObterClimaAtualPorCoordenadasAsync`/`ObterPrevisaoPorCoordenadasAsync`), mesmo
`CachedWeatherProvider` (chave de cache própria, coordenada arredondada a 4 casas decimais — 
~11 m de precisão, o bastante para duas leituras de GPS da mesma cidade caírem na mesma entrada),
mesma resiliência HTTP, e o mesmo `ClimaService` — a lógica de derivar máxima/mínima do dia e de
degradar quando a previsão falha foi extraída para um método que recebe a chamada ao provedor como
`Func`, em vez de duplicá-la para o caminho por coordenada.

Validação de `latitude`/`longitude` (obrigatórios, faixas `-90..90`/`-180..180`) segue o mesmo
padrão de `CriarFavoritoRequestValidator`: um `IValidator<T>` do FluentValidation rodado pelo
`ValidacaoActionFilter` já registrado globalmente — nenhuma peça nova de infraestrutura.

---

## Estratégia de testes

**43 testes, todos passando.** `dotnet test`

```
tests/WeatherApp.Application.Tests/
├── Clima/PrevisaoDiariaAggregatorTests.cs   (17 testes)
└── Services/
    ├── ClimaServiceTests.cs                  (10 testes)
    ├── FavoritosServiceTests.cs               (7 testes)
    └── AuthServiceTests.cs                    (6 testes)
```

### Por que a cobertura está concentrada no agregador e nos serviços

Cobertura uniforme não é o objetivo — cobrir onde o **risco** está, é. Neste projeto:

- controllers só delegam (um `Ok(await ...)`), verificados ao vivo, não por teste de unidade;
- repositórios são `Where` + `SaveChanges`, verificados contra o SQL Server real durante o
  desenvolvimento de cada fase;
- o provider é mapeamento de campo, também verificado ao vivo;
- **o agregador e os serviços concentram toda a lógica condicional do sistema**, e são o único
  lugar em que um erro passa silenciosamente porque os números continuam plausíveis.

Por isso `PrevisaoDiariaAggregator` foi escrito como **função pura**: sem I/O, sem `DateTime.UtcNow`
interno (o "agora" entra por parâmetro). É o que torna possível testar fuso, virada de dia e
recorte de forma determinística, sem mock de HTTP e sem banco.

### O que está coberto

**Agregação de previsão** (`PrevisaoDiariaAggregatorTests`): 40 blocos → 6 datas locais nas duas
distribuições reais observadas; recorte em exatamente 5 dias; cauda parcial descartada; agrupamento
por data **local**, não UTC; máx/mín por dia a partir dos blocos; dia corrente descartado com
cobertura insuficiente; dias passados ignorados; ícone do meio-dia local com conversão
noturno→diurno; fuso fracionário (Índia, +05:30); entradas degeneradas (lista vazia, poucos dias).

**`ClimaService`**: máx/mín vêm do forecast e não de `main.temp_max`/`temp_min` (**anti-regressão**
com os valores reais medidos — 23,92/23,92 → 22,41/31,62); temperatura atual entra no cálculo de
máx/mín; cidade não encontrada devolve `null` sem chamar a previsão (ver
[decisão 16](#16-cidade-não-encontrada-deixou-de-ser-exceção)); degradação para
`fonteMaxMin: "leitura-atual"` quando a previsão falha, não tem bloco para hoje, ou (caso raro)
não encontra a mesma cidade que o clima atual já resolveu; previsão delega corretamente para o
aggregator via `TimeProvider` controlado.

**`FavoritosService`**: duplicata não chega a consultar o provedor; cidade inexistente não persiste;
usuário anônimo é garantido antes do favorito; nome **canônico** do provedor é o que é salvo, não o
digitado; remover favorito ausente/de outro usuário lança 404; remover o próprio favorito funciona;
listagem retorna só os do usuário atual.

**`AuthService`**: e-mail duplicado bloqueia o registro; **promoção de anônimo preserva o `Id`**
(o cenário validado ao vivo com dado legado, [decisão 3](#3-jwt-mapinboundclaims-e-promoção-de-usuário-anônimo));
registro sem anônimo prévio cria usuário novo; login com e-mail inexistente ou senha errada lança
a mesma exceção (`CredenciaisInvalidasException`); login correto devolve token.

### Ferramentas e por quê

- **xUnit v3 3.2.2** — o template in-box do SDK 10 ainda gera xUnit v2 (2.9.3); troquei para a
  linha atual. Exige `OutputType=Exe` no `.csproj`, porque a v3 roda os testes como executável.
  O analisador `xUnit1051` pede `TestContext.Current.CancellationToken` explícito em chamadas
  assíncronas — mais um sinal de que é uma linha ativamente mantida, não só "mais nova".
- **Shouldly** — asserções legíveis. Ver [justificativa da troca](#3-shouldly-em-vez-de-fluentassertions).
- **NSubstitute** — `Arg.Is<T>` tipa o parâmetro do predicado como `T?` (reflete que um argumento
  real pode chegar nulo em runtime mesmo com nullable reference types); com `Nullable=enable` isso
  exige `!` explícito dentro do lambda quando se sabe que o valor não será nulo.

---

## Desvios deliberados do planejamento

O planejamento original sugeria outras escolhas nestes pontos. Registro o motivo de cada mudança:

### 1. Scalar em vez de Swagger UI

O .NET 10 **gera** o documento OpenAPI nativamente (`Microsoft.AspNetCore.OpenApi`) mas **não traz
UI** — os templates deixaram de incluir Swashbuckle desde o .NET 9. `Scalar.AspNetCore` fornece a
UI navegável em uma linha, consome direto o documento nativo e já mostra o botão "Authorize" para
o Bearer token (via `BearerSecuritySchemeTransformer`, um `IOpenApiDocumentTransformer` de ~25
linhas).

Se a expectativa for literalmente "Swagger", `Swashbuckle.AspNetCore.SwaggerUI` (só o middleware de
UI, sem o gerador) apontado para `/openapi/v1.json` entrega a interface familiar sem reintroduzir o
gerador antigo. É uma troca de duas linhas.

### 2. `IExceptionHandler` em vez de `ExceptionHandlingMiddleware`

O planejamento citava as duas abordagens em seções diferentes. Ver
[decisão 7](#7-iexceptionhandler-em-vez-de-middleware-escrito-à-mão).

### 3. Shouldly em vez de FluentAssertions

**FluentAssertions 8.x passou a exigir licença comercial** (Xceed) para uso não-OSS. Num repositório
de candidatura isso é um risco desnecessário — inclusive porque quem avalia pode ser exatamente
alguém que sabe disso. Shouldly 4.3.0 é livre.

Alternativas equivalentes, se a sintaxe do FluentAssertions for preferida: fixar `FluentAssertions
7.x` (última linha sob a licença antiga) ou usar `AwesomeAssertions` (fork Apache-2.0 da 7.x).

### 4. Solution em formato `.sln`, não `.slnx`

O SDK 10 gera **`.slnx` por default** (`dotnet new sln --format` tem `slnx` como padrão). O formato
XML é mais novo e Visual Studio mais antigo não o abre. Para um teste técnico que será aberto na
máquina de outra pessoa, `.sln` é a escolha segura.

### 5. SQL Server Express local em vez de `docker-compose`

Docker não estava disponível na máquina de desenvolvimento. Preferi **não** versionar um
`docker-compose.yml` que nunca foi executado — um arquivo de infraestrutura não testado é pior que
sua ausência, porque cria falsa confiança.

### 6. `FavoritosController` protegido por `[Authorize]`, não por header opcional

O planejamento descrevia o JWT como bônus explícito, mas não detalhava se favoritos ficariam
acessíveis anonimamente **e** autenticados, ou só autenticados após a fase 3. Optei por proteger
totalmente com `[Authorize]`: é a leitura mais direta de "JWT + restrição de endpoints
autenticados" no enunciado. A consequência (documentada na [modelagem de dados](#a-entidade-usuario-tem-duas-variantes-na-mesma-tabela))
é que a promoção de usuário anônimo passa a servir só para dado legado — um trade-off que preferi
assumir e explicar a deixar implícito.

---

## Como rodar

### Pré-requisitos

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (desenvolvido com 10.0.302)
- SQL Server (testado em **SQL Server 2022 Express**, instância `.\SQLEXPRESS`, autenticação Windows)
- Uma [chave da OpenWeatherMap](https://home.openweathermap.org/api_keys) (plano free)

> ⏱️ **Chave nova pode levar até ~2 horas para ativar.** Até lá, todas as chamadas retornam `401`
> e a API responde `502`. Não é bug de código — gere a chave antes de começar.

### 1. Restaurar dependências e a ferramenta local do EF

```bash
dotnet restore
dotnet tool restore          # instala dotnet-ef 10.0.10 conforme dotnet-tools.json
```

### 2. Configurar os segredos

Nunca vão para o `appsettings.json`:

```bash
dotnet user-secrets set "OpenWeatherMap:ApiKey" "SUA_CHAVE_AQUI" --project src/WeatherApp.API
```

Chave de assinatura do JWT (mínimo de 32 bytes; abaixo, 48):

```powershell
# PowerShell — gera 48 bytes aleatórios em Base64
$b = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
dotnet user-secrets set "Jwt:Chave" ([Convert]::ToBase64String($b)) --project src/WeatherApp.API
```

Conferir o que está armazenado (fica em `%APPDATA%\Microsoft\UserSecrets\`, fora do repositório):

```bash
dotnet user-secrets list --project src/WeatherApp.API
```

### 3. Ajustar a connection string (se necessário)

`src/WeatherApp.API/appsettings.json` — o default aponta para a instância local:

```
Server=.\SQLEXPRESS;Database=WeatherApp;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Application Name=WeatherApp
```

Dois pontos que costumam causar dor de cabeça:

- **`TrustServerCertificate=True` é obrigatório**: o `Microsoft.Data.SqlClient` 6.x usa
  `Encrypt=true` por default e o certificado do SQL Express é autoassinado.
- **`Application Name` explícito**: o EF Core 10 injeta um valor próprio se você não definir.

Usando autenticação SQL em vez de Windows, troque `Trusted_Connection=True` por
`User Id=<usuário>;Password=<senha>`.

### 4. Criar o banco

```bash
dotnet ef database update --project src/WeatherApp.Infrastructure --startup-project src/WeatherApp.API
```

### 5. Rodar

```bash
dotnet run --project src/WeatherApp.API
```

- API: `https://localhost:7061`
- Documentação navegável: `https://localhost:7061/scalar/v1`

### 6. Testes

```bash
dotnet test
```

### Verificação rápida ponta a ponta (PowerShell)

```powershell
$b = "https://localhost:7061/api"

# clima atual — máx/mín devem ser uma amplitude diária real, não valores quase iguais
Invoke-RestMethod "$b/clima/São José do Rio Preto"

# previsão — deve retornar exatamente 5
(Invoke-RestMethod "$b/clima/São José do Rio Preto/previsao").dias.Count

# erro padronizado — 404 em application/problem+json, não 500
curl.exe -sk "$b/clima/cidadeinexistente123"

# fluxo de auth + favoritos
$token = (Invoke-RestMethod "$b/auth/register" -Method Post -ContentType 'application/json; charset=utf-8' `
    -Body '{"nome":"Teste","email":"teste@teste.com","senha":"SenhaForte123"}').token
$h = @{ Authorization = "Bearer $token" }

Invoke-RestMethod "$b/favoritos" -Headers $h -Method Post -ContentType 'application/json; charset=utf-8' `
    -Body '{"nome":"Fortaleza","paisCodigo":"BR"}'
Invoke-RestMethod "$b/favoritos" -Headers $h                      # deve listar Fortaleza
curl.exe -sk -w "`nHTTP %{http_code}`n" "$b/favoritos"             # sem token -> 401
```

> Em PowerShell 5.1, `curl` é **alias de `Invoke-WebRequest`** e não aceita flags do curl real —
> use `curl.exe` explicitamente. `-SkipHttpErrorCheck` também não existe nessa versão; para
> inspecionar 4xx/5xx, use `try/catch` ou `curl.exe -i`.

---

## Fora de escopo / evolução futura

Avaliado e deliberadamente deixado de fora, por não fazer parte do enunciado:

- **Refresh token** — o JWT expira (`Jwt:MinutosExpiracao`, default 60 min) e exige novo login.
  Razoável para o escopo do teste; um refresh token adicionaria um fluxo e uma tabela só para isso.
- **`HybridCache`** (.NET 9+) no lugar de `IMemoryCache` — tem proteção nativa contra *cache
  stampede*: hoje, N requisições concorrentes para a mesma cidade fria disparam N chamadas ao
  provedor, porque `IMemoryCache.GetOrCreateAsync` não serializa esse caso. Não é um problema
  visível no volume de um teste técnico, mas seria a primeira melhoria de cache num cenário real.
- **One Call API 3.0** da OpenWeatherMap — entrega máx/mín diários nativos, eliminando a
  necessidade de derivar da previsão. Exige assinatura com cartão de crédito mesmo no tier
  gratuito de 1.000 chamadas/dia, inviável para este contexto.
- **`/geo/1.0/direct`** para autocomplete/desambiguação de cidades homônimas na busca — não há
  esse requisito hoje; a mitigação atual (`"Cidade,PaisCodigo"` + coordenadas persistidas nos
  favoritos) cobre o caso necessário sem a chamada extra.
