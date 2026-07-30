# MyPersonalWeatherApp — Backend

API REST em **.NET 10** para consulta de clima e previsão do tempo, integrando com a
**OpenWeatherMap** e persistindo cidades favoritas em **SQL Server**.

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
- [Próximos passos](#próximos-passos)

---

## Status de implementação

Documentar isto explicitamente é proposital: é mais útil saber exatamente onde o projeto está
do que ler uma lista de recursos que não existem.

| Fase | Entrega | Status |
|---|---|:--:|
| 0 | Estrutura da solution, 4 camadas, configuração, segredos | ✅ |
| 1 | Domínio, EF Core + migration, integração OpenWeatherMap, clima atual | ✅ |
| 2 | Previsão de 5 dias | ✅ |
| 2 | CRUD de favoritos, validação, usuário implícito | ⬜ |
| 3 | JWT (bônus) | ⬜ |
| 4 | Cache (Decorator) + resiliência (Polly) | ⬜ |
| 5 | Testes de serviços, OpenAPI completo | 🟡 parcial |

**Funcionando hoje, verificado ponta a ponta:** `GET /api/clima/{cidade}` e
`GET /api/clima/{cidade}/previsao`, tratamento de erros via `ProblemDetails`, documentação
OpenAPI navegável, banco criado por migration, 19 testes unitários passando.

**Transparência sobre pacotes declarados antecipadamente:** `Microsoft.Extensions.Http.Resilience`,
`Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Identity.Core`,
`Microsoft.IdentityModel.JsonWebTokens` e `FluentValidation` já estão referenciados nos `.csproj`
porque as versões foram todas validadas de uma vez na fase 0, mas **ainda não estão em uso** —
entram nas fases 2 a 4. Preferi declarar isso a deixar o leitor descobrir sozinho.

---

## Stack e tecnologias

| Tecnologia | Versão | Por que |
|---|---|---|
| **.NET / ASP.NET Core** | 10.0 (`net10.0`) | Versão pedida no enunciado; SDK 10.0.302 |
| **C#** | `latest` | `record`, primary constructors, collection expressions — reduzem cerimônia em DTOs e entidades |
| **Entity Framework Core** | 10.0.10 | Produtividade em CRUD e, principalmente, **migrations versionadas**: o schema nasce do código e é reproduzível por quem clona o repositório |
| **SQL Server Express** | 2022 | Exigido pelo enunciado. Docker não estava disponível na máquina de desenvolvimento, então a instância local substitui o `docker-compose` sugerido no planejamento |
| **Microsoft.AspNetCore.OpenApi** | 10.0.10 | Geração nativa do documento OpenAPI no .NET 10 |
| **Scalar.AspNetCore** | 2.16.16 | UI navegável para o documento OpenAPI ([ver justificativa](#1-scalar-em-vez-de-swagger-ui)) |
| **xUnit v3** | 3.2.2 | Linha atual do xUnit |
| **Shouldly** | 4.3.0 | Asserções legíveis ([ver justificativa](#3-shouldly-em-vez-de-fluentassertions)) |
| **NSubstitute** | 6.0.0 | Substitutos de teste com sintaxe enxuta, sem `.Object` em toda linha |
| **dotnet-ef** | 10.0.10 | Instalada como **ferramenta local** (`dotnet-tools.json`), não global — quem clona roda `dotnet tool restore` e obtém exatamente a mesma versão |

### Configuração transversal (`Directory.Build.props`)

Centralizar evita repetir cinco vezes e evita drift entre projetos:

- `Nullable=enable` — nullability como parte do contrato dos tipos, não como convenção
- `TreatWarningsAsErrors=true` — **decisão que já se pagou**: foi ela que transformou o aviso
  `NU1903` (vulnerabilidade em pacote transitivo) em falha de build, forçando a correção
  imediatamente em vez de deixá-la passar silenciosamente
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
│                            ProblemDetails, CORS, OpenAPI │
└───────────────┬──────────────────────────┬───────────────┘
                │                          │
                ▼                          ▼
┌───────────────────────────┐  ┌───────────────────────────┐
│  WeatherApp.Application   │  │ WeatherApp.Infrastructure │
│  Casos de uso, DTOs,      │◄─┤ EF Core, DbContext,       │
│  agregação de previsão    │  │ OpenWeatherMapProvider    │
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
`AddValidation()` do .NET 10 **só funciona em Minimal APIs**, o que muda a estratégia de validação
da fase 2 (ver [próximos passos](#próximos-passos)).

---

## Design patterns aplicados

| Padrão | Onde | Por que |
|---|---|---|
| **Adapter** (*anti-corruption layer*) | `IWeatherProvider` → `OpenWeatherMapProvider` | O enunciado permite dois provedores. A interface devolve modelos neutros do domínio (`ClimaAtualBruto`, `PrevisaoBruta`), então **nenhum JSON da OpenWeatherMap atravessa para a Application** |
| **Repository** | `ICidadeFavoritaRepository`, `IUsuarioRepository` | Abstrai persistência e permite testar serviços sem banco. Detalhe deliberado: **toda** assinatura recebe `usuarioId` — não existe "listar todos" nem "obter por id" sem escopo de usuário, então vazamento de dados entre usuários fica difícil de escrever por acidente |
| **DTO** | `Application/DTOs` | Desacopla o contrato HTTP dos modelos de domínio/EF. Evita que uma mudança de entidade quebre o frontend, e impede vazar campo interno |
| **Options Pattern** | `OpenWeatherMapSettings` com `ValidateDataAnnotations().ValidateOnStart()` | Configuração tipada, sem *magic strings*. O `ValidateOnStart` é o ponto importante: sem ele, uma `ApiKey` ausente só apareceria como 401 do provedor na primeira requisição — com ele, a aplicação **não sobe** e diz exatamente o que falta |
| **Dependency Injection** | Nativo, em todas as camadas | Inversão de dependência e testabilidade |
| **Exception Handling centralizado** | `IExceptionHandler` → `DomainExceptionHandler` | Um único ponto traduz exceção de domínio em status HTTP. Zero `try/catch` repetido em controller |
| **Unit of Work** (implícito) | `WeatherAppDbContext` | O `SaveChangesAsync` já comita as mudanças de todos os repositórios na mesma transação. Formalizar um `IUnitOfWork` separado só se pagaria com múltiplos repositórios em transações distintas — não é o caso |
| **Função pura / Strategy de agregação** | `PrevisaoDiariaAggregator` | A única lógica não-trivial do projeto, isolada **sem I/O e sem relógio ambiente** (o "agora" entra por parâmetro). É o que permite testá-la de forma determinística |

**Previstos para as próximas fases:** **Decorator** (`CachedWeatherProvider` envolvendo o provider
real, com `IMemoryCache`) e **Retry/Circuit Breaker** (via `AddStandardResilienceHandler`).

---

## Modelagem de dados

Duas tabelas, criadas pela migration `InicialSchema`.

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
- **Registrado** — tem e-mail e hash de senha.

`Usuario.Promover()` converte um anônimo em registrado **sem trocar o `Id`**. Como
`CidadesFavoritas.UsuarioId` referencia essa PK, **os favoritos criados antes do login são
preservados automaticamente** e a entrada do JWT na fase 3 **não exigirá nenhuma migration de
schema** — que é exatamente a promessa feita no planejamento.

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
provedor por coordenada. Isso importa porque a OpenWeatherMap marca a **busca por nome como
*deprecated*** (funcional, mas sem correções futuras) — guardar as coordenadas é a mitigação que
permite migrar sem tocar em regra de negócio.

---

## Contrato da API

Base local: `https://localhost:7061` · `http://localhost:5239`
Documentação navegável: **`/scalar/v1`** · documento OpenAPI: **`/openapi/v1.json`**

### Endpoints implementados

| Método | Rota | Auth | Retorno |
|---|---|---|---|
| `GET` | `/api/clima/{cidade}` | pública | Temperatura atual, condição + ícone, **máx/mín do dia**, umidade |
| `GET` | `/api/clima/{cidade}/previsao` | pública | Exatamente 5 dias: data, máx/mín, condição + ícone |

`{cidade}` aceita `"Nome"` ou `"Nome,PaisCodigo"` (ex.: `São José do Rio Preto,BR`) para
desambiguar homônimas.

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
> Erros de **validação** (fase 2) usarão `ValidationProblemDetails`, que tem um campo extra
> `errors` (`{ "campo": ["mensagem"] }`) produzido automaticamente pelo `[ApiController]`.
> Os demais erros usam `ProblemDetails`, **sem** `errors`. Trate `errors` como opcional.

### Mapeamento de exceção → status HTTP

Centralizado em `DomainExceptionHandler`:

| Exceção de domínio | Status | Raciocínio |
|---|---|---|
| `CidadeNaoEncontradaException` | `404` | Recurso inexistente |
| `FavoritoNaoEncontradoException` | `404` | Inexistente **ou de outro usuário** — deliberadamente 404 e não 403: responder 403 confirmaria que aquele `Id` existe, o que é vazamento de informação |
| `FavoritoDuplicadoException` | `409` | Conflito com o estado atual |
| `EmailJaCadastradoException` | `409` | Conflito |
| `CredenciaisInvalidasException` | `401` | Mensagem genérica para e-mail inexistente **e** senha errada — distinguir os casos entregaria um oráculo de enumeração de usuários |
| `UsuarioNaoIdentificadoException` | `400` | Falta identificação (passa a `401` quando o JWT entrar) |
| `ProvedorClimaIndisponivelException` | `503` + `Retry-After: 60` | Indisponibilidade temporária de terceiro, não erro do cliente |
| `FalhaIntegracaoProvedorException` | `502` | Chave inválida/ausente: **configuração nossa**, não culpa do cliente nem instabilidade do provedor |

---

## Decisões técnicas relevantes

As três primeiras são armadilhas reais, descobertas medindo a API de verdade — não deduzidas da
documentação. Todas estão cobertas por teste para não regredirem.

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
exibir "atual 26°, máxima 24°" — inconsistência óbvia para quem está olhando a tela.

O custo da segunda chamada será praticamente nulo quando o `CachedWeatherProvider` entrar (fase 4),
porque as duas telas compartilham a mesma entrada de cache da previsão. **Este é o argumento real a
favor do Decorator neste projeto** — ele deixa de ser enfeite e passa a viabilizar a correção.

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

### 3. Agrupar por data **UTC** desloca todos os cards em um dia

Consequência direta de fusos negativos: em São José do Rio Preto (UTC−3), o bloco de `00:00Z` do
dia 31 é **21:00 do dia 30** local. Agrupar pela data UTC joga esse bloco no dia seguinte e
desalinha a previsão inteira — um bug silencioso, que ninguém percebe sem teste porque os números
continuam plausíveis.

O teste `Agrupamento_usa_data_LOCAL_e_nao_UTC` fixa isso escolhendo deliberadamente a asserção que
**discrimina** os dois comportamentos (a mínima do dia difere entre eles; a máxima, não).

### 4. Tratar `404` do provedor **antes** de `EnsureSuccessStatusCode`

Se `EnsureSuccessStatusCode()` roda primeiro, o 404 da OpenWeatherMap vira uma
`HttpRequestException` genérica e o cliente recebe **500** em vez de **404** — falhando exatamente
no requisito de "tratamento de erros". O provider testa o 404 explicitamente e o converte em
`CidadeNaoEncontradaException`.

### 5. `IExceptionHandler` em vez de middleware escrito à mão

O planejamento mencionava as duas opções. Escolhi a abstração nativa (.NET 8+): integra com
`IProblemDetailsService`, é encadeável (vários handlers, em ordem de registro) e dispensa código de
middleware manual.

### 6. `UseExceptionHandler()` registrado **também** em Development

Os exemplos oficiais frequentemente colocam esse registro atrás de `if (!IsDevelopment())`. Isso é
uma armadilha aqui: em Development a *Developer Exception Page* devolveria **HTML**, e o frontend
Angular — que roda justamente contra Development — quebraria ao tentar parsear `ProblemDetails`.
Registrado sempre.

### 7. CORS com `AllowAnyHeader()`

O frontend enviará o header customizado `X-Usuario-Id` (fase 2). Sem liberá-lo, o *preflight* o
rejeita e o erro que chega ao browser é um CORS opaco, difícil de diagnosticar. Origens permitidas
ficam em `appsettings.json` (`Cors:OrigensPermitidas`), não hard-coded.

### 8. Segredos fora do repositório

`ApiKey` e chave JWT vivem em **`dotnet user-secrets`** (`%APPDATA%\Microsoft\UserSecrets\`), nunca
em `appsettings.json`. O `appsettings.json` versionado contém só `BaseUrl`, unidades, idioma e TTLs.

Como user-secrets **só é carregado em Development**, `ValidateOnStart()` é o que evita o pior modo
de falha: subir em outro ambiente sem a chave e só descobrir na primeira requisição.

### 9. `q={cidade}` em vez de geocodificar com `/geo/1.0/direct`

Metade das chamadas e um único ponto de tratamento de "não encontrado". A busca por nome está
marcada como *deprecated* pela OpenWeatherMap (funcional, sem correções futuras); a mitigação é
persistir `lat`/`lon`. `/geo/1.0/direct` seria o caminho certo se houvesse requisito de
autocomplete ou desambiguação de homônimas — não há.

### 10. `Microsoft.OpenApi` fixado em 2.7.5

`Microsoft.AspNetCore.OpenApi 10.0.10` traz transitivamente `Microsoft.OpenApi 2.0.0`, afetada por
**CVE-2026-49451** (recursão descontrolada ao *parsear* documentos OpenAPI). Corrigido a partir da
2.7.5.

Fixei na **menor** versão corrigida, não na última 2.x: a `10.0.10` foi compilada contra a `2.0.0`,
então quanto menor o salto, menor o risco de divergência de API. Não subir para a 3.x, que esta
versão do ASP.NET Core não espera.

O impacto real aqui é nulo — a API **gera** documento, não parseia documento de terceiro. O motivo
de corrigir é outro: `TreatWarningsAsErrors` transforma `NU1903` em erro de build, e quem clonar o
repositório precisa de um `restore` limpo.

---

## Estratégia de testes

**19 testes, todos passando.** `dotnet test`

```
tests/WeatherApp.Application.Tests/
└── Clima/PrevisaoDiariaAggregatorTests.cs
```

### Por que a cobertura está concentrada no agregador

Cobertura uniforme não é o objetivo — cobrir onde o **risco** está, é. Neste projeto:

- controllers só delegam (um `Ok(await ...)`);
- repositórios são `Where` + `SaveChanges`;
- o provider é mapeamento de campo, verificável rodando a aplicação;
- **o agregador concentra praticamente toda a lógica condicional do sistema**, e é o único ponto em
  que um erro passa silenciosamente porque os números continuam plausíveis.

Por isso ele foi escrito como **função pura**: sem I/O, sem `DateTime.UtcNow` interno (o "agora"
entra por parâmetro). É o que torna possível testar fuso, virada de dia e recorte de forma
determinística, sem mock de HTTP e sem banco.

### O que está coberto

| Cenário | Por que importa |
|---|---|
| 40 blocos caem em 6 datas locais — nas duas distribuições reais (12:00Z e 15:00Z) | Sanidade da premissa; garante que a agregação não dependa de um split específico |
| Retorna exatamente 5 dias, nos dois formatos | Requisito do enunciado |
| Datas locais consecutivas começando hoje | Ordem e continuidade |
| Cauda parcial do 6º dia é descartada | O bug do `GroupBy` ingênuo |
| **Agrupamento por data local, não UTC** | Deslocamento de um dia em fuso negativo |
| Máx/mín do dia vêm dos blocos daquele dia | Correção do cálculo |
| Dia corrente descartado com < 3 blocos / mantido com cobertura | Consulta noturna |
| Dias anteriores a hoje são ignorados | Filtro temporal |
| Ícone do meio-dia local; noturno → diurno | Coerência visual |
| Fuso **fracionário** (+05:30, Índia) | Garante que a lógica não assume offsets múltiplos de 1 h |
| Lista vazia; menos de 5 dias disponíveis | Não estourar em entrada degenerada |

### Ferramentas e por quê

- **xUnit v3 3.2.2** — o template in-box do SDK 10 ainda gera xUnit v2 (2.9.3); troquei para a
  linha atual. Exige `OutputType=Exe` no `.csproj`, porque a v3 roda os testes como executável.
- **Shouldly** — asserções legíveis. Ver [justificativa da troca](#3-shouldly-em-vez-de-fluentassertions).
- **NSubstitute** — para os testes de serviço das próximas fases.

---

## Desvios deliberados do planejamento

O planejamento original sugeria outras escolhas nestes cinco pontos. Registro o motivo de cada
mudança:

### 1. Scalar em vez de Swagger UI

O .NET 10 **gera** o documento OpenAPI nativamente (`Microsoft.AspNetCore.OpenApi`) mas **não traz
UI** — os templates deixaram de incluir Swashbuckle desde o .NET 9. `Scalar.AspNetCore` fornece a
UI navegável em uma linha e consome direto o documento nativo.

Se a expectativa for literalmente "Swagger", `Swashbuckle.AspNetCore.SwaggerUI` (só o middleware de
UI, sem o gerador) apontado para `/openapi/v1.json` entrega a interface familiar sem reintroduzir o
gerador antigo. É uma troca de duas linhas.

### 2. `IExceptionHandler` em vez de `ExceptionHandlingMiddleware`

O planejamento citava as duas abordagens em seções diferentes. Ver
[decisão 5](#5-iexceptionhandler-em-vez-de-middleware-escrito-à-mão).

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

A chave de assinatura do JWT só será usada na fase 3, mas já pode ser gerada (mínimo de 32 bytes):

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

### Verificação rápida (PowerShell)

```powershell
$b = "https://localhost:7061/api"

# clima atual — máx/mín devem ser uma amplitude diária real, não valores quase iguais
Invoke-RestMethod "$b/clima/São José do Rio Preto"

# previsão — deve retornar exatamente 5
(Invoke-RestMethod "$b/clima/São José do Rio Preto/previsao").dias.Count

# erro padronizado — 404 em application/problem+json, não 500
curl.exe -sk "$b/clima/cidadeinexistente123"
```

> Em PowerShell 5.1, `curl` é **alias de `Invoke-WebRequest`** e não aceita flags do curl real —
> use `curl.exe` explicitamente. `-SkipHttpErrorCheck` também não existe nessa versão; para
> inspecionar 4xx, use `try/catch` ou `curl.exe -i`.

---

## Próximos passos

Em ordem de prioridade, seguindo o roadmap do planejamento:

1. **CRUD de favoritos** (fase 2) — repositórios EF, `FavoritosService`, `FavoritosController`.
   O usuário vem de uma abstração `IUsuarioAtualProvider`: antes do JWT, lê o GUID anônimo do
   header `X-Usuario-Id`; depois, a claim `sub` do token. O `FavoritosService` não muda ao trocar
   um pelo outro.
   > **Nota de segurança a registrar desde já:** pré-JWT, `X-Usuario-Id` é um **identificador de
   > partição de dados, não uma credencial** — vem do cliente e portanto é forjável. O objetivo
   > dessa fase é estabilizar o modelo de dados, não autenticar.

2. **Validação de entrada** (fase 2) — FluentValidation registrado por DI + um `IAsyncActionFilter`
   próprio. Nota: `FluentValidation.AspNetCore` (auto-validation) está descontinuado, e o
   `AddValidation()` nativo do .NET 10 **só se aplica a Minimal APIs** — com Controllers, o que se
   tem "de graça" é o `ValidationProblemDetails` automático do `[ApiController]` a partir de
   DataAnnotations.

3. **JWT** (fase 3, bônus) — `JsonWebTokenHandler`, `PasswordHasher<Usuario>` (PBKDF2, sem
   dependência de terceiros), `[Authorize]` nos favoritos, security scheme no OpenAPI.
   > ⚠️ `JwtBearerOptions.MapInboundClaims` tem default **`true`** e remapeia `sub` para
   > `ClaimTypes.NameIdentifier` — ler `User.FindFirst("sub")` retornaria `null`. Será preciso
   > `MapInboundClaims = false`.
   >
   > Esta fase **não deve gerar migration nova**; se gerar, o modelo da fase 1 estava errado.

4. **Cache + resiliência** (fase 4) — `CachedWeatherProvider` (Decorator, `IMemoryCache`, TTL
   10–15 min, chave normalizada sem acento/caixa) e `AddStandardResilienceHandler`.
   > ⚠️ Dois cuidados já mapeados: o circuit breaker padrão exige `MinimumThroughput = 100`
   > requisições em 30 s, então **nunca abriria** numa demonstração — precisa de tuning explícito;
   > e o validador de opções falha no startup se `SamplingDuration < 2 × AttemptTimeout`.

5. **Testes de serviço** (fase 5) — `ClimaService` e `FavoritosService` com NSubstitute, incluindo
   um teste anti-regressão de que máx/mín vêm da previsão e não de `main.temp_max`.

**Fora de escopo, mencionado como evolução:** refresh token; `HybridCache` (.NET 9+) no lugar de
`IMemoryCache`, que tem proteção nativa contra *cache stampede* — hoje N requisições concorrentes
para a mesma cidade fria disparariam N chamadas ao provedor; e One Call API 3.0 da OpenWeatherMap,
que entrega máx/mín diários nativos mas exige assinatura com cartão de crédito.
