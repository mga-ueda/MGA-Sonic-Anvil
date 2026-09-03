namespace MgaSonicAnvil.Domain;

internal static class MarkerMoves
{
    public static long ResolveGroupDelta(
        IReadOnlyList<long> movingSorted,
        long desiredDelta,
        HashSet<long> occupiedOthers,
        long maxFrame)
    {
        if (movingSorted.Count == 0 || desiredDelta == 0)
        {
            return 0;
        }

        var first = movingSorted[0];
        var last = movingSorted[^1];
        var delta = Math.Clamp(desiredDelta, -first, maxFrame - last);
        if (delta == 0 || occupiedOthers.Count == 0)
        {
            return delta;
        }

        var forbidden = new HashSet<long>();
        foreach (var other in occupiedOthers)
        {
            foreach (var source in movingSorted)
            {
                forbidden.Add(other - source);
            }
        }

        var step = delta > 0 ? 1L : -1L;
        while (delta != 0 && forbidden.Contains(delta))
        {
            delta -= step;
        }

        return delta;
    }
}
