using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// A spoken version of a book's map (M). It lists where you are, the paths leading from here, and every other
    /// place you know, each with a compass direction and distance measured from the positions on the map picture.
    /// Enter on a place gives a route through known paths. Only places the game would show you are mentioned.
    /// </summary>
    internal static class MapReader
    {
        internal class Place
        {
            public MapLocation Loc;
            public string Name;
            public Vector2 Pos;
            public bool Greyed;
        }

        /// <summary>The map's places, or null if this book has no map.</summary>
        private static List<Place> KnownPlaces(out Place here, out Transform mapRoot)
        {
            here = null;
            mapRoot = null;
            MapController mc = UnityEngine.Object.FindObjectOfType<MapController>(true);
            if (mc == null)
            {
                return null;
            }
            MapLocation[] locations = Traverse.Create(mc).Field("mapLocations").GetValue<MapLocation[]>();
            mapRoot = Traverse.Create(mc).Field("map").GetValue<Transform>();
            if (locations == null || locations.Length == 0 || mapRoot == null)
            {
                return null;
            }
            string currentId = BBGameController.instance?.character?.mapPosition;
            List<Place> places = new List<Place>();
            foreach (MapLocation l in locations)
            {
                if (l == null)
                {
                    continue;
                }
                bool isHere = !string.IsNullOrEmpty(currentId) && l.id == currentId;
                bool visible, greyed;
                Visibility(l, out visible, out greyed);
                if (!visible && !greyed && !isHere)
                {
                    continue;
                }
                Vector3 local = mapRoot.InverseTransformPoint(l.transform.position);
                Place p = new Place
                {
                    Loc = l,
                    Name = NameOf(l),
                    Pos = new Vector2(local.x, local.y),
                    Greyed = greyed && !visible
                };
                places.Add(p);
                if (isHere)
                {
                    here = p;
                }
            }
            return places;
        }

        /// <summary>
        /// The same test as MapLocation.CheckEnable, without changing the map's visuals: a place is visible if you
        /// visited one of its sections on this adventure, greyed if only on an earlier one.
        /// </summary>
        private static void Visibility(MapLocation l, out bool visible, out bool greyed)
        {
            visible = false;
            greyed = false;
            BBGameController gc = BBGameController.instance;
            if (l.sections == null || gc == null)
            {
                return;
            }
            foreach (int s in l.sections)
            {
                int e1, x1;
                List<int> e2, x2;
                if (gc.character != null && gc.character.HasVisitedMapPosition(s, out e1, out x1)) visible = true;
                if (gc.Player != null && gc.book != null && gc.Player.HasVisitedMapPosition(gc.book.ID, s, out e2, out x2)) greyed = true;
            }
        }

        private static string NameOf(MapLocation l)
        {
            string name = l.labelText != null ? TextUtil.Clean(l.labelText.text) : "";
            if (name.Length == 0) name = TextUtil.Clean(l.label);
            if (name.Length == 0) name = TextUtil.Clean(l.description);
            if (name.Length == 0) name = TextUtil.Humanize(l.id);
            return name;
        }

        /// <summary>A "step" is the typical gap between a place and its nearest neighbour on this map.</summary>
        private static float StepSize(List<Place> places)
        {
            List<float> nearest = new List<float>();
            foreach (Place a in places)
            {
                float best = float.MaxValue;
                foreach (Place b in places)
                {
                    if (a != b) best = Mathf.Min(best, Vector2.Distance(a.Pos, b.Pos));
                }
                if (best < float.MaxValue && best > 0.01f) nearest.Add(best);
            }
            if (nearest.Count == 0) return 1f;
            nearest.Sort();
            return nearest[nearest.Count / 2];
        }

        private static readonly string[] Compass = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };

        /// <summary>"north-east, 2 steps". On screen, up is north.</summary>
        public static string Relative(Vector2 from, Vector2 to, float step)
        {
            Vector2 d = to - from;
            float dist = d.magnitude / step;
            if (dist < 0.4f)
            {
                return "right here";
            }
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            int sector = Mathf.RoundToInt(((angle + 360f) % 360f) / 45f) % 8;
            int steps = Mathf.Max(1, Mathf.RoundToInt(dist));
            return Compass[sector] + ", " + steps + (steps == 1 ? " step" : " steps");
        }

        private static string DirectionOnly(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            return Compass[Mathf.RoundToInt(((angle + 360f) % 360f) / 45f) % 8];
        }

        /// <summary>Places directly connected to this one, in either direction.</summary>
        private static IEnumerable<MapLocation> Neighbours(MapLocation l)
        {
            if (l.exits != null) foreach (MapLocation m in l.exits) if (m != null) yield return m;
            if (l.entries != null) foreach (MapLocation m in l.entries) if (m != null) yield return m;
        }

        public static void Open()
        {
            if (!SectionReader.InBook())
            {
                Speech.Say("No book is open.");
                return;
            }
            Place here;
            Transform root;
            List<Place> places = KnownPlaces(out here, out root);
            if (places == null)
            {
                Speech.Say("This book has no map.");
                return;
            }
            float step = StepSize(places);
            Dictionary<MapLocation, Place> byLoc = new Dictionary<MapLocation, Place>();
            foreach (Place p in places) byLoc[p.Loc] = p;

            List<string> lines = new List<string>();
            List<int> paras = new List<int>();
            List<Place> lineTarget = new List<Place>();
            Action<string, int, Place> add = (t, para, target) => { lines.Add(t); paras.Add(para); lineTarget.Add(target); };

            if (here == null)
            {
                add("Your position isn't marked on the map yet.", 0, null);
            }
            else
            {
                string desc = TextUtil.Clean(here.Loc.description);
                add("You are at " + here.Name + "." + (desc.Length > 0 && desc != here.Name ? " " + desc : ""), 0, null);
                // Paths from here: connected places you know.
                List<string> seen = new List<string>();
                foreach (MapLocation n in Neighbours(here.Loc))
                {
                    Place p;
                    if (!byLoc.TryGetValue(n, out p) || seen.Contains(p.Name)) continue;
                    seen.Add(p.Name);
                    add("Path: " + Relative(here.Pos, p.Pos, step) + ": " + p.Name + ".", 1, p);
                }
                if (seen.Count == 0)
                {
                    add("No known paths lead from here.", 1, null);
                }
            }
            // Everything else you know, nearest first.
            List<Place> others = new List<Place>(places);
            others.Remove(here);
            Vector2 origin = here != null ? here.Pos : Vector2.zero;
            others.Sort((a, b) => Vector2.Distance(origin, a.Pos).CompareTo(Vector2.Distance(origin, b.Pos)));
            add(others.Count == 0 ? "You don't know any other places yet." : "Places you know, nearest first:", 2, null);
            foreach (Place p in others)
            {
                string where = here != null ? Relative(here.Pos, p.Pos, step) + ": " : "";
                add(where + p.Name + (p.Greyed ? ", from an earlier adventure" : "") + ".", 2, p);
            }

            MapBox box = new MapBox(here, places, lineTarget);
            box.SetLines(lines, paras);
            TextWindow.ShowBox("Map", box);
        }

        /// <summary>Breadth-first search over known places: the route with the fewest stops, or null.</summary>
        public static List<Place> Route(Place from, Place to, List<Place> places)
        {
            Dictionary<MapLocation, Place> byLoc = new Dictionary<MapLocation, Place>();
            foreach (Place p in places) byLoc[p.Loc] = p;
            Dictionary<Place, Place> cameFrom = new Dictionary<Place, Place>();
            Queue<Place> queue = new Queue<Place>();
            queue.Enqueue(from);
            cameFrom[from] = null;
            while (queue.Count > 0)
            {
                Place cur = queue.Dequeue();
                if (cur == to)
                {
                    List<Place> path = new List<Place>();
                    for (Place p = to; p != null; p = cameFrom[p]) path.Insert(0, p);
                    return path;
                }
                foreach (MapLocation n in Neighbours(cur.Loc))
                {
                    Place next;
                    if (byLoc.TryGetValue(n, out next) && !cameFrom.ContainsKey(next))
                    {
                        cameFrom[next] = cur;
                        queue.Enqueue(next);
                    }
                }
            }
            return null;
        }

        public static string DescribeRoute(List<Place> path)
        {
            List<string> legs = new List<string>();
            for (int i = 1; i < path.Count; i++)
            {
                legs.Add(DirectionOnly(path[i - 1].Pos, path[i].Pos) + " to " + path[i].Name);
            }
            string s = string.Join(", then ", legs.ToArray());
            return "Route: " + char.ToUpper(s[0]) + s.Substring(1) + ".";
        }
    }

    /// <summary>The map window: Enter on a place gives a route to it from where you are.</summary>
    internal class MapBox : TextBox
    {
        private readonly MapReader.Place here;
        private readonly List<MapReader.Place> places;
        private readonly List<MapReader.Place> lineTarget;

        public MapBox(MapReader.Place here, List<MapReader.Place> places, List<MapReader.Place> lineTarget)
        {
            this.here = here;
            this.places = places;
            this.lineTarget = lineTarget;
        }

        protected override void OnEnter()
        {
            int line = CurrentLine;
            MapReader.Place target = line < lineTarget.Count ? lineTarget[line] : null;
            if (target == null)
            {
                Say("Not a place.");
                return;
            }
            if (here == null)
            {
                Say("Your position isn't marked, so there's no route.");
                return;
            }
            List<MapReader.Place> path = MapReader.Route(here, target, places);
            if (path == null)
            {
                Say("No known route to " + target.Name + ".");
                return;
            }
            string desc = TextUtil.Clean(target.Loc.description);
            Say(MapReader.DescribeRoute(path) + (desc.Length > 0 && desc != target.Name ? " " + desc : ""));
        }
    }
}
