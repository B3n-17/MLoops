namespace MLoops.Models;

public enum AudioFileType { Unknown, Wav, Flac, Ogg }

public class AudioFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public AudioFileType FileType { get; set; }

    public WavMetadata? WavMetadata { get; set; }
    public FlacMetadata? FlacMetadata { get; set; }
    public OggMetadata? OggMetadata { get; set; }

    public List<CuePoint> CuePoints { get; set; } = new();
    public List<LoopPoint> LoopPoints { get; set; } = new();
    public List<MetadataTag> Tags { get; set; } = new();

    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public double DurationSeconds { get; set; }
}
