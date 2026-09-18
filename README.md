# GridWise — Smart Campus Energy Optimization API

[![Build & Test](https://img.shields.io/badge/build-passing-brightgreen.svg)](#testing)
[![.NET](https://img.shields.io/badge/.NET-8.0%20LTS-blue.svg)](https://dotnet.microsoft.com/)
[![Optimization](https://img.shields.io/badge/Solver-Google%20OR--Tools%20GLOP-orange.svg)](https://developers.google.com/optimization)
[![Event](https://img.shields.io/badge/Hackathon-BUP%20CSE%20Fest%202026-purple.svg)](#)

A production-quality, AI-powered HTTP API service designed for the **GridWise Smart Campus Energy Optimization Challenge** at **BUP CSE Fest 2026 Hackathon**.

The system ingests a 24-hour campus energy forecast (demand, solar, and dynamic tariffs), specifications for an energy storage battery, and natural-language operator notes. It leverages an LLM-assisted interpreter with guardrail validation to convert unstructured operator instructions into machine-checkable energy directives, and then solves a mathematical Linear Programming (LP) optimization problem using **Google OR-Tools** to minimize total grid import costs while enforcing energy balance, rate limits, and end-of-day battery neutrality.

---

## Architecture Overview

The system follows **Clean Architecture** principles and implements a strict deterministic guardrail pipeline around the untrusted LLM output:

```
                  +-----------------------------------+
                  |           HTTP Client             |
                  +-----------------------------------+
                                    |
                       POST /optimize-energy
                                    |
                                    v
+---------------------------------------------------------------------------+
|                          ASP.NET Core Web API                             |
|                                                                           |
|  [EnergyController]                                                       |
|         |                                                                 |
|         v                                                                 |
|  [EnergyRequestValidator] ─── (Checks 24h, non-negative, battery bounds)  |
|         |                                                                 |
|         v                                                                 |
|  [ILLMInterpreter / LLMInterpreterService]                                |
|         │  • OpenAI / Azure OpenAI / Local (vLLM, Ollama)                 |
|         │  • Automatic Semantic NLP Rule Fallback Engine                  |
|         v                                                                 |
|  [DirectiveValidator Guardrail] ─── (Validates types, hours, ranges, etc) |
|         |                                                                 |
|         v                                                                 |
|  [IEnergyOptimizer / EnergyOptimizer]                                     |
|         │  • Primary: Google OR-Tools GLOP Linear Programming Solver      |
|         │  • Fallback: Managed Simplex LP Solver                          |
|         v                                                                 |
|  [ScheduleValidator] ─── (Deterministic physics & balance checks)        |
|         |                                                                 |
+---------│-----------------------------------------------------------------+
          v
  JSON API Response
```

### Execution Flow

1. **Request Ingestion**: Validates scenario structure (24 hours, non-negative physical values, valid battery limits).
2. **LLM Directive Interpretation**: Each operator note (1–3 strings) is parsed via `ILLMInterpreter` into structured directives. Supports paraphrased instructions like maintenance, panel washing, cloud cover, relay testing, feeder limits, and non-actionable distractors (`no_op`).
3. **Deterministic Guardrails**: The `DirectiveValidator` rigorously validates the LLM output (verifying allowed types, unique ascending hours in [0, 23], solar factor $\in [0, 1]$, reserve $\le$ capacity, and absence of invented parameters).
4. **Mathematical Optimization**: Formulates and solves a 24-hour continuous Linear Program:
   $$\min \sum_{t=0}^{23} \Big( P_t \cdot \text{grid}_t + 10^{-5} \cdot (\text{charge}_t + \text{discharge}_t) \Big)$$
   subject to:
   - Energy balance: $\text{grid}_t + \text{solar\_used}_t + \text{discharge}_t = \text{demand}_t + \text{charge}_t$
   - Usable solar: $\text{solar\_used}_t \le \text{effective\_solar}_t$
   - Battery reserve: $\text{energy}_t \ge \text{active\_reserve}_t$
   - Capacity: $\text{energy}_t \le \text{capacity}$
   - Charge rate: $\text{charge}_t \le \text{max\_charge}_t$ (0 during `no_charge_window`)
   - Discharge rate: $\text{discharge}_t \le \text{max\_discharge}_t$ (0 during `no_discharge_window`)
   - Grid feeder cap: $\text{grid}_t \le \text{max\_grid}_t$
   - Neutrality: $\text{energy}_{23} = \text{initial\_energy}$
5. **Schedule Verification**: Verifies physical consistency before serializing response.
6. **Controlled Error Handling**: Returns `400 Bad Request` for invalid JSON, `422 Unprocessable Entity` for semantic or constraint violations, and `500 Internal Server Error` without leaking secrets.

---

## Supported Directives

| Directive Type | Description | Example Note | Extracted Parameters |
|---|---|---|---|
| `solar_reduction` | Reduces usable solar during window | *"Facilities will wash rooftop panels from noon until 2 PM. Usable solar should be treated as 25%."* | `hours: [12, 13], factor: 0.25` |
| `minimum_battery_reserve` | Raises minimum allowed battery energy | *"Keep at least 50% of the battery capacity stored from 6 PM to 9 PM."* | `hours: [18, 19, 20], minimum_energy_kwh: 100` |
| `no_charge_window` | Disables battery charging during window | *"Battery charger will be isolated from 2 AM until 5 AM."* | `hours: [2, 3, 4]` |
| `no_discharge_window` | Disables battery discharging during window | *"Battery must not discharge from 6 PM until 8 PM."* | `hours: [18, 19]` |
| `max_grid_window` | Caps grid import during window | *"Campus grid import must not exceed 155 kWh from 6 PM until 9 PM."* | `hours: [18, 19, 20], max_grid_kwh: 155` |
| `no_op` | Unrelated or non-actionable note | *"The sports office moved next month's registration deadline."* | `applies: false, structured_adjustment: null` |

*Note: Time windows are start-inclusive and end-exclusive (e.g. 1 PM to 3 PM maps to hours [13, 14]).*

---

## Configuration & Environment Variables

Configure via `appsettings.json` or environment variables:

```json
{
  "LLM": {
    "Provider": "OpenAI",
    "ApiKey": "",
    "Endpoint": "https://api.openai.com/v1",
    "Model": "gpt-4o-mini",
    "DeploymentName": "",
    "ApiVersion": "2024-02-15-preview",
    "TimeoutSeconds": 15,
    "EnableSemanticFallback": true
  }
}
```

### Environment Variables

| Variable | Description | Default |
|---|---|---|
| `OPENAI_API_KEY` or `LLM__APIKEY` | API Key for LLM provider | `""` (Uses built-in NLP fallback if empty) |
| `LLM__PROVIDER` | Provider: `OpenAI`, `AzureOpenAI`, `Local`, `FallbackOnly` | `OpenAI` |
| `LLM__ENDPOINT` | Base endpoint URL | `https://api.openai.com/v1` |
| `LLM__MODEL` | Model name | `gpt-4o-mini` |
| `LLM__ENABLESEMANTICFALLBACK` | Enable deterministic NLP parser if API offline | `true` |
| `ASPNETCORE_URLS` | HTTP listening URL | `http://0.0.0.0:8080` |

> [!NOTE]
> **Zero-Configuration Fallback**: If no API key is provided or the network is offline, the service automatically utilizes its built-in **Semantic Rule Fallback Interpreter**. It passes 100% of benchmark test cases without requiring external cloud access.

---

## Running Locally

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. Build the Project
```bash
dotnet build
```

### 2. Run the Service
```bash
dotnet run --project CampusGrid.csproj
```
The API will be available at `http://localhost:5000` or `http://localhost:8080`.
Swagger UI is accessible in Development mode at `/swagger`.

---

## Running with Docker

### 1. Build the Docker Image
```bash
docker build -t gridwise-api .
```

### 2. Run the Container
```bash
docker run -d -p 8080:8080 --name gridwise gridwise-api
```

With custom OpenAI API Key:
```bash
docker run -d -p 8080:8080 -e OPENAI_API_KEY="your-api-key" --name gridwise gridwise-api
```

---

## Automated Testing

The solution includes comprehensive unit and integration test suites:
- Health check verification (`GET /health`)
- All 10 official benchmark scenarios (`SAMPLE-01` through `SAMPLE-10`)
- Guardrail validation tests (untrusted LLM outputs, invalid ranges, non-ascending hours, invented parameters)
- Request validation tests (400 Bad Request for malformed JSON, 422 Unprocessable Entity for semantic errors)
- Physics tests (hourly energy balance, battery neutrality)

Execute all tests:
```bash
dotnet test tests/CampusGrid.Tests/CampusGrid.Tests.csproj
```

---
### chocolate

## API Documentation & Examples

### 1. Health Endpoint

**Request:**
```http
GET /health HTTP/1.1
Host: localhost:8080
```

**Response:**
```json
{
  "status": "ok"
}
```

---

### 2. Optimization Endpoint

**Request:**
```http
POST /optimize-energy HTTP/1.1
Host: localhost:8080
Content-Type: application/json

{
  "scenario_id": "SAMPLE-01",
  "operator_notes": [
    "Facilities will wash the rooftop solar panels from noon until 2 PM. During cleaning, usable solar should be treated as roughly 25% of the forecast.",
    "The sports office moved next month's registration deadline."
  ],
  "hours": [
    { "hour": 0, "demand_kwh": 90, "solar_kwh": 0, "tariff_bdt_per_kwh": 6 },
    { "hour": 1, "demand_kwh": 85, "solar_kwh": 0, "tariff_bdt_per_kwh": 6 },
    { "hour": 2, "demand_kwh": 80, "solar_kwh": 0, "tariff_bdt_per_kwh": 5 },
    { "hour": 3, "demand_kwh": 80, "solar_kwh": 0, "tariff_bdt_per_kwh": 5 },
    { "hour": 4, "demand_kwh": 85, "solar_kwh": 0, "tariff_bdt_per_kwh": 5 },
    { "hour": 5, "demand_kwh": 95, "solar_kwh": 0, "tariff_bdt_per_kwh": 6 },
    { "hour": 6, "demand_kwh": 110, "solar_kwh": 5, "tariff_bdt_per_kwh": 8 },
    { "hour": 7, "demand_kwh": 130, "solar_kwh": 20, "tariff_bdt_per_kwh": 10 },
    { "hour": 8, "demand_kwh": 150, "solar_kwh": 50, "tariff_bdt_per_kwh": 12 },
    { "hour": 9, "demand_kwh": 165, "solar_kwh": 90, "tariff_bdt_per_kwh": 14 },
    { "hour": 10, "demand_kwh": 175, "solar_kwh": 130, "tariff_bdt_per_kwh": 16 },
    { "hour": 11, "demand_kwh": 180, "solar_kwh": 160, "tariff_bdt_per_kwh": 16 },
    { "hour": 12, "demand_kwh": 185, "solar_kwh": 180, "tariff_bdt_per_kwh": 15 },
    { "hour": 13, "demand_kwh": 180, "solar_kwh": 170, "tariff_bdt_per_kwh": 14 },
    { "hour": 14, "demand_kwh": 170, "solar_kwh": 140, "tariff_bdt_per_kwh": 13 },
    { "hour": 15, "demand_kwh": 165, "solar_kwh": 90, "tariff_bdt_per_kwh": 14 },
    { "hour": 16, "demand_kwh": 170, "solar_kwh": 45, "tariff_bdt_per_kwh": 18 },
    { "hour": 17, "demand_kwh": 185, "solar_kwh": 10, "tariff_bdt_per_kwh": 22 },
    { "hour": 18, "demand_kwh": 205, "solar_kwh": 0, "tariff_bdt_per_kwh": 28 },
    { "hour": 19, "demand_kwh": 215, "solar_kwh": 0, "tariff_bdt_per_kwh": 30 },
    { "hour": 20, "demand_kwh": 205, "solar_kwh": 0, "tariff_bdt_per_kwh": 26 },
    { "hour": 21, "demand_kwh": 175, "solar_kwh": 0, "tariff_bdt_per_kwh": 18 },
    { "hour": 22, "demand_kwh": 135, "solar_kwh": 0, "tariff_bdt_per_kwh": 10 },
    { "hour": 23, "demand_kwh": 105, "solar_kwh": 0, "tariff_bdt_per_kwh": 7 }
  ],
  "battery": {
    "capacity_kwh": 220,
    "initial_energy_kwh": 110,
    "minimum_energy_kwh": 40,
    "max_charge_kwh_per_hour": 50,
    "max_discharge_kwh_per_hour": 50
  }
}
```

**Response (HTTP 200 OK):**
```json
{
  "scenario_id": "SAMPLE-01",
  "directive_interpretation": [
    {
      "note_index": 0,
      "applies": true,
      "directive_type": "solar_reduction",
      "structured_adjustment": {
        "hours": [12, 13],
        "factor": 0.25
      },
      "explanation": "Solar availability is reduced to 25% during the specified hours [12, 13]."
    },
    {
      "note_index": 1,
      "applies": false,
      "directive_type": "no_op",
      "structured_adjustment": null,
      "explanation": "This note does not affect today's 24-hour energy schedule."
    }
  ],
  "hourly_plan": [
    { "hour": 0, "grid_kwh": 90.0, "solar_used_kwh": 0.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 110.0 },
    { "hour": 1, "grid_kwh": 45.0, "solar_used_kwh": 0.0, "battery_action": "discharge", "battery_kwh": 40.0, "battery_energy_after_kwh": 70.0 },
    { "hour": 2, "grid_kwh": 130.0, "solar_used_kwh": 0.0, "battery_action": "charge", "battery_kwh": 50.0, "battery_energy_after_kwh": 120.0 },
    { "hour": 3, "grid_kwh": 130.0, "solar_used_kwh": 0.0, "battery_action": "charge", "battery_kwh": 50.0, "battery_energy_after_kwh": 170.0 },
    { "hour": 4, "grid_kwh": 135.0, "solar_used_kwh": 0.0, "battery_action": "charge", "battery_kwh": 50.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 5, "grid_kwh": 95.0, "solar_used_kwh": 0.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 6, "grid_kwh": 105.0, "solar_used_kwh": 5.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 7, "grid_kwh": 110.0, "solar_used_kwh": 20.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 8, "grid_kwh": 100.0, "solar_used_kwh": 50.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 9, "grid_kwh": 75.0, "solar_used_kwh": 90.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 10, "grid_kwh": 0.0, "solar_used_kwh": 130.0, "battery_action": "discharge", "battery_kwh": 45.0, "battery_energy_after_kwh": 175.0 },
    { "hour": 11, "grid_kwh": 0.0, "solar_used_kwh": 160.0, "battery_action": "discharge", "battery_kwh": 20.0, "battery_energy_after_kwh": 155.0 },
    { "hour": 12, "grid_kwh": 90.0, "solar_used_kwh": 45.0, "battery_action": "discharge", "battery_kwh": 50.0, "battery_energy_after_kwh": 105.0 },
    { "hour": 13, "grid_kwh": 152.5, "solar_used_kwh": 42.5, "battery_action": "charge", "battery_kwh": 15.0, "battery_energy_after_kwh": 120.0 },
    { "hour": 14, "grid_kwh": 80.0, "solar_used_kwh": 140.0, "battery_action": "charge", "battery_kwh": 50.0, "battery_energy_after_kwh": 170.0 },
    { "hour": 15, "grid_kwh": 125.0, "solar_used_kwh": 90.0, "battery_action": "charge", "battery_kwh": 50.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 16, "grid_kwh": 125.0, "solar_used_kwh": 45.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 220.0 },
    { "hour": 17, "grid_kwh": 145.0, "solar_used_kwh": 10.0, "battery_action": "discharge", "battery_kwh": 30.0, "battery_energy_after_kwh": 190.0 },
    { "hour": 18, "grid_kwh": 155.0, "solar_used_kwh": 0.0, "battery_action": "discharge", "battery_kwh": 50.0, "battery_energy_after_kwh": 140.0 },
    { "hour": 19, "grid_kwh": 165.0, "solar_used_kwh": 0.0, "battery_action": "discharge", "battery_kwh": 50.0, "battery_energy_after_kwh": 90.0 },
    { "hour": 20, "grid_kwh": 155.0, "solar_used_kwh": 0.0, "battery_action": "discharge", "battery_kwh": 50.0, "battery_energy_after_kwh": 40.0 },
    { "hour": 21, "grid_kwh": 175.0, "solar_used_kwh": 0.0, "battery_action": "idle", "battery_kwh": 0.0, "battery_energy_after_kwh": 40.0 },
    { "hour": 22, "grid_kwh": 155.0, "solar_used_kwh": 0.0, "battery_action": "charge", "battery_kwh": 20.0, "battery_energy_after_kwh": 60.0 },
    { "hour": 23, "grid_kwh": 155.0, "solar_used_kwh": 0.0, "battery_action": "charge", "battery_kwh": 50.0, "battery_energy_after_kwh": 110.0 }
  ],
  "total_grid_kwh": 2692.5,
  "total_cost_bdt": 38365.0,
  "peak_grid_kwh": 175.0,
  "plan_summary": "Optimized 24-hour campus energy schedule for scenario SAMPLE-01. Total grid import: 2692.5 kWh, total cost: 38365 BDT, peak grid import: 175.0 kWh. Satisfies 1 active directive(s) and maintains end-of-day battery neutrality."
}
```

---

## Evaluation Checklist & Compliance

- [x] **Backend Only**: Zero frontend UI, pure HTTP REST API.
- [x] **Endpoints**: `GET /health` and `POST /optimize-energy`.
- [x] **Strict JSON Schema**: Exact camel_case/snake_case mapping per hackathon specification.
- [x] **Paraphrase-Tolerant LLM**: Robust interpreter abstraction supporting OpenAI, Azure OpenAI, Local models, and NLP fallback.
- [x] **Deterministic Guardrails**: Validates types, ranges, sorted ascending hours, solar factor, and prevents invented parameters.
- [x] **Mathematical Optimization**: Google OR-Tools GLOP Linear Programming solver minimizing total grid cost under battery and directive constraints.
- [x] **End-of-Day Neutrality**: $E_{23} = E_{initial}$ guaranteed.
- [x] **Hourly Energy Balance**: $\text{Grid} + \text{Solar} + \text{Discharge} = \text{Demand} + \text{Charge}$ verified for all 24 hours.
- [x] **All 10 Sample Cases Passing**: 100% test pass rate on official benchmark pack.
- [x] **Docker Ready**: Multi-stage Linux container with non-root security.
- [x] **Clean Error Handling**: 400 Bad Request, 422 Unprocessable Entity, and 500 without leaking secrets.
