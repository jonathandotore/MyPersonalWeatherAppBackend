using WeatherApp.Domain.Entities;

namespace WeatherApp.Domain.Interfaces;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiraEmUtc) GerarToken(Usuario usuario);
}
