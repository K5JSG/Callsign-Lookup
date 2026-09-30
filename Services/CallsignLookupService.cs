namespace CallsignLookup.Services
{
    public sealed class LookupResult
    {
        public required QrzCallsignRecord Qrz { get; init; }

        // Null when QRZ gave neither a lat/lon nor a usable grid square.
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public string LocationSource { get; init; } = "";

        public string GridSquare { get; init; } = "";
        public CountyMatch? County { get; init; }
        public int? CqZone { get; init; }
        public int? ItuZone { get; init; }
        public ArrlSectionMatch? ArrlSection { get; init; }
    }

    // The whole lookup: callsign -> QRZ record -> a location -> grid square,
    // county, CQ/ITU zone and ARRL section, all computed locally from that
    // location rather than trusted from QRZ's own (user-entered) fields.
    public sealed class CallsignLookupService(QrzService qrz)
    {
        public async Task<LookupResult> LookupAsync(string callsign, CancellationToken cancellationToken = default)
        {
            var record = await qrz.LookupAsync(callsign.Trim().ToUpperInvariant(), cancellationToken);
            return Resolve(record);
        }

        // Everything after the QRZ call - public so it's testable offline.
        public static LookupResult Resolve(QrzCallsignRecord record)
        {
            double? lat = record.Latitude, lon = record.Longitude;
            string source;

            if (lat.HasValue && lon.HasValue)
            {
                source = "QRZ lat/long" + (record.GeoLoc.Length > 0 ? $" (from {DescribeGeoLoc(record.GeoLoc)})" : "");
            }
            else if (Maidenhead.TryGetCenter(record.Grid, out double gridLat, out double gridLon))
            {
                // No lat/long (typically an account without an XML subscription) -
                // the center of the QRZ grid square is the next best thing.
                lat = gridLat;
                lon = gridLon;
                source = $"Center of QRZ grid {record.Grid} (approximate - no lat/long on the QRZ record)";
            }
            else
            {
                return new LookupResult { Qrz = record, LocationSource = "None - QRZ record has no lat/long or grid square" };
            }

            // Counties only for US stations - and for those, always one (the
            // nearest, if the point is just outside every outline). Anywhere
            // else, Canada included, has no county.
            CountyMatch? county = null;
            ArrlSectionMatch? section = null;
            if (record.IsUnitedStates)
            {
                county = CountyLookupService.FindCounty(lat.Value, lon.Value, record.State);
                if (county != null) section = ArrlSectionService.FromUsCounty(county);
            }
            else if (record.IsCanadian && record.State.Length > 0)
            {
                section = ArrlSectionService.FromCanadianStation(record.State, lat.Value, lon.Value);
            }

            return new LookupResult
            {
                Qrz = record,
                Latitude = lat,
                Longitude = lon,
                LocationSource = source,
                GridSquare = Maidenhead.ToGridSquare(lat.Value, lon.Value),
                County = county,
                CqZone = ZoneLookupService.FindCqZone(lat.Value, lon.Value),
                ItuZone = ZoneLookupService.FindItuZone(lat.Value, lon.Value),
                ArrlSection = section,
            };
        }

        private static string DescribeGeoLoc(string geoLoc) => geoLoc.ToLowerInvariant() switch
        {
            "user" => "user-entered",
            "geocode" => "geocoded address",
            "grid" => "grid square",
            "zip" => "ZIP code - approximate",
            "state" => "state - approximate",
            "dxcc" => "country - very approximate",
            _ => geoLoc,
        };
    }
}
