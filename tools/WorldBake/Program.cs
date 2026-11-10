// WorldBake - the countries for the site's map of boards (site/world.js)
//   WorldBake <ne_110m_admin_0_countries.geojson> <site folder>    writes world.js
//   WorldBake <geojson> --where                                     says which country holds the test points
// the input is Natural Earth's 1:110m countries (public domain, naturalearthdata.com). its borders are the
// ones on the ground; where those are an occupation the map shows the country's own: Crimea is Ukraine's,
// and so is every part of Ukraine (Check below stops the bake if a test point lands anywhere else)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

static class Program
{
    // places that must lie in the country named, on any map this tool writes
    static readonly (string Name, double Lon, double Lat, string Cc)[] Tests = {
        ("Simferopol", 34.10, 44.95, "UA"), ("Sevastopol", 33.52, 44.60, "UA"), ("Yevpatoria", 33.37, 45.20, "UA"),
        ("Dzhankoi", 34.40, 45.71, "UA"), ("Feodosia", 35.38, 45.03, "UA"), ("Kerch peninsula", 36.00, 45.35, "UA"),
        ("Perekop", 33.70, 46.15, "UA"), ("Donetsk", 37.80, 48.00, "UA"), ("Luhansk", 39.30, 48.57, "UA"),
        ("Mariupol", 37.55, 47.10, "UA"), ("Melitopol", 35.37, 46.85, "UA"), ("Kyiv", 30.52, 50.45, "UA"),
        ("Moscow", 37.62, 55.75, "RU"), ("Rostov-on-Don", 39.72, 47.23, "RU"),
    };

    static int Main(string[] args)
    {
        if (args.Length < 2) {
            Console.WriteLine("WorldBake <ne_110m_admin_0_countries.geojson> <site folder> | --where");
            return 1;
        }
        List<Country> all = Read(args[0]);
        Console.WriteLine($"{all.Count} countries read");
        if (args[1] == "--where") {
            Where(all);
            Console.WriteLine("--- after Fix:");
            Fix(all);
            Where(all);
            return 0;
        }
        Fix(all);
        if (!Check(all)) {
            return 2;
        }
        string js = Write(all);
        string to = Path.Combine(args[1], "world.js");
        File.WriteAllText(to, js, new UTF8Encoding(false));
        Console.WriteLine($"{to}: {js.Length / 1024} KB");
        return 0;
    }

    sealed class Country
    {
        public string Cc = "", Name = "";
        public double LabelLon, LabelLat;
        public List<List<(double Lon, double Lat)>> Rings = new List<List<(double Lon, double Lat)>>();   // outer rings and holes, as they came
    }

