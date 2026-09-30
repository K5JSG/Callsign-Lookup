namespace CallsignLookup.Services
{
    // Shared geometry for the offline lookups (counties, CQ/ITU zones, Ontario
    // census divisions). Every shape is stored the same way: a list of
    // polygons, each a list of rings, each ring a flat [lon,lat,lon,lat,...]
    // array. A polygon's first ring is its outer boundary; any further rings
    // are holes.
    internal static class PolygonMath
    {
        public static bool Contains(List<List<double[]>> polys, double lon, double lat)
        {
            foreach (var poly in polys)
            {
                // Parity across every ring of the polygon (outer boundary plus
                // holes): odd means genuinely inside, which handles holes
                // without needing to know which ring is which.
                int ringsContainingPoint = 0;
                foreach (var ring in poly)
                {
                    if (RingContains(ring, lon, lat)) ringsContainingPoint++;
                }
                if (ringsContainingPoint % 2 == 1) return true;
            }
            return false;
        }

        // Squared distance (in degrees) from the point to the nearest edge of
        // any ring - only ever compared against other results from this, so
        // the square root is never needed.
        public static double DistanceSquared(List<List<double[]>> polys, double lon, double lat)
        {
            double best = double.MaxValue;
            foreach (var poly in polys)
            {
                foreach (var ring in poly)
                {
                    int n = ring.Length / 2;
                    for (int i = 0; i < n; i++)
                    {
                        int next = (i + 1) % n;
                        best = Math.Min(best, PointToSegmentDistanceSquared(
                            lon, lat,
                            ring[2 * i], ring[2 * i + 1],
                            ring[2 * next], ring[2 * next + 1]));
                    }
                }
            }
            return best;
        }

        // True if the shape lies (at least partly) due north of the point:
        // some edge crosses the point's meridian at a higher latitude.
        public static bool HasEdgeDueNorth(List<List<double[]>> polys, double lon, double lat)
        {
            foreach (var poly in polys)
            {
                foreach (var ring in poly)
                {
                    int n = ring.Length / 2;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        double xi = ring[2 * i], yi = ring[2 * i + 1];
                        double xj = ring[2 * j], yj = ring[2 * j + 1];
                        if ((xi > lon) == (xj > lon)) continue;

                        double crossingLat = yi + (lon - xi) * (yj - yi) / (xj - xi);
                        if (crossingLat > lat) return true;
                    }
                }
            }
            return false;
        }

        private static bool RingContains(double[] ring, double lon, double lat)
        {
            int n = ring.Length / 2;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = ring[2 * i], yi = ring[2 * i + 1];
                double xj = ring[2 * j], yj = ring[2 * j + 1];

                if (((yi > lat) != (yj > lat)) &&
                    (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static double PointToSegmentDistanceSquared(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            if (dx == 0 && dy == 0)
            {
                double ddx = px - ax, ddy = py - ay;
                return ddx * ddx + ddy * ddy;
            }

            double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1);
            double ex = px - (ax + t * dx), ey = py - (ay + t * dy);
            return ex * ex + ey * ey;
        }
    }
}
