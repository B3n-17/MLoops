using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MLoops.Models;
using MLoops.Services;

namespace MLoops.ViewModels;

public partial class MetadataViewModel : ViewModelBase
{
    [ObservableProperty] private ObservableCollection<MetadataSection> _sections = new();

    public void LoadFromAudioFile(AudioFileInfo info)
    {
        Sections.Clear();

        // File Information
        var fileSection = new MetadataSection { Title = "File Information" };
        fileSection.Items.Add(new MetadataItem("File Name", info.FileName));
        fileSection.Items.Add(new MetadataItem("File Size", AudioMetadataService.FormatBytes(info.FileSize)));
        fileSection.Items.Add(new MetadataItem("File Type", info.FileType.ToString()));

        if (info.SampleRate > 0)
            fileSection.Items.Add(new MetadataItem("Sample Rate", $"{info.SampleRate:N0} Hz"));
        if (info.Channels > 0)
            fileSection.Items.Add(new MetadataItem("Channels", info.Channels.ToString()));
        if (info.BitsPerSample > 0)
            fileSection.Items.Add(new MetadataItem("Bits Per Sample", info.BitsPerSample.ToString()));
        if (info.DurationSeconds > 0)
            fileSection.Items.Add(new MetadataItem("Duration", AudioMetadataService.FormatTime(info.DurationSeconds)));

        Sections.Add(fileSection);

        // Format-specific sections
        switch (info.FileType)
        {
            case AudioFileType.Wav:
                LoadWavSections(info);
                break;
            case AudioFileType.Flac:
                LoadFlacSections(info);
                break;
            case AudioFileType.Ogg:
                LoadOggSections(info);
                break;
        }

        // Cue Points
        if (info.CuePoints.Count > 0)
        {
            var cueSection = new MetadataSection { Title = $"Cue Points ({info.CuePoints.Count})" };
            foreach (var (cue, i) in info.CuePoints.Select((c, i) => (c, i)))
            {
                cueSection.Items.Add(new MetadataItem($"Cue {i + 1} (ID: {cue.Id})",
                    $"Position: {cue.Position}, Chunk: {cue.FccChunk}, Sample Offset: {cue.SampleOffset}"));
            }
            Sections.Add(cueSection);
        }

        // Loop Points
        if (info.LoopPoints.Count > 0)
        {
            var loopSection = new MetadataSection { Title = $"Loop Points ({info.LoopPoints.Count})" };
            foreach (var (loop, i) in info.LoopPoints.Select((l, i) => (l, i)))
            {
                var playCountStr = loop.PlayCount switch
                {
                    -1 => "Infinite",
                    0 => "Once",
                    _ => loop.PlayCount.ToString()
                };
                loopSection.Items.Add(new MetadataItem($"Loop {i + 1} ({loop.TypeName})",
                    $"Start: {loop.Start}, End: {loop.End}, Play Count: {playCountStr}"));
            }
            Sections.Add(loopSection);
        }
    }

