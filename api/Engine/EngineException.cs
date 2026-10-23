namespace ExpenseWorkflow.Api.Engine;

public enum EngineError { NotFound, Forbidden, Conflict, Invalid }

public class EngineException(EngineError kind, string message) : Exception(message)
{
    public EngineError Kind { get; } = kind;
}
