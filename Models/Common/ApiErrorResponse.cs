using System.Text.Json.Serialization;

namespace CampusGrid.Models.Common;

public class ApiErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Details { get; set; }

    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }
}
