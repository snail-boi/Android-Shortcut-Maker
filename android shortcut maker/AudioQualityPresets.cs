namespace android_shortcut_maker;

/// <summary>
/// Single source of truth for scrcpy audio quality presets.
/// In ASM, quality settings live in the shortcut args rather than a persistent
/// config, so MatchFromConfig takes raw string values instead of a MusicConfig.
/// </summary>
public static class AudioQualityPresets
{
    public const string CustomLabel = "Custom";

    public sealed class Preset
    {
        public string Name        { get; init; } = string.Empty;
        public string ShortName   { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Codec       { get; init; } = "raw";
        public string Bitrate     { get; init; } = string.Empty;
        public int    BufferMs    { get; init; } = 80;
        public int    FlacCompressionLevel { get; init; } = 2;
    }

    public static readonly IReadOnlyList<Preset> All = new[]
    {
        new Preset
        {
            Name = "Data Saver",
            ShortName = "Data Saver",
            Description = "Opus 64 kbps, smallest data use, OK quality.",
            Codec = "opus", Bitrate = "64", BufferMs = 120,
        },
        new Preset
        {
            Name = "Default",
            ShortName = "Default",
            Description = "Opus 128 kbps, balanced for general audio.",
            Codec = "opus", Bitrate = "128", BufferMs = 100,
        },
        new Preset
        {
            Name = "High Quality",
            ShortName = "High",
            Description = "Opus 256 kbps, transparent for most music.",
            Codec = "opus", Bitrate = "256", BufferMs = 80,
        },
        new Preset
        {
            Name = "Lossless",
            ShortName = "Lossless",
            Description = "FLAC, lossless with moderate compression.",
            Codec = "flac", Bitrate = string.Empty, BufferMs = 80, FlacCompressionLevel = 2,
        },
        new Preset
        {
            Name = "Max Quality",
            ShortName = "Max",
            Description = "Raw PCM, uncompressed audio.",
            Codec = "raw", Bitrate = string.Empty, BufferMs = 80,
        },
    };

    public static Preset? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Match a preset from raw codec/bitrate/buffer values (from shortcut args).</summary>
    public static Preset? MatchFromArgs(string codec, string bitrate, int bufferMs, int flacLevel)
    {
        codec = string.IsNullOrWhiteSpace(codec) ? "raw" : codec.Trim().ToLowerInvariant();
        bitrate = (bitrate ?? string.Empty).Trim().TrimEnd('K', 'k');

        foreach (var preset in All)
        {
            if (!preset.Codec.Equals(codec, StringComparison.OrdinalIgnoreCase)) continue;

            bool bitrateMatch = preset.Codec switch
            {
                "raw"  => true,
                "flac" => true,
                _      => string.Equals(preset.Bitrate, bitrate, StringComparison.OrdinalIgnoreCase),
            };
            if (!bitrateMatch) continue;
            if (preset.BufferMs != bufferMs) continue;
            if (preset.Codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
                && preset.FlacCompressionLevel != flacLevel) continue;

            return preset;
        }
        return null;
    }

    public static string GetShortLabel(string codec, string bitrate, int bufferMs, int flacLevel)
        => MatchFromArgs(codec, bitrate, bufferMs, flacLevel)?.ShortName ?? CustomLabel;
}
