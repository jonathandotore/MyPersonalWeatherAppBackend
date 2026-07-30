using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddHttpClient<IWeatherProvider, OpenWeatherMapProvider>((sp, http) =>
        {
            var cfg = configuration
                .GetSection(OpenWeatherMapSettings.SecaoConfiguracao)
                .Get<OpenWeatherMapSettings>() ?? new OpenWeatherMapSettings();

            http.BaseAddress = new Uri(cfg.BaseUrl);
        });

        return services;
    }
}
