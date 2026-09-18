using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CampusGrid.Configuration;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;

namespace CampusGrid.LLM;

public class LLMInterpreterService : ILLMInterpreter
{
    private readonly HttpClient _httpClient;
    private readonly LLMOptions _options;
    private readonly ILogger<LLMInterpreterService> _logger;

    public LLMInterpreterService(
        HttpClient httpClient,
        IOptions<LLMOptions> options,
        ILogger<LLMInterpreterService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DirectiveInterpretationDto>> InterpretAllAsync(
        IReadOnlyList<string> notes,
        Battery battery,
        CancellationToken cancellationToken = default)
    {
        if (notes.Count is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(notes), "Between one and three notes are required.");
        }

        var apiKey = GetEffectiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) &&
            !_options.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new LLMServiceUnavailableException(
                "No LLM API key is configured. Set GROQ_API_KEY or LLM__ApiKey on the backend host.");
        }

        var attempts = Math.Clamp(_options.MaxAttempts, 1, 2);
        Exception? lastError = null;
        using var overallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 1, 29)));

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await CallLlmApiAsync(notes, battery, apiKey, overallTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException("The LLM provider did not respond before the configured deadline.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                lastError = ex;
            }

            if (attempt < attempts)
            {
                _logger.LogWarning(lastError, "LLM interpretation attempt {Attempt} failed; retrying once.", attempt);
            }
        }

        throw new LLMServiceUnavailableException(
            "The LLM provider could not produce a valid directive response.",
            lastError ?? new InvalidOperationException("Unknown LLM provider failure."));
    }

    private string? GetEffectiveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey;
        }

        return Environment.GetEnvironmentVariable("GROQ_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("LLM__APIKEY")
            ?? Environment.GetEnvironmentVariable("LLM_API_KEY")
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    }

    private async Task<IReadOnlyList<DirectiveInterpretationDto>> CallLlmApiAsync(
        IReadOnlyList<string> notes,
        Battery battery,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint());

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (_options.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Add("api-key", apiKey);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        var notePayload = notes.Select((text, noteIndex) => new { note_index = noteIndex, text });
        var payload = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = BuildSystemPrompt(battery, notes.Count) },
                new
                {
                    role = "user",
                    content = "Interpret every operator note in this JSON: " +
                              JsonSerializer.Serialize(new { operator_notes = notePayload })
                }
            },
            temperature = 0.0,
            response_format = BuildResponseFormat(notes.Count)
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 25)));

        using var response = await _httpClient.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(timeout.Token);
        return ParseLlmResponse(responseJson, notes.Count);
    }

    private string BuildEndpoint()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException("LLM endpoint is not configured.");
        }

        if (_options.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = _options.Endpoint.TrimEnd('/');
            var deployment = _options.DeploymentName ?? _options.Model;
            return $"{baseUrl}/openai/deployments/{deployment}/chat/completions?api-version={_options.ApiVersion}";
        }

        var endpoint = _options.Endpoint.TrimEnd('/');
        return endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : $"{endpoint}/chat/completions";
    }

    private static object BuildResponseFormat(int noteCount)
    {
        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "gridwise_directives",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        directives = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    note_index = new
                                    {
                                        type = "integer",
                                        minimum = 0,
                                        maximum = noteCount - 1
                                    },
                                    applies = new { type = "boolean" },
                                    directive_type = new
                                    {
                                        type = "string",
                                        @enum = DirectiveType.All.OrderBy(value => value).ToArray()
                                    },
                                    structured_adjustment = new
                                    {
                                        anyOf = new object[]
                                        {
                                            new
                                            {
                                                type = "object",
                                                properties = new
                                                {
                                                    hours = new
                                                    {
                                                        type = new[] { "array", "null" },
                                                        items = new { type = "integer", minimum = 0, maximum = 23 }
                                                    },
                                                    factor = new
                                                    {
                                                        type = new[] { "number", "null" }, minimum = 0, maximum = 1
                                                    },
                                                    minimum_energy_kwh = new
                                                    {
                                                        type = new[] { "number", "null" }, minimum = 0
                                                    },
                                                    max_grid_kwh = new
                                                    {
                                                        type = new[] { "number", "null" }, minimum = 0
                                                    }
                                                },
                                                required = new[]
                                                {
                                                    "hours", "factor", "minimum_energy_kwh", "max_grid_kwh"
                                                },
                                                additionalProperties = false
                                            },
                                            new { type = "null" }
                                        }
                                    },
                                    explanation = new { type = "string" }
                                },
                                required = new[]
                                {
                                    "note_index", "applies", "directive_type", "structured_adjustment", "explanation"
                                },
                                additionalProperties = false
                            }
                        }
                    },
                    required = new[] { "directives" },
                    additionalProperties = false
                }
            }
        };
    }

    private static string BuildSystemPrompt(Battery battery, int noteCount)
    {
        return $$"""
            You are the GridWise operator-note interpreter. Return one JSON object only.
            Interpret all {{noteCount}} notes independently and preserve their note_index.

            Battery specification:
            capacity={{battery.CapacityKwh}} kWh
            minimum={{battery.MinimumEnergyKwh}} kWh
            maximum charge={{battery.MaxChargeKwhPerHour}} kWh/hour
            maximum discharge={{battery.MaxDischargeKwhPerHour}} kWh/hour

            The root schema is:
            {
              "directives": [
                {
                  "note_index": 0,
                  "applies": true,
                  "directive_type": "solar_reduction | minimum_battery_reserve | no_charge_window | no_discharge_window | max_grid_window | no_op",
                  "structured_adjustment": {
                    "hours": [0],
                    "factor": 0.5,
                    "minimum_energy_kwh": null,
                    "max_grid_kwh": null
                  },
                  "explanation": "short explanation"
                }
              ]
            }

            Rules:
            1. Return exactly one directive for each input note and no additional directives.
            2. Hours are unique ascending integers from 0 through 23. Time windows are start-inclusive and end-exclusive.
               Example: 1 PM to 3 PM means [13, 14]. Midnight to 2 AM means [0, 1].
            3. solar_reduction.factor is the usable fraction remaining. An 80% reduction means 0.2; one-fifth remains means 0.2.
            4. For a percentage battery reserve, calculate minimum_energy_kwh from the provided capacity.
            5. For no_charge_window and no_discharge_window, set factor, minimum_energy_kwh, and max_grid_kwh to null.
            6. For max_grid_window, set factor and minimum_energy_kwh to null.
            7. For unrelated or non-actionable notes, use applies=false, directive_type="no_op", and structured_adjustment=null.
            8. For every active directive use applies=true. Never invent missing hours or numeric limits.
            9. The adjustment schema requires all four properties. Set irrelevant properties to null; never fill them with invented values.
            """;
    }

    private static IReadOnlyList<DirectiveInterpretationDto> ParseLlmResponse(string responseJson, int expectedCount)
    {
        using var envelope = JsonDocument.Parse(responseJson);
        var content = envelope.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("LLM returned empty message content.");
        }

        using var contentDocument = JsonDocument.Parse(content);
        if (!contentDocument.RootElement.TryGetProperty("directives", out var directivesElement) ||
            directivesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("LLM response must contain a 'directives' array.");
        }

        var directives = directivesElement.EnumerateArray()
            .Select(ParseDirective).OrderBy(directive => directive.NoteIndex).ToList();

        if (directives.Count != expectedCount ||
            directives.Select(d => d.NoteIndex).Distinct().Count() != expectedCount ||
            directives.Where((directive, index) => directive.NoteIndex != index).Any())
        {
            throw new InvalidOperationException(
                $"LLM must return exactly one directive for each note index from 0 to {expectedCount - 1}.");
        }

        return directives;
    }

    private static DirectiveInterpretationDto ParseDirective(JsonElement element)
    {
        if (!element.TryGetProperty("note_index", out var indexElement) || !indexElement.TryGetInt32(out var noteIndex))
        {
            throw new InvalidOperationException("Each LLM directive requires an integer note_index.");
        }

        if (!element.TryGetProperty("applies", out var appliesElement) ||
            appliesElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException($"Directive #{noteIndex} requires a Boolean applies value.");
        }

        if (!element.TryGetProperty("directive_type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Directive #{noteIndex} requires directive_type.");
        }

        var directiveType = typeElement.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!DirectiveType.All.Contains(directiveType))
        {
            throw new InvalidOperationException($"Directive #{noteIndex} returned unknown directive_type '{directiveType}'.");
        }

        StructuredAdjustment? adjustment = null;
        if (element.TryGetProperty("structured_adjustment", out var adjustmentElement) &&
            adjustmentElement.ValueKind != JsonValueKind.Null)
        {
            if (adjustmentElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Directive #{noteIndex} has an invalid structured_adjustment.");
            }

            adjustment = new StructuredAdjustment
            {
                Hours = ReadOptionalHours(adjustmentElement, noteIndex),
                Factor = ReadOptionalFiniteDouble(adjustmentElement, "factor", noteIndex),
                MinimumEnergyKwh = ReadOptionalFiniteDouble(adjustmentElement, "minimum_energy_kwh", noteIndex),
                MaxGridKwh = ReadOptionalFiniteDouble(adjustmentElement, "max_grid_kwh", noteIndex)
            };
        }

        var explanation = element.TryGetProperty("explanation", out var explanationElement) &&
                          explanationElement.ValueKind == JsonValueKind.String
            ? explanationElement.GetString() ?? string.Empty
            : string.Empty;

        return new DirectiveInterpretationDto
        {
            NoteIndex = noteIndex,
            Applies = appliesElement.GetBoolean(),
            DirectiveType = directiveType,
            StructuredAdjustment = adjustment,
            Explanation = explanation
        };
    }

    private static List<int>? ReadOptionalHours(JsonElement adjustment, int noteIndex)
    {
        if (!adjustment.TryGetProperty("hours", out var hoursElement) || hoursElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (hoursElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Directive #{noteIndex} hours must be an array of integers.");
        }

        var hours = new List<int>();
        foreach (var hourElement in hoursElement.EnumerateArray())
        {
            if (!hourElement.TryGetInt32(out var hour))
            {
                throw new InvalidOperationException($"Directive #{noteIndex} hours must contain integers only.");
            }
            hours.Add(hour);
        }
        return hours;
    }

    private static double? ReadOptionalFiniteDouble(JsonElement adjustment, string propertyName, int noteIndex)
    {
        if (!adjustment.TryGetProperty(propertyName, out var valueElement) || valueElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!valueElement.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            throw new InvalidOperationException($"Directive #{noteIndex} property '{propertyName}' must be a finite number.");
        }
        return value;
    }
}
