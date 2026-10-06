namespace Player.Core.Diagnostics;

public sealed record DigitalBoundaryResult(string Status, int AlignmentOffsetFrames, double CalibrationCorrelation, double FittedGain,
    int BeforeBoundaryLagFrames, int AfterBoundaryLagFrames, double BeforeCorrelation, double AfterCorrelation,
    double BoundaryNormalizedRmsError, double BoundaryMaximumRelativeError, int EvaluatedFrames, int CaptureFrames,
    string Method = "Owned deterministic PCM reference; positive correlation alignment; independent pre/post boundary lag; fitted gain; boundary error");

/// <summary>Analyze captured PCM; never substitute synthesized reference data for endpoint capture.</summary>
public static class DigitalBoundaryAnalyzer
{
    public static DigitalBoundaryResult Analyze(ReadOnlySpan<float> captured, int channels, int rate, int sourceFrames, int boundaryFrame)
    {
        if (channels is < 1 or > 8 || rate is < 8000 or > 192000 || captured.Length > 16 * 1024 * 1024 || captured.Length % channels != 0 ||
            sourceFrames < rate || sourceFrames > rate * 30 || boundaryFrame < rate / 2 || boundaryFrame > sourceFrames - rate / 2)
            throw new ArgumentException("Invalid or unbounded capture/reference dimensions.");
        foreach (var sample in captured) if (!float.IsFinite(sample)) throw new InvalidDataException("Captured PCM contains non-finite samples.");
        var frames = captured.Length / channels; var calibration = rate / 4;
        var match = Find(captured, channels, calibration, 0, Math.Max(-1, frames - calibration - 256));
        var offset = match.Lag;
        if (match.Correlation < .995 || match.Gain <= .0001 || frames < offset + sourceFrames)
            return new("failed-positive-control", offset, match.Correlation, match.Gain, 0, 0, 0, 0, double.MaxValue, double.MaxValue, 0, frames);
        var before = Find(captured, channels, boundaryFrame - rate / 10, offset - rate / 50, offset + rate / 50);
        var after = Find(captured, channels, boundaryFrame + rate / 10, offset - rate / 50, offset + rate / 50);
        var squaredError = 0d; var squaredReference = 0d; var maxError = 0d; var peak = 1000d / 32768 * Math.Abs(match.Gain);
        var start = boundaryFrame - rate / 20; var end = boundaryFrame + rate / 20;
        for (var frame = start; frame < end; frame++)
        {
            var expected = AcceptanceSignal.ReferenceSample(frame) / 32768d * match.Gain;
            for (var channel = 0; channel < channels; channel++)
            {
                var error = captured[(offset + frame) * channels + channel] - expected;
                squaredError += error * error; squaredReference += expected * expected; maxError = Math.Max(maxError, Math.Abs(error));
            }
        }
        var rms = Math.Sqrt(squaredError / Math.Max(squaredReference, double.Epsilon)); var relative = maxError / peak;
        var pass = before.Lag == offset && after.Lag == offset && before.Correlation >= .995 && after.Correlation >= .995 && rms <= .001 && relative <= .01;
        return new(pass ? "digital-boundary-passed" : "digital-boundary-failed", offset, match.Correlation, match.Gain,
            before.Lag - offset, after.Lag - offset, before.Correlation, after.Correlation, rms, relative, end - start, frames);
    }
    private static (int Lag, double Correlation, double Gain) Find(ReadOnlySpan<float> capture, int channels, int sourceFrame, int first, int last)
    {
        var best = -1d; var bestLag = 0; var gain = 0d;
        for (var lag = Math.Max(first, -sourceFrame); lag <= last; lag++)
        {
            if ((lag + sourceFrame + 252) * channels >= capture.Length) break;
            var xy = 0d; var xx = 0d; var yy = 0d;
            for (var i = 0; i < 64; i++)
            {
                var x = AcceptanceSignal.ReferenceSample(sourceFrame + i * 4) / 32768d;
                var y = capture[(lag + sourceFrame + i * 4) * channels];
                xx += x * x; xy += x * y; yy += y * y;
            }
            var correlation = xy / Math.Sqrt(Math.Max(xx * yy, double.Epsilon));
            if (correlation > best) { best = correlation; bestLag = lag; gain = xy / xx; }
        }
        return (bestLag, best, gain);
    }
}
