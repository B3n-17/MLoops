using System.Text.Json.Serialization;

namespace MLoops.Services;

internal sealed class CuePointMetadataEntry
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }

    [JsonPropertyName("position")]
    public uint Position { get; set; }

    [JsonPropertyName("fccChunk")]
    public string FccChunk { get; set; } = "data";

    [JsonPropertyName("chunkStart")]
    public uint ChunkStart { get; set; }

    [JsonPropertyName("blockStart")]
    public uint BlockStart { get; set; }

    [JsonPropertyName("sampleOffset")]
    public uint SampleOffset { get; set; }
}

internal sealed class LoopPointMetadataEntry
{
    [JsonPropertyName("cuePointID")]
    public uint CuePointId { get; set; }

    [JsonPropertyName("type")]
    public uint Type { get; set; }

    [JsonPropertyName("start")]
    public uint Start { get; set; }

    [JsonPropertyName("end")]
    public uint End { get; set; }

    [JsonPropertyName("fraction")]
    public uint Fraction { get; set; }

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CuePointMetadataEntry[]))]
[JsonSerializable(typeof(LoopPointMetadataEntry[]))]
internal partial class MLoopsJsonSerializerContext : JsonSerializerContext
{
}
