using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;
using WeatherApp.Application.Validators;

namespace WeatherApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // TimeProvider.System injetado explicitamente em vez de DateTime.UtcNow espalhado.
        services.TryAddSingletonTimeProvider();
        services.AddScoped<ClimaService>();
        services.AddScoped<FavoritosService>();
        services.AddScoped<IValidator<CriarFavoritoRequest>, CriarFavoritoRequestValidator>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
    }
}
