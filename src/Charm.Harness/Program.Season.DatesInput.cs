using System.Globalization;
using System.Text.Json;

namespace Charm.Harness;

// ============================================================================
//  S108 — THE DATING ORACLE'S INPUT, EMITTED BY THE ENGINE RATHER THAN BY HAND.
//
//  ★ WHY THIS FILE EXISTS. `tools/nonconference_dates_oracle.py` is the SPEC for
//  S106's dating layer and `tools/nonconference_dates_golden.json` is what Phase
//  97 C1c proves the port against, row for row. The oracle reads its world from a
//  JSON file — and until now NOTHING IN THE REPO PRODUCED THAT FILE. S106 built
//  it by hand in a scratch session and the recipe left with the session, so the
//  first time the matching moved, the golden could not be regenerated at all
//  without re-deriving the export from scratch. That is the same failure mode as
//  the lost r4 addendum (O-100): a step that lives only in a chat log is a step
//  that will be redone wrong.
//
//  ★ THIS EMITTER IS NOT AN ORACLE AND MUST NEVER BECOME ONE. It computes
//  nothing. Every field below is COPIED out of a season run — the dated
//  conference slate, the seated event windows, the matcher's pairs, the authored
//  conference games — so the Python side still derives every date independently
//  and the parity check stays a real comparison between two implementations. The
//  moment this file starts deciding something, C1c becomes the engine agreeing
//  with itself.
//
//  ★ THE PAIR ORDER IS PART OF THE ARTIFACT. The oracle indexes games by their
//  position in this list (`pairIndex`), so the pairs are written in the matcher's
//  own emission order and never sorted.
//
//  Usage (the world and seed the goldens are emitted against):
//      dotnet run --project src\Charm.Harness\Charm.Harness.csproj -- \
//          dates-input worlds\stock-d1.world.json 20260720 tools\s106-in.json
//      python tools\nonconference_dates_oracle.py tools\s106-in.json
// ============================================================================

internal static partial class Program
{
    private static int RunDatesInput(string engineConfigPath, string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("usage: dates-input <world.json> <seed> <out.json>");
            Console.WriteLine("  Emits the input file tools/nonconference_dates_oracle.py reads.");
            Console.WriteLine("  Copies a season run's facts; computes nothing.");
            return 1;
        }
        if (!long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            Console.WriteLine($"DATES-INPUT ERROR: seed '{args[2]}' is not a valid integer.");
            return 1;
        }

        WorldFile world;
        try { world = LoadWorld(args[1]); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.WriteLine($"DATES-INPUT ERROR: {ex.Message}");
            return 1;
        }

        var run = RunSeasonCore(world, seed, engineConfigPath, verbose: false);

        var options = new JsonWriterOptions { Indented = true };
        using var stream = File.Create(args[3]);
        using var w = new Utf8JsonWriter(stream, options);

        w.WriteStartObject();
        w.WriteNumber("startYear", SeasonDefaultStartYear);

        // ── The schools, and which league each belongs to ────────────────────────
        w.WriteStartArray("schools");
        foreach (var s in world.Schools.OrderBy(s => s.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("id", s.Id);
            w.WriteNumber("conf", s.ConferenceId);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // ── The leagues. `games` is what tells the oracle who is an Independent:
        //    a zero-game container holds strangers, exactly as the matcher reads it.
        w.WriteStartArray("conferences");
        foreach (var c in world.Conferences.OrderBy(c => c.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("id", c.Id);
            w.WriteNumber("games", c.Games);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // ── The immovable league nights, AS PLAYED. Taken from the schedule the
        //    season actually ran (S96: once host memory can move a venue, a
        //    preflight schedule is a DIFFERENT schedule).
        // ★ Selected by HAVING A DATE, not by a kind string — the identical predicate
        //   DateNonConferenceGames itself uses over the same list. A kind literal here
        //   would be a second spelling of the same rule, free to drift.
        var dated = run.Schedule.Where(g => g.Date is not null).ToList();
        w.WriteStartArray("conferenceGames");
        foreach (var g in dated)
        {
            w.WriteStartObject();
            w.WriteNumber("h", g.HomeId);
            w.WriteNumber("a", g.AwayId);
            w.WriteString("d", g.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // ── The event windows and who sits in them. Authored MM-DD is passed
        //    THROUGH unresolved; the oracle owns resolving it onto the season
        //    spine, which is the half of O-99 this file must not pre-empt.
        w.WriteStartArray("events");
        foreach (var e in run.Events.Seating.Active.OrderBy(e => e.EventId))
        {
            w.WriteStartObject();
            w.WriteString("first", e.FirstDay);
            w.WriteString("last", e.LastDay);
            w.WriteStartArray("seats");
            foreach (var seat in e.Seats.OrderBy(s => s.Seat))
                w.WriteNumberValue(seat.SchoolId);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // ── ★ THE PAIRINGS, IN THE MATCHER'S OWN ORDER. Never sorted: the oracle
        //    and Phase 97 both index by position.
        w.WriteStartArray("pairs");
        foreach (var p in run.Matching.Pairs)
        {
            w.WriteStartObject();
            w.WriteNumber("h", p.HostSchoolId);
            w.WriteNumber("v", p.VisitorSchoolId);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteEndObject();
        w.Flush();

        Console.WriteLine($"DATES-INPUT: {args[3]}");
        Console.WriteLine($"  {world.Schools.Count} schools, {world.Conferences.Count} conferences, " +
                          $"{dated.Count} dated league games, " +
                          $"{run.Events.Seating.Active.Count} active event(s), " +
                          $"{run.Matching.Pairs.Count} pairing(s) in matcher order.");
        Console.WriteLine("  Next: python tools/nonconference_dates_oracle.py " + args[3]);
        return 0;
    }
}