    private void LoadWavSections(AudioFileInfo info)
    {
        var wav = info.WavMetadata;
        if (wav is null) return;

        // RIFF Header
        var riffSection = new MetadataSection { Title = "RIFF Header" };
        riffSection.Items.Add(new MetadataItem("Chunk ID", wav.ChunkId));
        riffSection.Items.Add(new MetadataItem("Chunk Size", AudioMetadataService.FormatBytes(wav.ChunkSize)));
        riffSection.Items.Add(new MetadataItem("Format", wav.Format));
        Sections.Add(riffSection);

        // Format
        var fmtSection = new MetadataSection { Title = "Format (fmt)" };
        fmtSection.Items.Add(new MetadataItem("Audio Format", $"{wav.AudioFormatName} (0x{wav.AudioFormat:X4})"));
        fmtSection.Items.Add(new MetadataItem("Channels", wav.NumChannels.ToString()));
        fmtSection.Items.Add(new MetadataItem("Sample Rate", $"{wav.SampleRate:N0} Hz"));
        fmtSection.Items.Add(new MetadataItem("Byte Rate", $"{wav.ByteRate:N0} bytes/sec"));
        fmtSection.Items.Add(new MetadataItem("Block Align", $"{wav.BlockAlign} bytes"));
        fmtSection.Items.Add(new MetadataItem("Bits Per Sample", wav.BitsPerSample.ToString()));
        Sections.Add(fmtSection);

        // Bext
        if (wav.Bext is not null)
        {
            var bextSection = new MetadataSection { Title = "Broadcast Extension (bext)" };
            bextSection.Items.Add(new MetadataItem("Description", wav.Bext.Description.Length > 0 ? wav.Bext.Description : "N/A"));
            bextSection.Items.Add(new MetadataItem("Originator", wav.Bext.Originator.Length > 0 ? wav.Bext.Originator : "N/A"));
            bextSection.Items.Add(new MetadataItem("Date", wav.Bext.OriginationDate));
            bextSection.Items.Add(new MetadataItem("Time", wav.Bext.OriginationTime));
            Sections.Add(bextSection);
        }

        // Smpl
        if (wav.Smpl is not null)
        {
            var smplSection = new MetadataSection { Title = "Sample (smpl)" };
            smplSection.Items.Add(new MetadataItem("MIDI Unity Note", wav.Smpl.MidiUnityNote.ToString()));
            smplSection.Items.Add(new MetadataItem("Sample Period", $"{wav.Smpl.SamplePeriod} ns"));
            smplSection.Items.Add(new MetadataItem("Number of Loops", wav.Smpl.NumSampleLoops.ToString()));
            Sections.Add(smplSection);
        }

        // Fact
        if (wav.FactSampleLength.HasValue)
        {
            var factSection = new MetadataSection { Title = "Fact" };
            factSection.Items.Add(new MetadataItem("Sample Length", $"{wav.FactSampleLength.Value:N0} samples"));
            Sections.Add(factSection);
        }

        // INFO Tags
        if (info.Tags.Count > 0)
        {
            var infoSection = new MetadataSection { Title = "INFO Tags" };
            foreach (var tag in info.Tags)
            {
                var displayName = AudioMetadataService.GetInfoTagName(tag.Key);
                infoSection.Items.Add(new MetadataItem($"{displayName} ({tag.Key})", tag.Value));
            }
            Sections.Add(infoSection);
        }

        // Unknown chunks with hex dumps
        foreach (var chunk in wav.Chunks.Where(c => c.HexDumpBytes is not null))
        {
            var chunkSection = new MetadataSection { Title = $"Chunk: {chunk.ChunkId} ({AudioMetadataService.FormatBytes(chunk.ChunkSize)})" };
            chunkSection.Items.Add(new MetadataItem("Hex Dump", "", AudioMetadataService.CreateHexDump(chunk.HexDumpBytes!)));
            Sections.Add(chunkSection);
        }
    }

