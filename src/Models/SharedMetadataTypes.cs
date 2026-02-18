namespace MLoops.Models;

public class CuePoint
{
    public uint Id { get; set; }
    public uint Position { get; set; }
    public string FccChunk { get; set; } = "data";
    public uint ChunkStart { get; set; }
    public uint BlockStart { get; set; }
    public uint SampleOffset { get; set; }
}

public class LoopPoint
{
    public uint CuePointId { get; set; }
    public uint Type { get; set; }
    public uint Start { get; set; }
    public uint End { get; set; }
    public uint Fraction { get; set; }
    public int PlayCount { get; set; }

    public string TypeName => Type switch
    {
        0 => "Forward",
        1 => "Ping-Pong",
        2 => "Reverse",
        _ => "Unknown"
    };
}

public class MetadataTag
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
