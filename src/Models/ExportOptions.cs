namespace MLoops.Models;

public enum ExportFormat { Flac, OggVorbis, Wav }

public class ExportOptions
{
    public ExportFormat Format { get; set; }
    public string SampleRate { get; set; } = "original";
    public string BitDepth { get; set; } = "original";
    public int CompressionLevel { get; set; } = 12;
    public bool PreserveMetadata { get; set; } = true;
    public bool KeepForeignMetadata { get; set; } = true;
}
