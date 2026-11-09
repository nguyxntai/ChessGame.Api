namespace ChessGame.Api.Online;

internal sealed partial class Chaos35Engine
{
    private void FireRifle(OnlineMatch m, ChaosSide side, AbilityCommand c, DateTime now)
    {
        var a = m.Chaos!; var rifle = a.Rifle;
        Require(Has(side, 25) && rifle?.Team == side.Team && now < rifle.Deadline, "RifleExpired");
        Require(c.Yaw.HasValue && c.Pitch.HasValue && double.IsFinite(c.Yaw.Value) && double.IsFinite(c.Pitch.Value) &&
            c.Pitch.Value >= -80 && c.Pitch.Value <= 80 && Math.Abs(c.Yaw.Value) <= 36000, "InvalidAim");
        double yaw = c.Yaw!.Value * Math.PI / 180, pitch = c.Pitch!.Value * Math.PI / 180;
        var direction = new[] { Math.Sin(yaw) * Math.Cos(pitch), -Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch) };
        var origin = new[] { rifle!.OriginX, rifle.OriginY, rifle.OriginZ };
        OnlinePiece? nearest = null; double distance = 500;
        // The board plane is an occluder too; a downward shot cannot hit pieces behind it.
        if (direction[1] < 0) distance = Math.Min(distance, -origin[1] / direction[1]);
        foreach (var p in m.Board.Where(p => p.Id != RealKing(m, side.Team)!.Id))
        {
            var square = GameRules.Square(p.Square); var box = a.Hitboxes.Single(x => x.Kind == p.Kind);
            double? hit = RayBox(origin, direction, new[] { square.File - box.HalfWidth, 0, square.Rank - box.HalfDepth },
                new[] { square.File + box.HalfWidth, box.Height, square.Rank + box.HalfDepth });
            if (hit is not null && hit < distance) { distance = hit.Value; nearest = p; }
        }
        bool hitEnemy = nearest is not null && nearest.Team != side.Team && nearest.Kind is not ("King" or "Queen");
        var king = RealKing(m, side.Team)!;
        var cue = Visual(m, 25, king, GameRules.Square(king.Square), GameRules.Square(nearest?.Square ?? king.Square),
            hitEnemy ? "Hit" : nearest is not null || distance < 500 ? "Blocked" : "Miss");
        cue.Origin = origin;
        cue.Impact = Enumerable.Range(0, 3).Select(i => origin[i] + direction[i] * Math.Min(distance, 12)).ToArray();
        if (hitEnemy)
        { RewardCapture(m, side.Team, nearest!); Remove(m, nearest!); }
        a.Rifle = null; side.RifleReady = side.CompletedTurns + 3;
    }
    private static double? RayBox(double[] origin, double[] dir, double[] min, double[] max)
    {
        double enter = 0, exit = 500;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(dir[i]) < 1e-9) { if (origin[i] < min[i] || origin[i] > max[i]) return null; continue; }
            double a = (min[i] - origin[i]) / dir[i], b = (max[i] - origin[i]) / dir[i];
            enter = Math.Max(enter, Math.Min(a, b)); exit = Math.Min(exit, Math.Max(a, b));
            if (enter > exit) return null;
        }
        return enter;
    }
}
