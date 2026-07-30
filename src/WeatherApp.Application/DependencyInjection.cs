using Microsoft.Extensions.DependencyInjection;
using WeatherApp.Application.Services;

namespace WeatherApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // TimeProvider.System injetado explicitamente em vez de DateTime.UtcNow espalhado:
        // é o que permite testar a agregação de previsão com um "agora" controlado.
        services.TryAddSingletonTimeProvider();

        services.AddScoped<ClimaService>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
