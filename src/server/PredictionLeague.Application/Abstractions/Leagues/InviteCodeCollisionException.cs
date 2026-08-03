namespace PredictionLeague.Application.Abstractions.Leagues;

// The unique index on League.InviteCode rejected the code — another league claimed it between
// the generator's probe and the insert. The persistence layer translates its provider-specific
// failure into this, so callers can retry with a fresh code without knowing about EF Core.
public class InviteCodeCollisionException : Exception
{
    public InviteCodeCollisionException(string inviteCode, Exception innerException)
        : base($"Invite code '{inviteCode}' is already taken.", innerException)
    {
        InviteCode = inviteCode;
    }

    public string InviteCode { get; }
}
