using System.Globalization;

namespace Charm.Engine;

// ============================================================================
//  S92 — WHERE THINGS ARE, AND HOW FAR APART.
//
//  Before this file the engine had no concept of place: every school carried a
//  latitude and a longitude since the world layer shipped and NOTHING computed
//  anything with them. This is the ruler. It answers "how far is it from here to
//  there"; it does NOT answer "who is playing", "whose gym is it" or "how loud is
//  the building" — those are the world layer's and the crowd model's jobs.
//
//  ★ THE DEPENDENCY DIRECTION IS THE POINT. This file knows about two numbers on
//  a sphere. It knows nothing about csvs, place ids, schools or conferences. The
//  world layer owns authored places and calls in here; nothing in here ever calls
//  out. That is what keeps a distance function from quietly growing a notion of
//  "domestic" or "campus".
//
//  ★ VALIDATED AT CONSTRUCTION. A GeoCoordinate that exists is a coordinate that
//  is real: no NaN, no infinity, latitude inside [-90,90], longitude inside
//  [-180,180]. So DistanceMiles re-validates NOTHING and loaders do not each grow
//  their own half-checks. `default(GeoCoordinate)` is (0,0) — a real point in the
//  Gulf of Guinea, and therefore still a valid coordinate, which is why the
//  guarantee survives the implicit parameterless constructor C# gives every
//  struct.
//
//  ★ NEGATIVE ZERO IS NORMALISED HERE, at the only door into the type. The world
//  file's fingerprint hashes BYTES, and -0.0 serialises as `-0` while +0.0
//  serialises as `0`. Two worlds that are the same world must not hash
//  differently because someone typed a minus sign in front of a zero.
//
//  ★ NO WALL CLOCK, NO CULTURE, NO CONFIG — same standing rule as CharmCalendar.
//  Nothing in here reads the host machine's date, its locale or a settings file.
// ============================================================================

/// <summary>A point on the earth, guaranteed real by construction. Degrees, because
/// that is what the authored data is in and converting at rest would mean two
/// spellings of the same place.</summary>
public readonly record struct GeoCoordinate
{
    /// <summary>Degrees north of the equator, in [-90, 90].</summary>
    public double LatitudeDegrees { get; }

    /// <summary>Degrees east of the prime meridian, in [-180, 180]. There is deliberately
    /// no implicit wrapping: 181 is a mistake, not a synonym for -179, and silently
    /// accepting it would hide an authoring error in a permanent data file.</summary>
    public double LongitudeDegrees { get; }

    private GeoCoordinate(double latitudeDegrees, double longitudeDegrees)
    {
        // The `+ 0.0` idiom turns -0.0 into +0.0 and leaves every other value alone.
        LatitudeDegrees = latitudeDegrees + 0.0;
        LongitudeDegrees = longitudeDegrees + 0.0;
    }

    /// <summary>True when the pair could name a real point. The predicate is public so a
    /// loader can report a bad row by name instead of catching an exception.</summary>
    public static bool IsValid(double latitudeDegrees, double longitudeDegrees)
        => double.IsFinite(latitudeDegrees)
           && double.IsFinite(longitudeDegrees)
           && latitudeDegrees >= -90.0 && latitudeDegrees <= 90.0
           && longitudeDegrees >= -180.0 && longitudeDegrees <= 180.0;

    /// <summary>The only constructor. Throws rather than clamping: a coordinate outside the
    /// globe is bad data, and a clamp would move a school to the pole and keep going.</summary>
    public static GeoCoordinate Create(double latitudeDegrees, double longitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || !double.IsFinite(longitudeDegrees))
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees),
                FormattableString.Invariant(
                    $"coordinate must be finite (got {latitudeDegrees}, {longitudeDegrees})."));
        if (latitudeDegrees < -90.0 || latitudeDegrees > 90.0)
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), latitudeDegrees,
                "latitude must be in [-90, 90] degrees.");
        if (longitudeDegrees < -180.0 || longitudeDegrees > 180.0)
            throw new ArgumentOutOfRangeException(nameof(longitudeDegrees), longitudeDegrees,
                "longitude must be in [-180, 180] degrees.");
        return new GeoCoordinate(latitudeDegrees, longitudeDegrees);
    }

    /// <summary>Non-throwing form, for loaders that want to name the offending row.</summary>
    public static bool TryCreate(double latitudeDegrees, double longitudeDegrees, out GeoCoordinate coordinate)
    {
        if (!IsValid(latitudeDegrees, longitudeDegrees)) { coordinate = default; return false; }
        coordinate = new GeoCoordinate(latitudeDegrees, longitudeDegrees);
        return true;
    }

    public override string ToString()
        => LatitudeDegrees.ToString("0.####", CultureInfo.InvariantCulture) + ", "
           + LongitudeDegrees.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Great-circle distance on a spherical earth.</summary>
public static class GeoDistance
{
    /// <summary>Mean earth radius in miles. PINNED — this constant is half the definition of
    /// the golden table, and changing it changes every mileage the game has ever printed.
    /// The other half is the haversine form below.</summary>
    public const double EarthMeanRadiusMiles = 3958.7613;

    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Miles between two points, great-circle, spherical earth.
    ///
    /// <para>★ HAVERSINE, not the law of cosines. The law of cosines loses most of its
    /// precision on short distances — and short distances are nearly the whole league;
    /// two schools in Philadelphia are the normal case, not the exotic one.</para>
    ///
    /// <para>★ THE INTERMEDIATE IS CLAMPED TO [0,1] BEFORE THE ROOT. Floating arithmetic
    /// can push it a hair above one near antipodal points, and <c>Asin</c> of 1.0000000000001
    /// is NaN — a distance that silently poisons every average it lands in.</para>
    ///
    /// <para>★ EQUAL COORDINATES RETURN EXACTLY 0.0, by construction rather than by a
    /// special case: identical inputs make both sine terms exactly zero.</para>
    ///
    /// <para>★ THE REVERSE IS NOT PROMISED. Two DIFFERENT coordinates may also return 0.0 if
    /// they are close enough that the difference underflows. That is a property of doubles,
    /// not a defect, and pretending otherwise would mean storing integer microdegrees for no
    /// benefit anyone can see on a basketball page.</para>
    ///
    /// <para>Validity is the coordinate type's guarantee, so nothing here re-checks it.</para></summary>
    public static double DistanceMiles(GeoCoordinate a, GeoCoordinate b)
    {
        var lat1 = a.LatitudeDegrees * DegreesToRadians;
        var lat2 = b.LatitudeDegrees * DegreesToRadians;
        var halfDLat = (lat2 - lat1) * 0.5;
        var halfDLong = (b.LongitudeDegrees - a.LongitudeDegrees) * DegreesToRadians * 0.5;

        var sinLat = Math.Sin(halfDLat);
        var sinLong = Math.Sin(halfDLong);
        var h = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLong * sinLong;

        if (h < 0.0) h = 0.0;
        if (h > 1.0) h = 1.0;

        return 2.0 * EarthMeanRadiusMiles * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>The longest distance the function can return — half way round. Used by the
    /// checks as the upper sanity bound; near-antipodal pairs are ill-conditioned enough
    /// that a mileage bar is meaningless there, but this bound always holds.</summary>
    public static double HalfCircumferenceMiles => Math.PI * EarthMeanRadiusMiles;
}
