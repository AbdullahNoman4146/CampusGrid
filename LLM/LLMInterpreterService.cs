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
    private readonly SemanticRuleFallbackInterpreter _fallbackInterpreter;
    private readonly ILogger<LLMInterpreterService> _logger;

    public LLMInterpreterService(
        HttpClient httpClient,
        IOptions<LLMOptions> options,
        SemanticRuleFallbackInterpreter fallbackInterpreter,
        ILogger<LLMInterpreterService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _fallbackInterpreter = fallbackInterpreter;
        _logger = logger;
    }

    public async Task<DirectiveInterpretationDto> InterpretAsync(
        string note,
        int noteIndex = 0,
        Battery? battery = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetEffectiveApiKey();

        // If FallbackOnly or no API key configured for cloud providers, use semantic parser directly
        if (_options.Provider.Equals("FallbackOnly", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(apiKey) && !_options.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Using SemanticRuleFallbackInterpreter for note {Index} (no LLM API key configured).", noteIndex);
            return _fallbackInterpreter.Interpret(note, noteIndex, battery);
        }

        try
        {
            return await CallLlmApiAsync(note, noteIndex, battery, apiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM interpretation call failed for note {Index}. Falling back to SemanticRuleFallbackInterpreter.", noteIndex);
            if (_options.EnableSemanticFallback)
            {
                return _fallbackInterpreter.Interpret(note, noteIndex, battery);
            }
            throw;
        }
    }

    private string? GetEffectiveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey;
        }

        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("LLM__APIKEY")
            ?? Environment.GetEnvironmentVariable("LLM_API_KEY")
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    }

    private async Task<DirectiveInterpretationDto> CallLlmApiAsync(
        string note,
        int noteIndex,
        Battery? battery,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

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

        var systemPrompt = BuildSystemPrompt(battery);
        var userPrompt = $"Operator note #{noteIndex}: \"{note}\"";

        var payload = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.0,
            response_format = new { type = "json_object" }
        };

        var jsonContent = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 15));

        var response = await _httpClient.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
        return ParseLlmResponse(responseJson, noteIndex, battery, note);
    }

    private string BuildEndpoint()
    {
        if (_options.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = _options.Endpoint.TrimEnd('/');
            var deployment = _options.DeploymentName ?? _options.Model;
            return $"{baseUrl}/openai/deployments/{deployment}/chat/completions?api-version={_options.ApiVersion}";
        }

        var ep = _options.Endpoint.TrimEnd('/');
        if (!ep.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ep}/chat/completions";
        }
        return ep;
    }

    private static string BuildSystemPrompt(Battery? battery)
    {
        var batteryInfo = battery != null
            ? $"Battery specification: capacity={battery.CapacityKwh} kWh, min={battery.MinimumEnergyKwh} kWh, max_charge={battery.MaxChargeKwhPerHour} kW, max_discharge={battery.MaxDischargeKwhPerHour} kW."
            : "Battery capacity is unknown.";

        return $@"You are a smart campus grid operator note interpreter for the GridWise system.
Analyze the operator note and output STRICT JSON.
{batteryInfo}

Allowed directive_type values ONLY:
- solar_reduction
- minimum_battery_reserve
- no_charge_window
- no_discharge_window
- max_grid_window
- no_op

Output JSON Schema:
{{
  ""directive_type"": ""<one of the allowed directive_type values>"",
  ""hours"": [<unique ascending integers 0-23>],
  ""factor"": <float 0.0-1.0, only for solar_reduction>,
  ""minimum_energy_kwh"": <float, only for minimum_battery_reserve>,
  ""max_grid_kwh"": <float, only for max_grid_window>,
  ""explanation"": ""<short description>""
}}

Rules:
1. Time windows are start-inclusive and end-exclusive. E.g. '1 PM to 3 PM' is [13, 14]. 'noon until 2 PM' is [12, 13].
2. For solar_reduction: factor is the usable fraction remaining. E.g., '80% reduction' means factor=0.2. 'Usable solar roughly 25%' means factor=0.25. 'half' means factor=0.5.
3. For minimum_battery_reserve: if specified as a percentage of battery capacity, compute minimum_energy_kwh = (percentage / 100) * capacity.
4. For no_op: any note unrelated to campus energy grid operations (events, sports, classes, room reservations, library notices, general administrative info) MUST have directive_type='no_op', hours=null, and explanation.
5. Never invent parameters.";
    }

    private DirectiveInterpretationDto ParseLlmResponse(string responseJson, int noteIndex, Battery? battery, string originalNote)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("LLM returned empty message content.");
        }

        using var contentDoc = JsonDocument.Parse(content);
        var parsed = contentDoc.RootElement;

        var directiveType = parsed.TryGetProperty("directive_type", out var dtProp)
            ? dtProp.GetString()?.Trim().ToLowerInvariant()
            : null;

        if (string.IsNullOrEmpty(directiveType) || !DirectiveType.All.Contains(directiveType))
        {
            throw new InvalidOperationException($"Invalid or unknown directive_type returned by LLM: '{directiveType}'.");
        }

        if (directiveType == DirectiveType.NoOp)
        {
            var exp = parsed.TryGetProperty("explanation", out var expProp)
                ? expProp.GetString() ?? "Unrelated note."
                : "This note does not affect today's 24-hour energy schedule.";

            return new DirectiveInterpretationDto
            {
                NoteIndex = noteIndex,
                Applies = false,
                DirectiveType = DirectiveType.NoOp,
                StructuredAdjustment = null,
                Explanation = exp
            };
        }

        var hours = new List<int>();
        if (parsed.TryGetProperty("hours", out var hoursProp) && hoursProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in hoursProp.EnumerateArray())
            {
                if (h.TryGetInt32(out int hourVal))
                {
                    hours.Add(hourVal);
                }
            }
        }

        double? factor = null;
        if (parsed.TryGetProperty("factor", out var factorProp) && factorProp.TryGetDouble(out double fVal))
        {
            factor = fVal;
        }

        double? minEnergy = null;
        if (parsed.TryGetProperty("minimum_energy_kwh", out var minProp) && minProp.TryGetDouble(out double mVal))
        {
            minEnergy = mVal;
        }

        double? maxGrid = null;
        if (parsed.TryGetProperty("max_grid_kwh", out var maxProp) && maxProp.TryGetDouble(out double gVal))
        {
            maxGrid = gVal;
        }

        var explanation = parsed.TryGetProperty("explanation", out var eProp)
            ? eProp.GetString() ?? $"Applied directive: {directiveType}."
            : $"Applied directive: {directiveType}.";

        return new DirectiveInterpretationDto
        {
            NoteIndex = noteIndex,
            Applies = true,
            DirectiveType = directiveType,
            StructuredAdjustment = new StructuredAdjustment
            {
                Hours = hours,
                Factor = factor,
                MinimumEnergyKwh = minEnergy,
                MaxGridKwh = maxGrid
            },
            Explanation = explanation
        };
    }
}
