using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;

namespace CampusGrid.LLM;

public interface ILLMInterpreter
{
    Task<IReadOnlyList<DirectiveInterpretationDto>> InterpretAllAsync(
        IReadOnlyList<string> notes,
        Battery battery,
        CancellationToken cancellationToken = default);
}
