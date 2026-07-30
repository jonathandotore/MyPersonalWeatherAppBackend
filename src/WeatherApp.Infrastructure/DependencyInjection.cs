using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeatherApp.Domain.Interfaces;
using WeatherApp.Infrastructure.Auth;
using WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap;
using WeatherApp.Infrastructure.Persistence;
using WeatherApp.Infrastructure.Persistence.Repositories;

namespace WeatherApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistencia(configuration);
        services.AddProvedorDeClima(configuration);
        services.AddAutenticacaoJwt(configuration);

        return services;
    }

    private static IServiceCollection AddAutenticacaoJwt(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SecaoConfiguracao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }

    private static IServiceCollection AddPersistencia(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("WeatherAppDb") ?? throw new InvalidOperationException("ConnectionStrings:WeatherAppDb não configurada em appsettings.json.");

        services.AddDbContext<WeatherAppDbContext>(o => o.UseSqlServer(connectionString));

        services.AddScoped<ICidadeFavoritaRepository, CidadeFavoritaRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();

        return services;
    }

    private static IServiceCollection AddProvedorDeClima(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart faz a aplicação falhar no boot com mensagem clara se a ApiKey não estiver nos user-secrets, em vez de subir e só quebrar na primeira requisição.
        services.AddOptions<OpenWeatherMapSettings>()
            .Bind(configuration.GetSection(OpenWeatherMapSettings.SecaoConfiguracao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WeatherCacheSettings>()
            .Bind(configuration.GetSection(WeatherCacheSettings.SecaoConfiguracao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();

        // Registrado pelo tipo concreto, não por IWeatherProvider: quem é exposto ao resto da
        // aplicação como IWeatherProvider é o CachedWeatherProvider, logo abaixo.
        services.AddHttpClient<OpenWeatherMapProvider>((sp, http) =>
        {
            var cfg = configuration
                .GetSection(OpenWeatherMapSettings.SecaoConfiguracao)
                .Get<OpenWeatherMapSettings>() ?? new OpenWeatherMapSettings();

            http.BaseAddress = new Uri(cfg.BaseUrl);
            http.Timeout = Timeout.InfiniteTimeSpan; // o pipeline de resiliência é o único dono de timeout
        })
        .AddStandardResilienceHandler(o =>
        {
            o.Retry.MaxRetryAttempts = 3;
            o.Retry.UseJitter = true;
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);

            // Os defaults (MinimumThroughput=100 em 30s) nunca abrem numa demonstração — exigem
            // volume de tráfego que um teste técnico não gera. Reduzidos para o circuito ser
            // observável sem descaracterizar o propósito (proteger contra instabilidade real).
            // MinimumThroughput=3: com AttemptTimeout=5s e MaxRetryAttempts=3, uma única
            // requisição de entrada completa no máximo 3 tentativas inteiras antes do
            // TotalRequestTimeout (20s) cortar a 4ª pela metade — medido ao vivo contra um
            // endpoint morto. Com 4, o circuito nunca via throughput suficiente para abrir.
            o.CircuitBreaker.MinimumThroughput = 3;
            o.CircuitBreaker.FailureRatio = 0.5;
            // SamplingDuration=20s, não os 10s mínimos (2 × AttemptTimeout): com backoff
            // exponencial entre os 3 retries, as falhas de uma única requisição se espalham por
            // quase os 20s inteiros do TotalRequestTimeout — medido ao vivo. Com uma janela de
            // 10s, a 1ª falha "saía" da janela antes da 3ª acontecer, e o circuito nunca via as
            // 3 falhas simultaneamente.
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IWeatherProvider>(sp => new CachedWeatherProvider(
            sp.GetRequiredService<OpenWeatherMapProvider>(),
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<IOptions<WeatherCacheSettings>>(),
            sp.GetRequiredService<ILogger<CachedWeatherProvider>>()));

        return services;
    }
}
