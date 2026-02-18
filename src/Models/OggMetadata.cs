namespace MLoops.Models;

public class OggMetadata
{
    public List<OggPage> Pages { get; set; } = new();
    public OggVorbisComment? VorbisComment { get; set; }
}

public class OggPage
{
    public int PageSequence { get; set; }
    public byte HeaderType { get; set; }
    public uint SerialNumber { get; set; }
    public int PayloadSize { get; set; }
}

public class OggVorbisComment
{
    public string Vendor { get; set; } = "";
    public List<MetadataTag> Comments { get; set; } = new();
}
