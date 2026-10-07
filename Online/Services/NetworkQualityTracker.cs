using System.Security.Cryptography;

namespace ChessGame.Api.Online;

public sealed record LatencySample(double RoundTripMilliseconds, double JitterMilliseconds, int Samples, DateTime MeasuredAt);

// Monotonic timing, one outstanding nonce per authenticated connection; no client timestamps.
public sealed class NetworkQualityTracker(TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Dictionary<string, Probe> probes = new();
    private readonly object gate = new();
    private sealed class Probe
    {
        public string Nonce = "";
        public long Started;
        public bool Began;
        public double Mean, Jitter;
        public int Samples;
        public DateTime LastSample;
    }
    public string Begin(string connection)
    {
        lock (gate)
        {
            if (!probes.TryGetValue(connection, out var p)) probes[connection] = p = new();
            var now = time.GetTimestamp();
            if (p.Began && time.GetElapsedTime(p.Started, now).TotalSeconds < 2) throw new OnlineException("LatencyProbeRateLimited", 429);
            p.Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); p.Started = now; p.Began = true;
            return p.Nonce;
        }
    }
    public LatencySample Complete(string connection, string nonce)
    {
        lock (gate)
        {
            if (!probes.TryGetValue(connection, out var p) || string.IsNullOrEmpty(p.Nonce) || p.Nonce != nonce)
                throw new OnlineException("InvalidLatencyProbe", 400);
            p.Nonce = ""; // Consume even an expired nonce.
            double elapsed = time.GetElapsedTime(p.Started, time.GetTimestamp()).TotalMilliseconds;
            if (elapsed < 0 || elapsed > 10000) throw new OnlineException("LatencyProbeExpired", 400);
            var now = time.GetUtcNow().UtcDateTime;
            if (now - p.LastSample > TimeSpan.FromSeconds(60)) p.Samples = 0;
            p.Jitter = p.Samples == 0 ? 0 : .75 * p.Jitter + .25 * Math.Abs(elapsed - p.Mean);
            p.Mean = p.Samples == 0 ? elapsed : .75 * p.Mean + .25 * elapsed;
            p.Samples = Math.Min(1000, p.Samples + 1); p.LastSample = now;
            return new(p.Mean, p.Jitter, p.Samples, now);
        }
    }
    public void Remove(string connection) { lock (gate) probes.Remove(connection); }
}
