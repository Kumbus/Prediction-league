using System.Security.Cryptography;
using PredictionLeague.Application.Abstractions.Leagues;
using PredictionLeague.Application.Abstractions.Persistence;

namespace PredictionLeague.Infrastructure.Leagues;

// 8 crypto-random characters from an alphabet with no I/L/O/0/1, so a code read aloud or copied
// off a screen transcribes cleanly. Well under League.InviteCode's HasMaxLength(32).
//
// The existence probe is an optimization only — it is racy by construction. The unique index on
// League.InviteCode is the real guarantee, so the caller must still handle a save-time collision.
public class RandomInviteCodeGenerator : IInviteCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;
    private const int MaxAttempts = 5;

    private readonly ILeagueRepository _leagues;

    public RandomInviteCodeGenerator(ILeagueRepository leagues)
    {
        _leagues = leagues;
    }

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = NewCode();
            if (!await _leagues.InviteCodeExistsAsync(candidate, cancellationToken))
                return candidate;
        }

        throw new InvalidOperationException(
            $"Could not generate a free invite code in {MaxAttempts} attempts.");
    }

    private static string NewCode()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
