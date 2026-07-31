using System.Globalization;
using System.Text;

namespace WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap;

/// <summary>Normaliza a chave de cache (não a chamada ao provedor, que usa a string original)
/// para que "São Paulo", " sao  paulo " e "SAO PAULO" compartilhem a mesma entrada.</summary>
internal static class ChaveCache
{
    public static string DeClimaAtual(string cidade) => $"clima:atual:v1:{Normalizar(cidade)}";

    public static string DePrevisao(string cidade) => $"clima:previsao:v1:{Normalizar(cidade)}";

    /// <summary>~11 m de precisão (4 casas decimais) — próxima o bastante de cachear a mesma
    /// localização, sem exigir bit-a-bit igual entre duas leituras de GPS da mesma cidade.</summary>
    public static string DeClimaAtualPorCoordenadas(decimal latitude, decimal longitude) =>
        $"clima:atual:coord:v1:{NormalizarCoordenada(latitude)}:{NormalizarCoordenada(longitude)}";

    public static string DePrevisaoPorCoordenadas(decimal latitude, decimal longitude) =>
        $"clima:previsao:coord:v1:{NormalizarCoordenada(latitude)}:{NormalizarCoordenada(longitude)}";

    private static string NormalizarCoordenada(decimal valor) =>
        valor.ToString("F4", CultureInfo.InvariantCulture);

    private static string Normalizar(string cidade)
    {
        var semAcento = cidade.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);

        return string.Join(' ', new string([.. semAcento]).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
