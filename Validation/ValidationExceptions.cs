namespace CampusGrid.Validation;

public class InvalidSemanticInputException : Exception
{
    public List<string> ValidationErrors { get; }

    public InvalidSemanticInputException(string message) : base(message)
    {
        ValidationErrors = new List<string> { message };
    }

    public InvalidSemanticInputException(string message, IEnumerable<string> errors) : base(message)
    {
        ValidationErrors = errors.ToList();
    }
}

public class DirectiveValidationException : InvalidSemanticInputException
{
    public DirectiveValidationException(string message) : base(message)
    {
    }

    public DirectiveValidationException(string message, IEnumerable<string> errors) : base(message, errors)
    {
    }
}

public class InfeasibleScenarioException : InvalidSemanticInputException
{
    public InfeasibleScenarioException(string message) : base(message)
    {
    }
}
