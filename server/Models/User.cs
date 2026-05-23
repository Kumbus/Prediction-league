namespace PredictionLeague.Models;

// Account identity. One account predicts across many leagues (FR-002).
public class User
{
    public Guid Id { get; set; }

    // Subject claim from the OAuth provider (FR-001).
    public required string ExternalAuthId { get; set; }

    public required string DisplayName { get; set; }

    public bool IsGlobalAdmin { get; set; }
}
