namespace PredictionLeague.Application.Abstractions.Leagues;

// Produces a short, human-transcribable league invite code (FR-007) — a friend types it to join
// in S-05. Collision handling lives in the implementation, not in the controller.
public interface IInviteCodeGenerator
{
    // Throws InvalidOperationException when it cannot find a free code within its retry budget.
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
