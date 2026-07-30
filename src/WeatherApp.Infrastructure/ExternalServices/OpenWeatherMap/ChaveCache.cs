using System.Globalization;
using System.Text;

namespace WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap;

/// <summary>Normaliza a chave de cache (não a chamada ao provedor, que usa a string original)
/// para que "São Paulo", " sao  paulo " e "SAO PAULO" compartilhem a mesma entrada.</summary>
internal static class ChaveCache
{
    public static string DeClimaAtual(string cidade) => $"clima:atual:v1:{Normalizar(cidade)}";

    public static string DePrevisao(string cidade) => $"clima:previsao:v1:{Normalizar(cidade)}";

    private static string Normalizar(string cidade)
    {
        var semAcento = cidade.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);

        return string.Join(' ', new string([.. semAcento]).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
