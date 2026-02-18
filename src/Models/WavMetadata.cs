namespace MLoops.Models;

public class WavMetadata
{
    public string ChunkId { get; set; } = "RIFF";
    public uint ChunkSize { get; set; }
    public string Format { get; set; } = "WAVE";

    public ushort AudioFormat { get; set; }
    public string AudioFormatName { get; set; } = "";
    public ushort NumChannels { get; set; }
    public uint SampleRate { get; set; }
    public uint ByteRate { get; set; }
    public ushort BlockAlign { get; set; }
    public ushort BitsPerSample { get; set; }

    public BextChunk? Bext { get; set; }
    public uint? FactSampleLength { get; set; }
    public SmplChunk? Smpl { get; set; }
    public uint DataSize { get; set; }

    public List<RawChunkInfo> Chunks { get; set; } = new();
}

public class BextChunk
{
    public string Description { get; set; } = "";
    public string Originator { get; set; } = "";
    public string OriginatorReference { get; set; } = "";
    public string OriginationDate { get; set; } = "";
    public string OriginationTime { get; set; } = "";
    public uint TimeReferenceLow { get; set; }
    public uint TimeReferenceHigh { get; set; }
}

public class SmplChunk
{
    public uint Manufacturer { get; set; }
    public uint Product { get; set; }
    public uint SamplePeriod { get; set; }
    public uint MidiUnityNote { get; set; }
    public uint MidiPitchFraction { get; set; }
    public uint SmpteFormat { get; set; }
    public uint SmpteOffset { get; set; }
    public uint NumSampleLoops { get; set; }
    public uint SamplerData { get; set; }
}

public class RawChunkInfo
{
    public string ChunkId { get; set; } = "";
    public uint ChunkSize { get; set; }
    public long FileOffset { get; set; }
    public byte[]? HexDumpBytes { get; set; }
}
