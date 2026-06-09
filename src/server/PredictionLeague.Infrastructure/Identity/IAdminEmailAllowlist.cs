namespace PredictionLeague.Infrastructure.Identity;

// Config-driven check used by AuthController to promote IsGlobalAdmin on sign-in.
public interface IAdminEmailAllowlist
{
    bool IsAdmin(string? email);
}
