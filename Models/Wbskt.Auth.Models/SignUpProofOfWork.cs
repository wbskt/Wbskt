using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Auth.Models;

/// <summary>
/// The sign-up proof of work, shared by the auth host that checks it and the clients that solve
/// it. A solution is any string for which SHA-256 of the UTF-8 bytes of <c>challenge + ":" +
/// solution</c> starts with at least <c>difficulty</c> zero bits. The console solves the same
/// puzzle in the browser, so the definition here and there must not drift.
/// </summary>
public static class SignUpProofOfWork
{
    public static bool Satisfies(string challenge, string solution, int difficulty)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(challenge + ":" + solution), hash);
        return LeadingZeroBits(hash) >= difficulty;
    }

    /// <summary>
    /// Counts up from zero until a decimal counter satisfies the puzzle. About
    /// 2^<paramref name="difficulty"/> hashes on average.
    /// </summary>
    public static string Solve(string challenge, int difficulty, CancellationToken cancellationToken = default)
    {
        for (long counter = 0; ; counter++)
        {
            if ((counter & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var solution = counter.ToString(CultureInfo.InvariantCulture);
            if (Satisfies(challenge, solution, difficulty))
            {
                return solution;
            }
        }
    }

    private static int LeadingZeroBits(ReadOnlySpan<byte> hash)
    {
        var bits = 0;
        foreach (var b in hash)
        {
            if (b != 0)
            {
                return bits + BitOperations.LeadingZeroCount((uint)b) - 24;
            }

            bits += 8;
        }

        return bits;
    }
}
