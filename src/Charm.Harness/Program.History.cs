using System.Security.Cryptography;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S89 — WHERE THE GAME MEETS ITS SAVE FILE.
//
//  Two small jobs live here and nothing else: turning a world into the
//  fingerprint that binds a history to it, and reading the `--history` argument
//  off a command line.
//
//  ★ THE HISTORY IS NAMED, NEVER DEFAULTED. There is deliberately no fallback
//  path. A career is a thing Emmett names and knows the location of; a hidden
//  file that appears next to the binary the first time a season is run is how
//  somebody ends up with three careers they cannot tell apart, and how a
//  throwaway test run permanently burns four thousand person numbers out of a
//  real career. No argument means no history, no file, no allocator, nothing
//  touched — which is exactly how every session before this one behaved.
//
//  ★ WHY THE FINGERPRINT IS THE WHOLE WORLD, not a chosen subset. A history is
//  only meaningful against the league it was built from, and guessing which
//  fields "can affect generation" is precisely the kind of omission that bites
//  three seasons later when somebody edits a prestige value and the careers
//  quietly continue against a different league. The world is small and does not
//  change, so hashing all of it costs nothing and cannot be wrong. A future
//  format change defines sha256-v2 rather than redefining what v1 meant.
// ============================================================================

internal static partial class Program
{
    private const string HistoryArgFlag = "--history";

    /// <summary>The world's canonical fingerprint, self-describing so a later scheme can
    /// be told apart from this one at a glance rather than by length.</summary>
    private static string WorldFingerprint(WorldFile world)
    {
        var hash = SHA256.HashData(CanonicalWorldBytes(world));
        return "sha256-v1:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Pull `--history &lt;path&gt;` out of a command line, or null for legacy mode.
    /// Scanned as a NAMED pair rather than a position, because `season` already spends its
    /// fourth slot on the minutes floor.</summary>
    private static string? ParseHistoryArg(string[] args, int firstOptional)
    {
        for (var i = firstOptional; i < args.Length; i++)
        {
            if (!string.Equals(args[i], HistoryArgFlag, StringComparison.Ordinal)) continue;
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new HistoryException(HistoryError.PathIsDirectory,
                    $"{HistoryArgFlag} needs a file path after it. There is no default history path — " +
                    "a career is named explicitly.");
            return args[i + 1];
        }
        return null;
    }

    /// <summary>True for an argument that belongs to the history flag, so a positional
    /// scan (the minutes floor) skips over it instead of trying to parse it.</summary>
    private static bool IsHistoryArgAt(string[] args, int index)
    {
        if (string.Equals(args[index], HistoryArgFlag, StringComparison.Ordinal)) return true;
        return index > 0 && string.Equals(args[index - 1], HistoryArgFlag, StringComparison.Ordinal);
    }

    /// <summary>Open the history for a run, bound to this world. Returns null in legacy
    /// mode — no file is read, no folder is touched, no allocator exists.</summary>
    private static HistoryStore? OpenHistoryFor(WorldFile world, string? historyPath)
        => historyPath is null ? null : HistoryStore.Open(historyPath, WorldFingerprint(world));
}
