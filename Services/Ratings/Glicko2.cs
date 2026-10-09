namespace ChessGame.Api.Services.Ratings;

// Reference: Mark Glickman, https://www.glicko.net/glicko/glicko2.pdf (revised Step 5).
// Pure period calculation; callers define the game's period, noise floor and movement cap.
public static class Glicko2
{
    private const double Scale = 173.7178;
    public sealed record State(double Rating, double Deviation, double Volatility);
    public sealed record Opponent(double Rating, double Deviation, double Score);

    public static State Update(State player, IReadOnlyList<Opponent> opponents, double tau = .2)
    {
        if (!double.IsFinite(player.Rating) || !double.IsFinite(player.Deviation) || player.Deviation <= 0 ||
            !double.IsFinite(player.Volatility) || player.Volatility <= 0 || !double.IsFinite(tau) || tau <= 0)
            throw new ArgumentOutOfRangeException(nameof(player));
        double mu = (player.Rating - 1500) / Scale, phi = player.Deviation / Scale;
        if (opponents.Count == 0)
            return player with { Deviation = Scale * Math.Sqrt(phi * phi + player.Volatility * player.Volatility) };
        double information = 0, improvement = 0;
        foreach (var opponent in opponents)
        {
            if (!double.IsFinite(opponent.Rating) || !double.IsFinite(opponent.Deviation) || opponent.Deviation <= 0 ||
                opponent.Score is not (0 or .5 or 1)) throw new ArgumentOutOfRangeException(nameof(opponents));
            double g = 1 / Math.Sqrt(1 + 3 * Math.Pow(opponent.Deviation / Scale, 2) / (Math.PI * Math.PI));
            double exponent = Math.Clamp(-g * (mu - (opponent.Rating - 1500) / Scale), -35, 35);
            double e = 1 / (1 + Math.Exp(exponent));
            information += g * g * e * (1 - e);
            improvement += g * (opponent.Score - e);
        }
        double v = 1 / information, delta = v * improvement, a = Math.Log(player.Volatility * player.Volatility);
        double F(double x)
        {
            double ex = Math.Exp(x), den = phi * phi + v + ex;
            return ex * (delta * delta - phi * phi - v - ex) / (2 * den * den) - (x - a) / (tau * tau);
        }
        double left = a, right;
        if (delta * delta > phi * phi + v) right = Math.Log(delta * delta - phi * phi - v);
        else
        {
            int k = 1;
            while (F(a - k * tau) < 0 && k < 1000) k++;
            if (k == 1000) throw new InvalidOperationException("Glicko-2 volatility bracket failed.");
            right = a - k * tau;
        }
        double fl = F(left), fr = F(right);
        int iteration = 0;
        while (Math.Abs(right - left) > .000001)
        {
            if (++iteration > 1000) throw new InvalidOperationException("Glicko-2 volatility did not converge.");
            double c = left + (left - right) * fl / (fr - fl), fc = F(c);
            if (fc * fr <= 0) { left = right; fl = fr; } else fl /= 2;
            right = c; fr = fc;
        }
        double sigma = Math.Exp(left / 2), prePhi = Math.Sqrt(phi * phi + sigma * sigma);
        double nextPhi = 1 / Math.Sqrt(1 / (prePhi * prePhi) + 1 / v);
        return new State(1500 + Scale * (mu + nextPhi * nextPhi * improvement), Scale * nextPhi, sigma);
    }
}
