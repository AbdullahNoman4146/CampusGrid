using System.Globalization;
using System.Text.RegularExpressions;
using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;

namespace CampusGrid.LLM;

public class SemanticRuleFallbackInterpreter
{
    private static readonly Regex TimeWindowRegex = new(
        @"(?:from|between)\s+(?<start>(?:\d{1,2}(?::\d{2})?\s*(?:am|pm)?|noon|midnight))\s+(?:until|to|and)\s+(?<end>(?:\d{1,2}(?::\d{2})?\s*(?:am|pm)?|noon|midnight))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SingleHourRegex = new(
        @"\b(?:hour|at)\s+(?<hour>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SolarReductionKeywords = new(
        @"\b(?:solar|rooftop|pv|panel|panels|photovoltaic|sunlight|generation)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BatteryReserveKeywords = new(
        @"\b(?:reserve|stored in the battery|remain in the battery|battery capacity|emergency operations|emergency services|data center requires|backup)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NoChargeKeywords = new(
        @"\b(?:charger|charging)\b.*?\b(?:isolated|unavailable|offline|disabled|maintenance|disconnected)\b|\b(?:do\s+not|must\s+not|cannot|stop)\s+charge\b|\b(?:no\s+charg(?:e|ing))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NoDischargeKeywords = new(
        @"\b(?:discharge|discharging)\b.*?\b(?:isolated|unavailable|offline|disabled|prohibited|stopped)\b|\b(?:must\s+not|do\s+not|cannot|stop)\s+discharge\b|\b(?:no\s+discharg(?:e|ing))\b|\b(?:protection|relay)\s+testing\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MaxGridKeywords = new(
        @"\b(?:grid import must not exceed|transformer limit|grid intake stay at or below|feeder|substation is constrained|grid cap|grid limit|import limit)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DirectiveInterpretationDto Interpret(string note, int noteIndex, Battery? battery)
    {
        var cleanedNote = note?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanedNote))
        {
            return CreateNoOp(noteIndex, "Empty operator note provided.");
        }

        // Try extracting time window
        var hours = ExtractHours(cleanedNote);