    private void LoadFlacSections(AudioFileInfo info)
    {
        var flac = info.FlacMetadata;
        if (flac is null) return;

        // StreamInfo
        if (flac.StreamInfo is not null)
        {
            var si = flac.StreamInfo;
            var siSection = new MetadataSection { Title = "STREAMINFO" };
            siSection.Items.Add(new MetadataItem("Sample Rate", $"{si.SampleRate:N0} Hz"));
            siSection.Items.Add(new MetadataItem("Channels", si.Channels.ToString()));
            siSection.Items.Add(new MetadataItem("Bits Per Sample", si.BitsPerSample.ToString()));
            siSection.Items.Add(new MetadataItem("Total Samples", $"{si.TotalSamples:N0}"));
            if (si.SampleRate > 0)
                siSection.Items.Add(new MetadataItem("Duration", $"{si.TotalSamples / (double)si.SampleRate:F2} seconds"));
            Sections.Add(siSection);
        }

        // Application blocks
        foreach (var app in flac.ApplicationBlocks)
        {
            var appSection = new MetadataSection { Title = $"APPLICATION: {app.ApplicationId}" };
            if (app.IsForeignMetadata)
                appSection.Items.Add(new MetadataItem("Type", "Foreign Metadata (RIFF/WAV chunks detected)"));
            if (app.Data.Length > 0)
                appSection.Items.Add(new MetadataItem("Data", "", AudioMetadataService.CreateHexDump(app.Data)));
            Sections.Add(appSection);
        }

        // Vorbis Comments
        if (flac.VorbisComment is not null)
        {
            var vcSection = new MetadataSection { Title = "Vorbis Comments" };
            vcSection.Items.Add(new MetadataItem("Vendor", flac.VorbisComment.Vendor));
            foreach (var comment in flac.VorbisComment.Comments)
                vcSection.Items.Add(new MetadataItem(comment.Key, comment.Value));
            Sections.Add(vcSection);
        }

        // Cue Sheet
        if (flac.CueSheet is not null)
        {
            var csSection = new MetadataSection { Title = "CUESHEET" };
            csSection.Items.Add(new MetadataItem("Catalog Number", flac.CueSheet.CatalogNumber.Length > 0 ? flac.CueSheet.CatalogNumber : "N/A"));
            csSection.Items.Add(new MetadataItem("Number of Tracks", flac.CueSheet.NumTracks.ToString()));
            Sections.Add(csSection);
        }

        // Pictures
        foreach (var pic in flac.Pictures)
        {
            var picSection = new MetadataSection { Title = $"PICTURE: {pic.PictureTypeName}" };
            picSection.Items.Add(new MetadataItem("MIME Type", pic.MimeType));
            picSection.Items.Add(new MetadataItem("Dimensions", $"{pic.Width} x {pic.Height}"));
            picSection.Items.Add(new MetadataItem("Picture Size", AudioMetadataService.FormatBytes(pic.PictureDataLength)));
            Sections.Add(picSection);
        }

        // Raw blocks
        foreach (var block in flac.RawBlocks)
        {
            var blockSection = new MetadataSection { Title = $"{block.BlockTypeName} ({AudioMetadataService.FormatBytes(block.BlockSize)})" };
            if (block.HexDumpBytes is not null)
                blockSection.Items.Add(new MetadataItem("Hex Dump", "", AudioMetadataService.CreateHexDump(block.HexDumpBytes)));
            Sections.Add(blockSection);
        }
    }

    private void LoadOggSections(AudioFileInfo info)
    {
        var ogg = info.OggMetadata;
        if (ogg is null) return;

        // Pages summary
        if (ogg.Pages.Count > 0)
        {
            var pageSection = new MetadataSection { Title = $"OGG Pages ({ogg.Pages.Count})" };
            foreach (var page in ogg.Pages.Take(10)) // Limit display
            {
                pageSection.Items.Add(new MetadataItem(
                    $"Page {page.PageSequence}",
                    $"Type: 0x{page.HeaderType:X2}, Serial: {page.SerialNumber}, Payload: {AudioMetadataService.FormatBytes(page.PayloadSize)}"));
            }
            if (ogg.Pages.Count > 10)
                pageSection.Items.Add(new MetadataItem("...", $"{ogg.Pages.Count - 10} more pages"));
            Sections.Add(pageSection);
        }

        // Vorbis Comments
        if (ogg.VorbisComment is not null)
        {
            var vcSection = new MetadataSection { Title = "Vorbis Comments" };
            vcSection.Items.Add(new MetadataItem("Vendor", ogg.VorbisComment.Vendor));
            foreach (var comment in ogg.VorbisComment.Comments)
                vcSection.Items.Add(new MetadataItem(comment.Key, comment.Value));
            Sections.Add(vcSection);
        }
    }
}

public class MetadataSection
{
    public string Title { get; set; } = "";
    public ObservableCollection<MetadataItem> Items { get; set; } = new();
    public bool IsExpanded { get; set; } = true;
}

public class MetadataItem
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string? HexDump { get; set; }

    public MetadataItem() { }
    public MetadataItem(string label, string value, string? hexDump = null)
    {
        Label = label;
        Value = value;
        HexDump = hexDump;
    }
}
