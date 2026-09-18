using System.Net;
using System.Text;
using System.Text.Json;
using CampusGrid.Configuration;
using CampusGrid.LLM;
using CampusGrid.Models.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CampusGrid.Tests;

public class LLMInterpreterServiceTests
{
    [Fact]
    public async Task InterpretAllAsync_ThreeNotes_UsesOneProviderRequestAndPreservesOrder()
    {
        var directivesJson = JsonSerializer.Serialize(new
        {
            directives = new object[]
            {
                new
                {
                    note_index = 0,
                    applies = true,
                    directive_type = "solar_reduction",
                    structured_adjustment = new
                    {
                        hours = new[] { 13, 14 },
                        factor = (double?)0.2,
                        minimum_energy_kwh = (double?)null,
                        max_grid_kwh = (double?)null
                    },
                    explanation = "Twenty percent of forecast solar remains."
                },
                new
                {
                    note_index = 1,
                    applies = true,
                    directive_type = "no_charge_window",
                    structured_adjustment = new
                    {
                        hours = new[] { 18, 19 },
                        factor = (double?)null,
                        minimum_energy_kwh = (double?)null,
                        max_grid_kwh = (double?)null
                    },
                    explanation = "Charging is unavailable."
                },
                new
                {
                    note_index = 2,
                    applies = false,
                    directive_type = "no_op",
                    structured_adjustment = (object?)null,
                    explanation = "The note is unrelated to energy operations."
                }
            }
        });

        var providerEnvelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = directivesJson } } }
        });
        var handler = new RecordingHandler(providerEnvelope);
        var options = Options.Create(new LLMOptions
        {
            Provider = "OpenAI",
            Endpoint = "https://example.test/openai/v1",
            Model = "test-model",
            ApiKey = "test-key",
            TimeoutSeconds = 5,
            MaxAttempts = 1
        });
        var service = new LLMInterpreterService(
            new HttpClient(handler),
            options,
            NullLogger<LLMInterpreterService>.Instance);
        var battery = new Battery
        {
            CapacityKwh = 200,
            InitialEnergyKwh = 100,
            MinimumEnergyKwh = 20,
            MaxChargeKwhPerHour = 40,
            MaxDischargeKwhPerHour = 40
        };

        var result = await service.InterpretAllAsync(
            new[] { "solar note", "charging note", "cafeteria note" }, battery);

        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("\"type\":\"json_schema\"", handler.LastRequestBody);
        Assert.Contains("solar note", handler.LastRequestBody);
        Assert.Contains("charging note", handler.LastRequestBody);
        Assert.Contains("cafeteria note", handler.LastRequestBody);
        Assert.Equal(new[] { 0, 1, 2 }, result.Select(directive => directive.NoteIndex));
        Assert.Equal(DirectiveType.SolarReduction, result[0].DirectiveType);
        Assert.Equal(0.2, result[0].StructuredAdjustment!.Factor!.Value);
        Assert.Equal(DirectiveType.NoChargeWindow, result[1].DirectiveType);
        Assert.Equal(DirectiveType.NoOp, result[2].DirectiveType);
        Assert.Null(result[2].StructuredAdjustment);
    }

    [Fact]
    public async Task InterpretAllAsync_DuplicateNoteIndex_RejectsProviderResponse()
    {
        var directivesJson = """
            {"directives":[
              {"note_index":0,"applies":false,"directive_type":"no_op","structured_adjustment":null,"explanation":"None."},
              {"note_index":0,"applies":false,"directive_type":"no_op","structured_adjustment":null,"explanation":"None."}
            ]}
            """;
        var providerEnvelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = directivesJson } } }
        });
        var handler = new RecordingHandler(providerEnvelope);
        var service = new LLMInterpreterService(
            new HttpClient(handler),
            Options.Create(new LLMOptions
            {
                Endpoint = "https://example.test/v1",
                ApiKey = "test-key",
                MaxAttempts = 1
            }),
            NullLogger<LLMInterpreterService>.Instance);

        await Assert.ThrowsAsync<LLMServiceUnavailableException>(() =>
            service.InterpretAllAsync(new[] { "note zero", "note one" }, CreateBattery()));
    }

    private static Battery CreateBattery() => new()
    {
        CapacityKwh = 200,
        InitialEnergyKwh = 100,
        MinimumEnergyKwh = 20,
        MaxChargeKwhPerHour = 40,
        MaxDischargeKwhPerHour = 40
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public int RequestCount { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
