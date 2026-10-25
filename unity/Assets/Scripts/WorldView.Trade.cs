using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// The caravans of the trade routes: for every open route a trader leads a pack horse and a loaded
    /// cart from the trading post along the road out of the valley, towards the side its people live on,
    /// and back again when the trip is nearly over. Between the two legs the caravan is away beyond the
    /// valley; while the route waits for goods or rests, it stands by the trading post.
    /// </summary>
    public partial class WorldView
    {
        sealed class Caravan
        {
            public string PartnerId;
            public Villager Trader;
            public Animal Horse;
            public GameObject Cart;
            /// <summary>Points from the trading post to the edge of the known land.</summary>
            public List<Vector3> Path;
            public float Length;
            public string PathKey;
            /// <summary>The route's trip time as last seen, and when (smooths the once-a-second simulation).</summary>
            public double SeenElapsed = -1;
            public float SeenAt;
            public int Slot;
        }

        readonly Dictionary<string, Caravan> caravans = new Dictionary<string, Caravan>();

        /// <summary>Walking pace of a caravan (metres per second).</summary>
        const float CaravanSpeed = 1.25f;
        /// <summary>Spacing along the road: horse in front, the cart behind it, the trader beside the cart.</summary>
        const float CartBehind = 2.1f, TraderBehind = 1.1f;

        /// <summary>Creates and removes caravans to match the open routes (called on every sync).</summary>
        void SyncCaravans(Game game)
        {
            var post = game.TradingPost();
            var wanted = post == null ? new List<TradeRoute>() : game.State.TradeRoutes;
            int slot = 0;
            foreach (var route in wanted)
            {
                var partner = game.Content.TradePartner(route.PartnerId);
                if (partner == null) continue;
                if (!caravans.TryGetValue(route.PartnerId, out var c))
                {
                    int seed = 7000 + route.PartnerId.GetHashCode() % 1000;
                    c = new Caravan { PartnerId = route.PartnerId };
                    var at = Grid.CellCenter(post.X, post.Y);
                    c.Trader = Villager.Spawn(peopleRoot, at, seed, seed % 2 == 0);
                    c.Trader.Puppet = true;
                    c.Trader.Ground = Ground;
                    c.Horse = Animal.Spawn("horse", peopleRoot, at, 0.5f, seed, Ground);
                    if (c.Horse != null) c.Horse.Puppet = true;
                    c.Cart = BuildingModels.Cart(peopleRoot, true);
                    caravans[route.PartnerId] = c;
                }
                c.Slot = slot++;
                string key = $"{post.Id}|{post.X},{post.Y}|{post.Rotation}|{game.State.ExploredRegions.Count}|{game.State.Buildings.Count}";
                if (c.PathKey != key)
                {
                    c.PathKey = key;
                    c.Path = TradePath(game, post, partner);
                    c.Length = 0;
                    for (int i = 1; i < c.Path.Count; i++) c.Length += Vector3.Distance(c.Path[i - 1], c.Path[i]);
                }
            }
            foreach (var id in caravans.Keys.Where(id => wanted.All(r => r.PartnerId != id)).ToList())
            {
                var c = caravans[id];
                if (c.Trader != null) Destroy(c.Trader.gameObject);
                if (c.Horse != null) Destroy(c.Horse.gameObject);
                if (c.Cart != null) Destroy(c.Cart);
                caravans.Remove(id);
            }
        }

        /// <summary>
        /// The road a caravan takes: from the walkable ground in front of the trading post to the last
        /// open, explored cell in the direction its people live (at most 40 cells away), found with the
        /// villagers' path finder so it keeps to the roads and goes round woods and water.
        /// </summary>
        List<Vector3> TradePath(Game game, PlacedBuilding post, TradePartnerDefinition partner)
        {
            var p = Paths;
            var fp = game.FootprintOf(post);
            int cx = post.X + fp.Width / 2, cy = post.Y + fp.Height / 2;
            var start = p.NearestWalkable(cx, cy + fp.Height / 2 + 1) ?? p.NearestWalkable(cx, cy) ?? (cx, cy);
            var (dx, dy) = partner.Heading;
            var exit = start;
            var map = game.Content.Map;
            for (int i = 1; i <= 40; i++)
            {
                int x = start.x + dx * i, y = start.y + dy * i;
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || !game.IsRevealed(x, y)) break;
                if (p.Walkable(x, y)) exit = (x, y);
            }
            var points = new List<Vector3> { Ground.OnGround(Grid.CellCenter(start.x, start.y)) };
            var end = Ground.OnGround(Grid.CellCenter(exit.x, exit.y));
            var middle = Route(points[0], end);
            if (middle != null) points.AddRange(middle);
            if ((points[points.Count - 1] - end).sqrMagnitude > 0.01f) points.Add(end);
            if (points.Count == 1) points.Add(points[0] + new Vector3(dx, 0, -dy) * Grid.CellSize * 3);
            return points;
        }

        /// <summary>A point <paramref name="distance"/> metres along the path (clamped to its ends) and the way it runs there.</summary>
        static Vector3 AlongPath(List<Vector3> path, float distance, out Vector3 direction)
        {
            direction = path.Count > 1 ? path[1] - path[0] : Vector3.forward;
            if (distance <= 0) return path[0];
            for (int i = 1; i < path.Count; i++)
            {
                var seg = path[i] - path[i - 1];
                float len = seg.magnitude;
                if (len < 1e-4f) continue;
                direction = seg;
                if (distance <= len) return path[i - 1] + seg * (distance / len);
                distance -= len;
            }
            return path[path.Count - 1];
        }

        /// <summary>Moves every caravan to where its route's trip has taken it (every frame).</summary>
        void UpdateCaravans()
        {
            if (caravans.Count == 0 || store == null) return;
            var game = store.Game;
            foreach (var c in caravans.Values)
            {
                var route = game.Route(c.PartnerId);
                var partner = game.Content.TradePartner(c.PartnerId);
                if (route == null || partner == null || c.Path == null) continue;
                var status = game.RouteStatus(route);

                // Distance along the road (−1 = away beyond the valley) and which way they walk.
                float d = 0f;
                bool walking = false, outbound = true;
                if (status.State == TradeRouteState.OnTheRoad)
                {
                    if (route.Elapsed != c.SeenElapsed) { c.SeenElapsed = route.Elapsed; c.SeenAt = Time.time; }
                    float elapsed = (float)route.Elapsed + Mathf.Min(Time.time - c.SeenAt, 1.5f);
                    float total = (float)game.TripSeconds(partner);
                    // Each leg takes the time to walk the road, but never more than 40 % of the trip.
                    float leg = Mathf.Min(total * 0.4f, c.Length / CaravanSpeed);
                    if (elapsed < leg) { d = c.Length * elapsed / leg; walking = true; }
                    else if (total - elapsed < leg) { d = c.Length * Mathf.Max(0f, total - elapsed) / leg; walking = true; outbound = false; }
                    else d = -1f;
                }
                bool away = d < 0f;
                c.Trader.gameObject.SetActive(!away);
                if (c.Horse != null) c.Horse.gameObject.SetActive(!away);
                c.Cart.SetActive(!away);
                if (away) continue;

                if (!walking)
                {
                    // Waiting at the trading post, side by side when several routes rest there.
                    var at = AlongPath(c.Path, 1.5f + c.Slot * 3.2f, out var dir);
                    var side = Vector3.Cross(Vector3.up, dir.normalized) * 1.4f;
                    Place(c, at + side, dir, false, false);
                    continue;
                }
                // The horse leads; the cart follows it and the trader walks beside the cart.
                float sign = outbound ? 1f : -1f;
                var head = AlongPath(c.Path, d, out var heading);
                Place(c, head, heading * sign, true, outbound, d, sign);
            }
        }

        void Place(Caravan c, Vector3 head, Vector3 heading, bool walking, bool loaded, float d = 0f, float sign = 1f)
        {
            heading.y = 0;
            if (heading.sqrMagnitude < 1e-6f) heading = Vector3.forward;
            heading.Normalize();
            Vector3 cartAt, traderAt;
            if (walking)
            {
                cartAt = AlongPath(c.Path, d - sign * CartBehind, out _);
                traderAt = AlongPath(c.Path, d - sign * TraderBehind, out _) + Vector3.Cross(Vector3.up, heading) * 0.9f;
            }
            else
            {
                cartAt = head - heading * CartBehind;
                traderAt = head - heading * TraderBehind + Vector3.Cross(Vector3.up, heading) * 0.9f;
            }
            head = Ground.OnWalk(head);
            cartAt = Ground.OnWalk(cartAt);
            traderAt = Ground.OnWalk(traderAt);
            if (c.Horse != null) c.Horse.Steer(head, heading, walking);
            // The cart's shafts (its −z side) point at the horse.
            var toHorse = head - cartAt;
            toHorse.y = 0;
            if (toHorse.sqrMagnitude > 1e-4f)
                c.Cart.transform.SetPositionAndRotation(cartAt, Quaternion.LookRotation(-toHorse.normalized));
            else c.Cart.transform.position = cartAt;
            c.Trader.Steer(traderAt, walking ? heading : (head - traderAt), walking, walking && !loaded ? "wicker_basket_01" : null);
        }
    }
}
