namespace CampusGrid.Models.Dto;

public static class EnergyRequestNormalizer
{
    public static EnergyRequest NormalizeHours(EnergyRequest request)
    {
        return new EnergyRequest
        {
            ScenarioId = request.ScenarioId,
            OperatorNotes = request.OperatorNotes.ToList(),
            Hours = request.Hours.OrderBy(hour => hour.Hour).ToList(),
            Battery = request.Battery
        };
    }
}