        // 1. Check for Solar Reduction
        if (SolarReductionKeywords.IsMatch(cleanedNote) &&
            (cleanedNote.Contains("reduc", StringComparison.OrdinalIgnoreCase) ||
             cleanedNote.Contains("drop", StringComparison.OrdinalIgnoreCase) ||
             cleanedNote.Contains("fall", StringComparison.OrdinalIgnoreCase) ||
             cleanedNote.Contains("wash", StringComparison.OrdinalIgnoreCase) ||
             cleanedNote.Contains("cleaning", StringComparison.OrdinalIgnoreCase) ||
             cleanedNote.Contains("cloud", StringComparison.OrdinalIgnoreCase) ||
             cleanedNote.Contains("leave about", StringComparison.OrdinalIgnoreCase) ||
             cleanedNote.Contains("treated as", StringComparison.OrdinalIgnoreCase)))
        {
            var factor = ExtractSolarFactor(cleanedNote);
            if (hours.Count == 0)
            {
                // default to midday hours if unspecified
                hours = new List<int> { 11, 12, 13 };
            }

            return new DirectiveInterpretationDto
            {
                NoteIndex = noteIndex,
                Applies = true,
                DirectiveType = DirectiveType.SolarReduction,
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = hours,
                    Factor = Math.Round(factor, 4)
                },
                Explanation = $"Solar availability is reduced to {factor * 100:G0}% during the specified hours [{string.Join(", ", hours)}]."
            };
        }

        // 2. Check for No Charge Window
        if (NoChargeKeywords.IsMatch(cleanedNote))
        {
            if (hours.Count > 0)
            {
                return new DirectiveInterpretationDto
                {
                    NoteIndex = noteIndex,
                    Applies = true,
                    DirectiveType = DirectiveType.NoChargeWindow,
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = hours
                    },
                    Explanation = $"Battery charging is disabled during the specified hours [{string.Join(", ", hours)}]."
                };
            }
        }

        // 3. Check for No Discharge Window
        if (NoDischargeKeywords.IsMatch(cleanedNote))
        {
            if (hours.Count > 0)
            {
                return new DirectiveInterpretationDto
                {
                    NoteIndex = noteIndex,
                    Applies = true,
                    DirectiveType = DirectiveType.NoDischargeWindow,
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = hours
                    },
                    Explanation = $"Battery discharging is disabled during the specified hours [{string.Join(", ", hours)}]."
                };
            }
        }

        // 4. Check for Minimum Battery Reserve
        if (BatteryReserveKeywords.IsMatch(cleanedNote) ||
            (cleanedNote.Contains("battery", StringComparison.OrdinalIgnoreCase) &&
             (cleanedNote.Contains("reserve", StringComparison.OrdinalIgnoreCase) ||
              cleanedNote.Contains("least", StringComparison.OrdinalIgnoreCase))))
        {
            var reserveKwh = ExtractBatteryReserve(cleanedNote, battery);
            if (reserveKwh.HasValue && hours.Count > 0)
            {
                return new DirectiveInterpretationDto
                {
                    NoteIndex = noteIndex,
                    Applies = true,
                    DirectiveType = DirectiveType.MinimumBatteryReserve,
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = hours,
                        MinimumEnergyKwh = Math.Round(reserveKwh.Value, 2)
                    },
                    Explanation = $"A minimum battery reserve of {reserveKwh.Value:G0} kWh is required during hours [{string.Join(", ", hours)}]."
                };
            }
        }

        // 5. Check for Max Grid Window
        if (MaxGridKeywords.IsMatch(cleanedNote) ||
            (cleanedNote.Contains("grid", StringComparison.OrdinalIgnoreCase) &&
             (cleanedNote.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
              cleanedNote.Contains("exceed", StringComparison.OrdinalIgnoreCase) ||
              cleanedNote.Contains("below", StringComparison.OrdinalIgnoreCase) ||
              cleanedNote.Contains("cap", StringComparison.OrdinalIgnoreCase))))
        {
            var maxGridKwh = ExtractMaxGridKwh(cleanedNote);
            if (maxGridKwh.HasValue && hours.Count > 0)
            {
                return new DirectiveInterpretationDto
                {
                    NoteIndex = noteIndex,
                    Applies = true,
                    DirectiveType = DirectiveType.MaxGridWindow,
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = hours,
                        MaxGridKwh = Math.Round(maxGridKwh.Value, 2)
                    },
                    Explanation = $"Campus grid import is capped at {maxGridKwh.Value:G0} kWh during hours [{string.Join(", ", hours)}]."
                };
            }
        }

        // 6. Default to No-Op for unrelated or non-actionable notes
        return CreateNoOp(noteIndex, "This note does not affect today's 24-hour energy schedule.");
    }

    private static DirectiveInterpretationDto CreateNoOp(int noteIndex, string explanation)
    {
        return new DirectiveInterpretationDto
        {
            NoteIndex = noteIndex,
            Applies = false,
            DirectiveType = DirectiveType.NoOp,
            StructuredAdjustment = null,
            Explanation = explanation
        };
    }

    public static List<int> ExtractHours(string text)
    {
        var match = TimeWindowRegex.Match(text);
        if (match.Success)
        {
            var startStr = match.Groups["start"].Value.Trim();
            var endStr = match.Groups["end"].Value.Trim();

            int startHour = ParseTimeToHour(startStr, isEnd: false);
            int endHour = ParseTimeToHour(endStr, isEnd: true);

            // Time windows are start-inclusive and end-exclusive
            var list = new List<int>();
            if (startHour < endHour)
            {
                for (int h = startHour; h < endHour && h < 24; h++)
                {
                    list.Add(h);
                }
            }
            else if (startHour > endHour)
            {
                // wraps midnight if applicable
                for (int h = startHour; h < 24; h++) list.Add(h);
                for (int h = 0; h < endHour; h++) list.Add(h);
            }
            return list;
        }

        var singleMatch = SingleHourRegex.Match(text);
        if (singleMatch.Success && int.TryParse(singleMatch.Groups["hour"].Value, out int sh) && sh >= 0 && sh < 24)
        {
            return new List<int> { sh };
        }

        return new List<int>();
    }

    public static int ParseTimeToHour(string timeStr, bool isEnd)
    {
        var s = timeStr.Trim().ToLowerInvariant();

        if (s == "noon") return 12;
        if (s == "midnight") return isEnd ? 24 : 0;

        bool isPm = s.EndsWith("pm");
        bool isAm = s.EndsWith("am");

        s = s.Replace("am", "").Replace("pm", "").Trim();

        int hour = 0;
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            int.TryParse(parts[0], out hour);
        }
        else
        {
            int.TryParse(s, out hour);
        }

        if (isPm && hour < 12)
        {
            hour += 12;
        }
        else if (isAm && hour == 12)
        {
            hour = 0;
        }

        return hour;
    }

    private static double ExtractSolarFactor(string text)
    {
        // 1. "X% reduction" or "reduced by X%" or "drop by X%"
        var reductionMatch = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%\s*reduction|reduc(?:ed|tion)\s*(?:by|of)\s*(\d+(?:\.\d+)?)\s*%|drop(?:s|ped)?\s*by\s*(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
        if (reductionMatch.Success)
        {
            string valStr = !string.IsNullOrEmpty(reductionMatch.Groups[1].Value) ? reductionMatch.Groups[1].Value :
                            !string.IsNullOrEmpty(reductionMatch.Groups[2].Value) ? reductionMatch.Groups[2].Value :
                            reductionMatch.Groups[3].Value;

            if (double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double reductionPct))
            {
                return Math.Clamp(1.0 - (reductionPct / 100.0), 0.0, 1.0);
            }
        }

        // 2. "drop to X%" or "treated as roughly X%" or "treated as X%" or "leave X%" or "about X%"
        var remainingMatch = Regex.Match(text, @"(?:drop(?:s|ped)?\s*to\s*(?:about)?|treated\s*as\s*(?:roughly)?|leave\s*(?:about)?|about)\s*(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
        if (remainingMatch.Success && double.TryParse(remainingMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double remainingPct))
        {
            return Math.Clamp(remainingPct / 100.0, 0.0, 1.0);
        }

        // 3. "half"
        if (Regex.IsMatch(text, @"\b(?:half|50%)\b", RegexOptions.IgnoreCase))
        {
            return 0.5;
        }

        // 4. "quarter"
        if (Regex.IsMatch(text, @"\b(?:quarter|25%)\b", RegexOptions.IgnoreCase))
        {
            return 0.25;
        }

        // 5. Generic percentage match
        var generalPct = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
        if (generalPct.Success && double.TryParse(generalPct.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double genPct))
        {
            if (text.Contains("reduc", StringComparison.OrdinalIgnoreCase) || text.Contains("cut", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Clamp(1.0 - (genPct / 100.0), 0.0, 1.0);
            }
            return Math.Clamp(genPct / 100.0, 0.0, 1.0);
        }

        // Default factor if note mentions solar drop but no number
        return 0.5;
    }

    private static double? ExtractBatteryReserve(string text, Battery? battery)
    {
        // 1. Percentage of capacity: "50% of the battery capacity"
        var pctMatch = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%\s*(?:of\s*(?:the\s*)?(?:battery\s*)?capacity)?", RegexOptions.IgnoreCase);
        if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
        {
            if (text.Contains("capacity", StringComparison.OrdinalIgnoreCase) && battery != null && battery.CapacityKwh > 0)
            {
                return (pct / 100.0) * battery.CapacityKwh;
            }
        }

        // 2. Absolute kWh: "at least 120 kWh" or "80 kWh"
        var kwhMatch = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*kwh", RegexOptions.IgnoreCase);
        if (kwhMatch.Success && double.TryParse(kwhMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double kwh))
        {
            return kwh;
        }

        return null;
    }

    private static double? ExtractMaxGridKwh(string text)
    {
        var kwhMatch = Regex.Match(text, @"(?:exceed|limit\s*is|stay\s*at\s*or\s*below|cap(?:ped)?\s*(?:at)?)\s*(\d+(?:\.\d+)?)\s*kwh", RegexOptions.IgnoreCase);
        if (kwhMatch.Success && double.TryParse(kwhMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double kwh))
        {
            return kwh;
        }

        var generalKwh = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*kwh", RegexOptions.IgnoreCase);
        if (generalKwh.Success && double.TryParse(generalKwh.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double genKwh))
        {
            return genKwh;
        }

        return null;
    }
}
