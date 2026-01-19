using System.Net.Http.Json;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public static class ApiClientJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(text, JsonOptions)!;
    }

    public static JsonContent ToJsonContent<T>(T value)
        => JsonContent.Create(value, options: JsonOptions);
}

