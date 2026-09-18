namespace CampusGrid.LLM;

public sealed class LLMServiceUnavailableException : Exception
{
    public LLMServiceUnavailableException(string message) : base(message)
    {
    }

    public LLMServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
