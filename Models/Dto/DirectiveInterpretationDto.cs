using System.Text.Json.Serialization;
using CampusGrid.Models.Domain;

namespace CampusGrid.Models.Dto;

public class DirectiveInterpretationDto
{
    [JsonPropertyName("note_index")]
    public int NoteIndex { get; set; }

    [JsonPropertyName("applies")]
    public bool Applies { get; set; }

    [JsonPropertyName("directive_type")]
    public string DirectiveType { get; set; } = string.Empty;

    [JsonPropertyName("structured_adjustment")]
    public StructuredAdjustment? StructuredAdjustment { get; set; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;
}
