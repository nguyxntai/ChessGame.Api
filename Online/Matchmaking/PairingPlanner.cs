namespace ChessGame.Api.Online;

// Maximum-cardinality matching in a bounded general graph (Edmonds' augmenting-path/blossom algorithm).
// Neighbor order favors lower cost, but this does not claim globally minimum total cost.
public static class PairingPlanner
{
    public static List<(int A, int B)> Match(int count, Func<int, int, double?> cost)
    {
        if (count is < 0 or > 256) throw new ArgumentOutOfRangeException(nameof(count));
        var graph = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var costs = new double[count, count];
        for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
        {
            var value = cost(a, b);
            if (value is null) continue;
            if (!double.IsFinite(value.Value)) throw new ArgumentException("Pair cost must be finite.");
            graph[a].Add(b); graph[b].Add(a); costs[a, b] = costs[b, a] = value.Value;
        }
        for (int a = 0; a < count; a++)
        { int vertex = a; graph[a].Sort((x, y) => costs[vertex, x] == costs[vertex, y] ? x.CompareTo(y) : costs[vertex, x].CompareTo(costs[vertex, y])); }
        var mate = Enumerable.Repeat(-1, count).ToArray();
        var parent = new int[count]; var basis = new int[count]; var used = new bool[count]; var blossom = new bool[count];
        int Ancestor(int a, int b)
        {
            var path = new bool[count];
            while (true) { a = basis[a]; path[a] = true; if (mate[a] == -1) break; a = parent[mate[a]]; }
            while (true) { b = basis[b]; if (path[b]) return b; b = parent[mate[b]]; }
        }
        void Mark(int v, int common, int child)
        {
            while (basis[v] != common)
            { blossom[basis[v]] = blossom[basis[mate[v]]] = true; parent[v] = child; child = mate[v]; v = parent[mate[v]]; }
        }
        int Search(int root)
        {
            Array.Fill(parent, -1); Array.Clear(used);
            for (int i = 0; i < count; i++) basis[i] = i;
            var queue = new Queue<int>(); queue.Enqueue(root); used[root] = true;
            while (queue.TryDequeue(out int v)) foreach (int u in graph[v])
            {
                if (basis[v] == basis[u] || mate[v] == u) continue;
                if (u == root || mate[u] != -1 && parent[mate[u]] != -1)
                {
                    int common = Ancestor(v, u); Array.Clear(blossom);
                    Mark(v, common, u); Mark(u, common, v);
                    for (int i = 0; i < count; i++) if (blossom[basis[i]])
                    { basis[i] = common; if (!used[i]) { used[i] = true; queue.Enqueue(i); } }
                }
                else if (parent[u] == -1)
                {
                    parent[u] = v;
                    if (mate[u] == -1) return u;
                    used[mate[u]] = true; queue.Enqueue(mate[u]);
                }
            }
            return -1;
        }
        for (int root = 0; root < count; root++) if (mate[root] == -1)
        {
            int v = Search(root);
            while (v != -1) { int p = parent[v], next = p == -1 ? -1 : mate[p]; mate[v] = p; if (p != -1) mate[p] = v; v = next; }
        }
        return Enumerable.Range(0, count).Where(a => mate[a] > a).Select(a => (a, mate[a])).ToList();
    }
}
