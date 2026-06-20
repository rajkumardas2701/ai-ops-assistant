namespace AiOps.Api.Budget;

/// <summary>Tracks and enforces how many tokens a user may consume per day.</summary>
public interface ITokenBudget
{
    /// <summary>Tokens the user has left for the current UTC day.</summary>
    int GetRemaining(string userId);

    /// <summary>Records tokens consumed by the user.</summary>
    void Consume(string userId, int tokens);
}
