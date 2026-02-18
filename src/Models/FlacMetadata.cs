namespace MLoops.Models;

public class FlacMetadata
{
    public FlacStreamInfo? StreamInfo { get; set; }
    public List<FlacApplicationBlock> ApplicationBlocks { get; set; } = new();
    public FlacVorbisComment? VorbisComment { get; set; }
    public FlacCueSheet? CueSheet { get; set; }
    public List<FlacPicture> Pictures { get; set; } = new();
    public List<FlacRawBlock> RawBlocks { get; set; } = new();
}

public class FlacStreamInfo
{
    public ushort MinBlockSize { get; set; }
    public ushort MaxBlockSize { get; set; }
    public uint MinFrameSize { get; set; }
    public uint MaxFrameSize { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public long TotalSamples { get; set; }
}

public class FlacApplicationBlock
{
    public string ApplicationId { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public bool IsForeignMetadata { get; set; }
}

public class FlacVorbisComment
{
    public string Vendor { get; set; } = "";
    public List<MetadataTag> Comments { get; set; } = new();
}

public class FlacCueSheet
{
    public string CatalogNumber { get; set; } = "";
    public int NumTracks { get; set; }
}

public class FlacPicture
{
    public uint PictureType { get; set; }
    public string PictureTypeName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Description { get; set; } = "";
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint ColorDepth { get; set; }
    public uint PictureDataLength { get; set; }
    public byte[]? PictureData { get; set; }
}

public class FlacRawBlock
{
    public int BlockType { get; set; }
    public string BlockTypeName { get; set; } = "";
    public int BlockSize { get; set; }
    public byte[]? HexDumpBytes { get; set; }
}
