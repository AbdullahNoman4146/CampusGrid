using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;

namespace CampusGrid.LLM;

public interface ILLMInterpreter
{
    Task<DirectiveInterpretationDto> InterpretAsync(
        string note,
        int noteIndex = 0,
        Battery? battery = null,
        CancellationToken cancellationToken = default);
}
