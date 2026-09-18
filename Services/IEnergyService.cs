using CampusGrid.Models.Dto;

namespace CampusGrid.Services;

public interface IEnergyService
{
    Task<EnergyResponse> OptimizeScenarioAsync(EnergyRequest request, CancellationToken cancellationToken = default);
}
