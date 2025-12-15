using System.Text.Json.Serialization;

public class ResourceDTO
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("link")] public string? Link { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("learned")] public bool Learned { get; set; }
}