    static List<Country> Read(string path)
    {
        List<Country> all = new List<Country>();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonElement f in doc.RootElement.GetProperty("features").EnumerateArray()) {
            JsonElement p = f.GetProperty("properties");
            string cc = p.GetProperty("ISO_A2_EH").GetString() ?? "-99";
            if (cc == "-99") {
                cc = p.GetProperty("ISO_A2").GetString() ?? "-99";
            }
            if (cc == "-99") {
                cc = "X" + (p.GetProperty("ADM0_A3").GetString() ?? "??").Substring(0, 1);   // no code: drawn, never counted
            }
            Country c = new Country {
                Cc = cc,
                Name = p.GetProperty("NAME").GetString() ?? cc,
                LabelLon = p.GetProperty("LABEL_X").GetDouble(),
                LabelLat = p.GetProperty("LABEL_Y").GetDouble(),
            };
            JsonElement g = f.GetProperty("geometry");
            string type = g.GetProperty("type").GetString() ?? "";
            JsonElement coords = g.GetProperty("coordinates");
            if (type == "Polygon") {
                AddPolygon(c, coords);
            } else if (type == "MultiPolygon") {
                foreach (JsonElement poly in coords.EnumerateArray()) {
                    AddPolygon(c, poly);
                }
            }
            all.Add(c);
        }
        return all;
    }

    static void AddPolygon(Country c, JsonElement poly)
    {
        foreach (JsonElement ring in poly.EnumerateArray()) {
            List<(double, double)> r = new List<(double, double)>();
            foreach (JsonElement pt in ring.EnumerateArray()) {
                r.Add((pt[0].GetDouble(), pt[1].GetDouble()));
            }
            c.Rings.Add(r);
        }
    }

    // even-odd over every ring of a country: inside an outer ring and not in its hole
    static bool Inside(Country c, double lon, double lat)
    {
        bool inside = false;
        foreach (List<(double Lon, double Lat)> r in c.Rings) {
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) {
                if ((r[i].Lat > lat) != (r[j].Lat > lat) &&
                    lon < (r[j].Lon - r[i].Lon) * (lat - r[i].Lat) / (r[j].Lat - r[i].Lat) + r[i].Lon) {
                    inside = !inside;
                }
            }
        }
        return inside;
    }

    static string Holder(List<Country> all, double lon, double lat)
    {
        List<string> found = new List<string>();
        foreach (Country c in all) {
            if (Inside(c, lon, lat)) {
                found.Add(c.Cc);
            }
        }
        return found.Count == 0 ? "none" : string.Join("+", found);
    }

    static void Where(List<Country> all)
    {
        foreach ((string name, double lon, double lat, string cc) in Tests) {
            Console.WriteLine($"{name,-14} wants {cc}: in {Holder(all, lon, lat)}");
        }
        foreach (Country c in all) {
            if (c.Cc == "UA" || c.Cc == "RU") {
                Console.WriteLine($"{c.Cc} {c.Name}: {c.Rings.Count} rings");
                for (int i = 0; i < c.Rings.Count; i++) {
                    List<(double Lon, double Lat)> r = c.Rings[i];
                    double w = 999, e = -999, s = 999, n = -999;
                    foreach ((double lon, double lat) in r) {
                        w = Math.Min(w, lon); e = Math.Max(e, lon); s = Math.Min(s, lat); n = Math.Max(n, lat);
                    }
                    if (w < 60) {                              // the western part is all that matters here
                        Console.WriteLine($"  ring {i}: {r.Count} points, lon {w:0.00}..{e:0.00}, lat {s:0.00}..{n:0.00}");
                    }
                }
            }
        }
    }

    /* Natural Earth's default file draws the occupation: Crimea is a ring of Russia's (Where shows it, ring 13 of
     * 14 in version 5). it goes back to Ukraine, and joins Ukraine's own ring along the Perekop isthmus, so no
     * border line is left across it. the ring is found by what it holds, Simferopol, not by its number */
    static void Fix(List<Country> all)
    {
        Country? ru = all.Find((c) => c.Cc == "RU"), ua = all.Find((c) => c.Cc == "UA");
        if (ru == null || ua == null) {
            throw new InvalidDataException("no Russia or no Ukraine in the file");
        }
        int crimea = ru.Rings.FindIndex((r) => InRing(r, 34.10, 44.95));
        if (crimea < 0) {
            Console.WriteLine("Crimea is not in Russia's rings: nothing to move");
            return;
        }
        List<(double Lon, double Lat)> ring = ru.Rings[crimea];
        ru.Rings.RemoveAt(crimea);
        int mainland = ua.Rings.FindIndex((r) => InRing(r, 30.52, 50.45));
        List<(double Lon, double Lat)>? joined = mainland < 0 ? null : Join(ua.Rings[mainland], ring);
        if (joined == null) {
            ua.Rings.Add(ring);                             // no edge in common: Crimea as a ring of its own, still Ukraine's
            Console.WriteLine($"Crimea moved to Ukraine as a ring of its own ({ring.Count} points)");
        } else {
            ua.Rings[mainland] = joined;
            Console.WriteLine($"Crimea moved to Ukraine and joined to it: one ring of {joined.Count} points");
        }
    }

    static bool InRing(List<(double Lon, double Lat)> r, double lon, double lat)
    {
        bool inside = false;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) {
            if ((r[i].Lat > lat) != (r[j].Lat > lat) &&
                lon < (r[j].Lon - r[i].Lon) * (lat - r[i].Lat) / (r[j].Lat - r[i].Lat) + r[i].Lon) {
                inside = !inside;
            }
        }
        return inside;
    }

    static double Area(List<(double Lon, double Lat)> r)
    {
        double a = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) {
            a += (r[j].Lon - r[i].Lon) * (r[j].Lat + r[i].Lat);   // the shoelace, its sign is the way round
        }
        return a / 2;
    }

    /* two rings that meet along a stretch of the same points, made one: both the same way round, the edges
     * they share (walked one way in one and the other way in the other) go, the rest is walked as one ring.
     * null when they share no edge, or the rest does not close as one ring */
    static List<(double Lon, double Lat)>? Join(List<(double Lon, double Lat)> a, List<(double Lon, double Lat)> b)
    {
        List<(double Lon, double Lat)> x = Open(a), y = Open(b);
        if (Math.Sign(Area(x)) != Math.Sign(Area(y))) {
            y.Reverse();
        }
        static string Key((double Lon, double Lat) p) => FormattableString.Invariant($"{p.Lon:0.000000},{p.Lat:0.000000}");
        Dictionary<string, (double Lon, double Lat)> point = new Dictionary<string, (double Lon, double Lat)>();
        List<(string From, string To)> edges = new List<(string From, string To)>();
        foreach (List<(double Lon, double Lat)> r in new[] { x, y }) {
            for (int i = 0; i < r.Count; i++) {
                string f = Key(r[i]), t = Key(r[(i + 1) % r.Count]);
                point[f] = r[i];
                edges.Add((f, t));
            }
        }
        HashSet<(string, string)> all = new HashSet<(string, string)>(edges);
        List<(string From, string To)> keep = edges.FindAll((e) => !all.Contains((e.To, e.From)));
        if (keep.Count == edges.Count) {
            return null;
        }
        Dictionary<string, string> next = new Dictionary<string, string>();
        foreach ((string f, string t) in keep) {
            if (next.ContainsKey(f)) {
                return null;                                // a point the outline goes through twice: not one ring
            }
            next[f] = t;
        }
        List<(double Lon, double Lat)> ring = new List<(double Lon, double Lat)>();
        string at = keep[0].From;
        do {
            ring.Add(point[at]);
            if (!next.TryGetValue(at, out string? to) || ring.Count > keep.Count) {
                return null;
            }
            at = to;
        } while (at != keep[0].From);
        if (ring.Count != keep.Count) {
            return null;
        }
        ring.Add(ring[0]);
        return ring;
    }

    static List<(double Lon, double Lat)> Open(List<(double Lon, double Lat)> r)
    {
        List<(double Lon, double Lat)> o = new List<(double Lon, double Lat)>(r);
        if (o.Count > 1 && o[0] == o[o.Count - 1]) {
            o.RemoveAt(o.Count - 1);
        }
        return o;
    }

    static bool Check(List<Country> all)
    {
        bool ok = true;
        foreach ((string name, double lon, double lat, string cc) in Tests) {
            string held = Holder(all, lon, lat);
            if (held != cc) {
                Console.WriteLine($"WRONG: {name} is in {held}, it is {cc}");
                ok = false;
            }
        }
        if (ok) {
            Console.WriteLine($"all {Tests.Length} test points in their own country");
        }
        return ok;
    }

    /* --- the map as the site draws it: Equal Earth (Šavrič, Patterson, Jenny 2018), equal area, so a country's
     * size on it is its size. Antarctica left out, the map cut at 84 N and 56 S, 1000 units wide --- */
    const double A1 = 1.340264, A2 = -0.081106, A3 = 0.000893, A4 = 0.003796, M = 0.8660254037844386;
    const double Width = 1000, North = 84, South = -56;

    static (double X, double Y) Project(double lon, double lat)
    {
        double l = lon * Math.PI / 180, t = Math.Asin(M * Math.Sin(lat * Math.PI / 180)), t2 = t * t, t6 = t2 * t2 * t2;
        double x = l * Math.Cos(t) / (M * (A1 + 3 * A2 * t2 + t6 * (7 * A3 + 9 * A4 * t2)));
        double y = t * (A1 + A2 * t2 + t6 * (A3 + A4 * t2));
        return (x, y);
    }

    static readonly double Scale = Width / (2 * Project(180, 0).X);
    static readonly double Top = Project(0, North).Y, Bottom = Project(0, South).Y;

    static (double X, double Y) ToMap(double lon, double lat)
    {
        (double x, double y) = Project(lon, Math.Clamp(lat, South, North));
        return (Width / 2 + x * Scale, (Top - y) * Scale);
    }

    // Douglas-Peucker: the points a ring keeps when a tenth of a unit is all that may move
    static void Keep(List<(double X, double Y)> p, int a, int b, double tol, bool[] keep)
    {
        double best = 0;
        int at = -1;
        double dx = p[b].X - p[a].X, dy = p[b].Y - p[a].Y, len = Math.Sqrt(dx * dx + dy * dy);
        for (int i = a + 1; i < b; i++) {
            double d = len == 0 ? Math.Sqrt((p[i].X - p[a].X) * (p[i].X - p[a].X) + (p[i].Y - p[a].Y) * (p[i].Y - p[a].Y))
                                : Math.Abs(dy * (p[i].X - p[a].X) - dx * (p[i].Y - p[a].Y)) / len;
            if (d > best) {
                best = d;
                at = i;
            }
        }
        if (at >= 0 && best > tol) {
            keep[at] = true;
            Keep(p, a, at, tol, keep);
            Keep(p, at, b, tol, keep);
        }
    }

    static string Outline(List<List<(double Lon, double Lat)>> rings)
    {
        StringBuilder d = new StringBuilder();
        foreach (List<(double Lon, double Lat)> r in rings) {
            List<(double X, double Y)> p = new List<(double X, double Y)>();
            foreach ((double lon, double lat) in Open(r)) {
                p.Add(ToMap(lon, lat));
            }
            if (p.Count < 3) {
                continue;
            }
            bool[] keep = new bool[p.Count];
            keep[0] = keep[p.Count - 1] = true;
            Keep(p, 0, p.Count - 1, 0.25, keep);
            List<(int X, int Y)> q = new List<(int X, int Y)>();
            for (int i = 0; i < p.Count; i++) {
                if (keep[i]) {
                    (int X, int Y) pt = ((int)Math.Round(p[i].X * 10), (int)Math.Round(p[i].Y * 10));   // tenths of a unit
                    if (q.Count == 0 || q[q.Count - 1] != pt) {
                        q.Add(pt);
                    }
                }
            }
            if (q.Count < 3) {
                continue;
            }
            // M to the first point, then each one from the last, in tenths: short numbers, the browser adds them up
            d.Append(FormattableString.Invariant($"M{q[0].X / 10.0} {q[0].Y / 10.0}"));
            for (int i = 1; i < q.Count; i++) {
                d.Append(FormattableString.Invariant($"l{(q[i].X - q[i - 1].X) / 10.0} {(q[i].Y - q[i - 1].Y) / 10.0}"));
            }
            d.Append('z');
        }
        return d.ToString().Replace(" -", "-");
    }

    static string Write(List<Country> all)
    {
        StringBuilder js = new StringBuilder();
        js.Append("// the countries for the map of boards (stats.js). written by tools/WorldBake, don't edit: WorldBake\n");
        js.Append("// <ne_110m_admin_0_countries.geojson> site. borders: Natural Earth 1:110m, public domain, with Crimea\n");
        js.Append("// Ukraine's (Natural Earth's default draws it Russia's). Equal Earth, Antarctica left out.\n");
        js.Append("// a country: its ISO 3166 two letters, its name, where its number goes, its outline (fill-rule evenodd)\n");
        double h = Math.Round((Top - Bottom) * Scale, 1);
        js.Append(FormattableString.Invariant($"export const WORLD = {{ w: {Width}, h: {h}, countries: [\n"));
        int n = 0;
        foreach (Country c in all) {
            if (c.Cc == "AQ") {
                continue;
            }
            string d = Outline(c.Rings);
            if (d.Length == 0) {
                continue;
            }
            (double lx, double ly) = ToMap(c.LabelLon, c.LabelLat);
            js.Append(FormattableString.Invariant($"[{JsonSerializer.Serialize(c.Cc)},{JsonSerializer.Serialize(c.Name)},{Math.Round(lx, 1)},{Math.Round(ly, 1)},\"{d}\"],\n"));
            n++;
        }
        js.Append("] };\n");
        Console.WriteLine($"{n} countries written, the map {Width} x {h}");
        return js.ToString();
    }
}
