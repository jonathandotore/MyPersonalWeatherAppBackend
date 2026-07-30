# Planejamento — Backend (.NET 10)
### Teste Técnico: Aplicação de Previsão do Tempo | Nível: Pleno

> Documento irmão: `planejamento-frontend-angular.md` (contém a parte do Angular). Este arquivo cobre só a API.

---

## 1. Escopo do Backend

- API REST integrando com provedor externo de clima (OpenWeatherMap ou WeatherAPI)
- Endpoints: clima atual por cidade, previsão 5 dias, CRUD de favoritos
- Persistência de favoritos em SQL Server
- Bônus: JWT + restrição de endpoints autenticados

O domínio é pequeno (praticamente 2 entidades: `CidadeFavorita` e `Usuario`). A complexidade real está na **integração externa** — não em regras de negócio ricas. Isso é o que orienta a escolha de padrões abaixo: o valor está em isolar bem essa integração, não em empilhar camadas por empilhar.

---

## 2. Recomendação de Arquitetura (Resumo)

**Clean Architecture pragmática**, em 4 projetos (Domain, Application, Infrastructure, API), com **EF Core 10** para acesso a dados e **Adapter + Decorator + Polly** para a integração com o provedor de clima.

**Por que não CQRS/MediatR/DDD tático completo?** Esses padrões se justificam em domínios com múltiplos agregados e regras de negócio complexas. Aqui não há isso — usá-los só para "mostrar conhecimento" é, na prática, um sinal negativo numa avaliação de nível pleno (falta de calibragem entre solução e problema). A divisão em 4 projetos já entrega inversão de dependência, testabilidade e a possibilidade de trocar EF Core por Dapper ou trocar o provedor de clima sem tocar em regra de negócio — que é o que importa aqui.

---

## 3. Estrutura de Camadas

```
WeatherApp.sln
│
├── src/
│   ├── WeatherApp.Domain/            → Entidades, interfaces de repositório, enums
│   │     Entities/
│   │       CidadeFavorita.cs
│   │       Usuario.cs
│   │     Interfaces/
│   │       ICidadeFavoritaRepository.cs
│   │       IWeatherProvider.cs
│   │
│   ├── WeatherApp.Application/       → Casos de uso, DTOs, validação, orquestração
│   │     Services/
│   │       ClimaService.cs
│   │       FavoritosService.cs
│   │     DTOs/
│   │       ClimaAtualDto.cs / PrevisaoDiaDto.cs / CidadeFavoritaDto.cs
│   │     Validators/                 (FluentValidation)
│   │
│   ├── WeatherApp.Infrastructure/    → Implementações concretas
│   │     Persistence/
│   │       WeatherAppDbContext.cs
│   │       Repositories/CidadeFavoritaRepository.cs
│   │     ExternalServices/
│   │       OpenWeatherMapProvider.cs   (implementa IWeatherProvider)
│   │       CachedWeatherProvider.cs    (decorator com IMemoryCache)
│   │     Auth/
│   │       JwtTokenService.cs
│   │
│   └── WeatherApp.API/               → Controllers, Middleware, DI, Swagger
│         Controllers/
│           ClimaController.cs
│           FavoritosController.cs
│           AuthController.cs
│         Middleware/
│           ExceptionHandlingMiddleware.cs
│         Program.cs
│
└── tests/
      WeatherApp.Application.Tests/   (xUnit + FluentAssertions + NSubstitute)
```

---

## 4. Design Patterns Aplicados

| Padrão | Onde | Justificativa |
|---|---|---|
| **Repository** | `ICidadeFavoritaRepository` + implementação EF Core | Abstrai persistência; facilita mock em testes e troca de tecnologia de acesso a dados |
| **Adapter** | `IWeatherProvider` → `OpenWeatherMapProvider` | O enunciado permite OpenWeatherMap *ou* WeatherAPI — uma interface comum evita acoplar o domínio ao contrato de um provedor específico (funciona como *anti-corruption layer*) |
| **Decorator** | `CachedWeatherProvider` envolvendo o provider real | Adiciona cache (`IMemoryCache`, TTL de 10–15 min) sem alterar a lógica de chamada à API — importante porque APIs gratuitas de clima têm rate limit agressivo |
| **Dependency Injection** | Nativo do ASP.NET Core, em todas as camadas | Inversão de dependência, testabilidade |
| **DTO** | `Application/DTOs` | Desacopla o contrato da API dos modelos de domínio/EF; evita vazar detalhes do provedor externo para o cliente |
| **Options Pattern** | `IOptions<WeatherApiSettings>`, `IOptions<JwtSettings>` | Configuração tipada e validável, sem "magic strings" espalhadas |
| **Exception Handling centralizado** | `IExceptionHandler` (nativo no .NET 8+, reforçado no .NET 10) | Respostas de erro padronizadas (`ProblemDetails`), sem `try/catch` repetido em cada Controller |
| **Resiliência (Retry/Circuit Breaker)** | `Polly` + `IHttpClientFactory` no `OpenWeatherMapProvider` | A API externa pode falhar/timeout; sem isso, uma instabilidade do provedor derruba a aplicação inteira |
| **Unit of Work** *(opcional)* | Implícito no `DbContext` do EF Core | Só vale formalizar como padrão separado se houver múltiplos repositórios na mesma transação — não é o caso aqui |

