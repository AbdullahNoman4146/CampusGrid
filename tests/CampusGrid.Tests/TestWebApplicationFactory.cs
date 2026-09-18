using System.Text.Json;
using CampusGrid.LLM;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CampusGrid.Tests;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILLMInterpreter>();
            services.AddSingleton<ILLMInterpreter, PublicCaseTestInterpreter>();
        });
    }
}

internal sealed class PublicCaseTestInterpreter : ILLMInterpreter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IReadOnlyDictionary<string, DirectiveInterpretationDto> _directivesByNote;

    public PublicCaseTestInterpreter()
    {
        var dataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
        var directivesByNote = new Dictionary<string, DirectiveInterpretationDto>(StringComparer.Ordinal);

        foreach (var sampleCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var notes = sampleCase.GetProperty("input").GetProperty("operator_notes").EnumerateArray()
                .Select(note => note.GetString() ?? string.Empty).ToArray();
            var expected = sampleCase.GetProperty("expected_output").GetProperty("directive_interpretation")
                .EnumerateArray()
                .Select(element => JsonSerializer.Deserialize<DirectiveInterpretationDto>(element.GetRawText(), JsonOptions)!)
                .ToArray();

            for (var index = 0; index < notes.Length; index++)
            {
                directivesByNote[notes[index]] = expected[index];
            }
        }

        _directivesByNote = directivesByNote;
    }

    public Task<IReadOnlyList<DirectiveInterpretationDto>> InterpretAllAsync(
        IReadOnlyList<string> notes,
        Battery battery,
        CancellationToken cancellationToken = default)
    {
        var directives = notes.Select((note, index) =>
        {
            if (_directivesByNote.TryGetValue(note, out var expected))
            {
                return new DirectiveInterpretationDto
                {
                    NoteIndex = index,
                    Applies = expected.Applies,
                    DirectiveType = expected.DirectiveType,
                    StructuredAdjustment = expected.StructuredAdjustment,
                    Explanation = expected.Explanation
                };
            }

            return new DirectiveInterpretationDto
            {
                NoteIndex = index,
                Applies = false,
                DirectiveType = DirectiveType.NoOp,
                StructuredAdjustment = null,
                Explanation = "Test-only interpretation for a non-sample note."
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<DirectiveInterpretationDto>>(directives);
    }
}
