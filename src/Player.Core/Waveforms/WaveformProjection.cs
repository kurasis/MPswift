namespace Player.Core.Waveforms;

public readonly record struct WaveformColumn(float Minimum, float Maximum, float Rms);

/// <summary>Time-weighted energy, not the loudest transient, determines the solid envelope.</summary>
public static class WaveformProjection
{
    public static WaveformColumn[] Create(WaveformData data, int requestedColumns)
    {
        data.Validate();
        var count = Math.Clamp(requestedColumns, 1, Math.Min(4096, data.Minimum.Length));
        var columns = new WaveformColumn[count];
        for (var column = 0; column < count; column++)
        {
            var start = (long)((double)column * data.TotalFrames / count);
            var end = (long)((double)(column + 1) * data.TotalFrames / count);
            var first = start / data.FramesPerBucket; var last = (end - 1) / data.FramesPerBucket;
            float low = 0, high = 0; double energy = 0;
            for (var i = first; i <= last; i++)
            {
                var frames = Math.Min(end, (i + 1) * data.FramesPerBucket) - Math.Max(start, i * data.FramesPerBucket);
                var rms = data.Rms?[i] ?? Math.Max(Math.Abs(data.Minimum[i]), Math.Abs(data.Maximum[i]));
                low = Math.Min(low, data.Minimum[i]); high = Math.Max(high, data.Maximum[i]);
                energy += (double)rms * rms * frames;
            }
            columns[column] = new(low, high, (float)Math.Sqrt(energy / (end - start)));
        }
        return columns;
    }
}