> **Não recomendo** para este escopo: CQRS/MediatR, Domain Events, Specification Pattern, microsserviços. Vale mencionar na entrevista que foram avaliados e descartados por desproporcionais — isso é, inclusive, um ponto forte para pleno (mostra julgamento, não só repertório de padrões).

---

## 5. Modelagem do Banco (SQL Server)

```sql
CREATE TABLE Usuarios (
    Id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Nome          NVARCHAR(150)   NOT NULL,
    Email         NVARCHAR(200)   NOT NULL UNIQUE,
    SenhaHash     NVARCHAR(300)   NOT NULL,
    DataCriacao   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE CidadesFavoritas (
    Id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Nome          NVARCHAR(150)   NOT NULL,
    PaisCodigo    NVARCHAR(5)     NULL,          -- ex.: "BR" — evita ambiguidade de cidades com nome repetido
    Latitude      DECIMAL(9,6)    NULL,
    Longitude     DECIMAL(9,6)    NULL,
    UsuarioId     UNIQUEIDENTIFIER NOT NULL,      -- FK para Usuarios (ou id anônimo, ver nota abaixo)
    DataCriacao   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CidadesFavoritas_Usuarios FOREIGN KEY (UsuarioId) REFERENCES Usuarios(Id),
    CONSTRAINT UQ_Usuario_Cidade UNIQUE (UsuarioId, Nome)   -- evita duplicar favorito
);
```

**Decisão de design:** implemente `UsuarioId` desde o início (mesmo antes do JWT). Sem autenticação, gere um identificador anônimo estável (GUID salvo pelo front — pode ser o mesmo usado no LocalStorage) e trate-o como "usuário implícito". Quando o JWT (bônus) for implementado, esse GUID vira o `Id` real do usuário autenticado. Assim a tabela não precisa ser remodelada depois.

`Latitude`/`Longitude` são opcionais, mas evitam ambiguidade ("Springfield" existe em vários países) e permitem consultar a previsão sem precisar re-geocodificar o nome da cidade a cada chamada.

---

## 6. Endpoints (contrato consumido pelo frontend)

| Método | Rota | Autenticação | Retorno |
|---|---|---|---|
| GET | `/api/clima/{cidade}` | Pública | Temp atual, condição+ícone, max/min do dia, umidade |
| GET | `/api/clima/{cidade}/previsao` | Pública | Array de 5 dias: data, temp max/min, ícone |
| GET | `/api/favoritos` | Bônus: `[Authorize]` | Lista de cidades favoritas do usuário |
| POST | `/api/favoritos` | Bônus: `[Authorize]` | Cidade favoritada criada |
| DELETE | `/api/favoritos/{id}` | Bônus: `[Authorize]` | 204 No Content |
| POST | `/api/auth/login` / `/api/auth/register` | Pública | JWT |

**Controllers vs Minimal APIs (.NET 10):** o .NET 10 melhorou as Minimal APIs (validação nativa via `AddValidation()`). Ainda assim, para um teste técnico, **Controllers tradicionais** tendem a ser mais legíveis para quem avalia (organização visual por recurso, `[Authorize]` declarativo). Minimal APIs são válidas e mais modernas, mas isso é preferência de equipe — não citaria como fator decisivo de nota.

Recomendo documentar a resposta de erro padrão (via `ProblemDetails`) aqui também, para o time de frontend saber o formato esperado de erro (`title`, `status`, `detail`, `errors`).

---

## 7. Autenticação JWT (bônus)

- `Microsoft.AspNetCore.Authentication.JwtBearer`
- Claims mínimas: `sub` (UserId), `email`
- `[Authorize]` nos endpoints de favoritos; extrair `UsuarioId` do `ClaimsPrincipal` (nunca confiar em um `usuarioId` vindo do corpo da requisição)
- Refresh token: **não é necessário** para o escopo do teste — mencionar como próximo passo é suficiente

---

## 8. Roadmap de Execução (Backend)

| Fase | Entrega |
|---|---|
| 0 | Setup: solution .NET, SQL Server (local ou `docker-compose`), chave de API do provedor de clima |
| 1 | Entidades + EF Core migrations + `IWeatherProvider`/`OpenWeatherMapProvider` + endpoint clima atual |
| 2 | Endpoint previsão 5 dias + CRUD de favoritos (sem JWT ainda) |
| 3 (bônus) | JWT: login/register + `[Authorize]` nos endpoints de favoritos |
| 4 | Cache (`IMemoryCache`/decorator) + Polly (retry/circuit breaker) |
| 5 | Testes unitários (Application layer) + Swagger/OpenAPI + README |

**Prioridade se o tempo apertar:** fases 0–2 são o núcleo funcional. JWT, cache e testes são diferenciais, mas o enunciado já marca autenticação como bônus — uma API completa e simples bate uma "arquiteturalmente rica" mas incompleta.

---

## 9. Checklist de Entrega (Backend)

- [ ] API REST .NET rodando, Swagger/OpenAPI documentado
- [ ] Busca de clima por cidade (integração externa)
- [ ] Previsão 5 dias
- [ ] CRUD de favoritos persistido em SQL Server
- [ ] Tratamento de erros e validação de entrada
- [ ] (Bônus) JWT protegendo endpoints de favoritos
- [ ] README com instruções de setup e decisões de arquitetura
