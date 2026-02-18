using System.Text;
using System.Text.Json;
using MLoops.Models;

namespace MLoops.Services;

public static class AudioMetadataService
{
    public static AudioFileInfo ParseFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        var magic = ReadString(reader, 4);
        stream.Position = 0;

        var info = new AudioFileInfo
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length
        };

        if (magic == "RIFF")
        {
            info.FileType = AudioFileType.Wav;
            ParseWav(reader, info);
        }
        else if (magic == "fLaC")
        {
            info.FileType = AudioFileType.Flac;
            ParseFlac(reader, info);
        }
        else if (magic == "OggS")
        {
            info.FileType = AudioFileType.Ogg;
            ParseOgg(reader, info);
        }
        else
        {
            throw new NotSupportedException($"Unsupported file format (magic: {magic})");
        }

        return info;
    }

    #region WAV Parsing

    private static void ParseWav(BinaryReader reader, AudioFileInfo info)
    {
        var wav = new WavMetadata();
        info.WavMetadata = wav;

        // RIFF header
        wav.ChunkId = ReadString(reader, 4); // "RIFF"
        wav.ChunkSize = reader.ReadUInt32();
        wav.Format = ReadString(reader, 4);   // "WAVE"

        // Parse all chunks
        while (reader.BaseStream.Position < reader.BaseStream.Length - 8)
        {
            var chunkId = ReadString(reader, 4);
            var chunkSize = reader.ReadUInt32();
            var chunkStart = reader.BaseStream.Position;

            var rawChunk = new RawChunkInfo
            {
                ChunkId = chunkId,
                ChunkSize = chunkSize,
                FileOffset = chunkStart
            };

            switch (chunkId)
            {
                case "fmt ":
                    ParseFmtChunk(reader, wav, info);
                    break;
                case "data":
                    wav.DataSize = chunkSize;
                    reader.BaseStream.Position = chunkStart + chunkSize;
                    break;
                case "cue ":
                    ParseCueChunk(reader, info);
                    break;
                case "smpl":
                    ParseSmplChunk(reader, wav, info);
                    break;
                case "LIST":
                    ParseListChunk(reader, chunkSize, info);
                    break;
                case "bext":
                    ParseBextChunk(reader, wav);
                    break;
                case "fact":
                    wav.FactSampleLength = reader.ReadUInt32();
                    break;
                default:
                    // Unknown chunk: capture hex dump
                    var dumpSize = (int)Math.Min(chunkSize, 256);
                    rawChunk.HexDumpBytes = reader.ReadBytes(dumpSize);
                    reader.BaseStream.Position = chunkStart + chunkSize;
                    break;
            }

            wav.Chunks.Add(rawChunk);

            // Ensure we're at the right position (word-aligned)
            reader.BaseStream.Position = chunkStart + chunkSize;
            if (chunkSize % 2 != 0 && reader.BaseStream.Position < reader.BaseStream.Length)
                reader.BaseStream.Position++;
        }

        // Compute duration from data size and format
        if (wav.SampleRate > 0 && wav.NumChannels > 0 && wav.BitsPerSample > 0)
        {
            var bytesPerSample = wav.BitsPerSample / 8.0 * wav.NumChannels;
            if (bytesPerSample > 0)
                info.DurationSeconds = wav.DataSize / bytesPerSample / wav.SampleRate;
        }
    }

    private static void ParseFmtChunk(BinaryReader reader, WavMetadata wav, AudioFileInfo info)
    {
        wav.AudioFormat = reader.ReadUInt16();
        wav.NumChannels = reader.ReadUInt16();
        wav.SampleRate = reader.ReadUInt32();
        wav.ByteRate = reader.ReadUInt32();
        wav.BlockAlign = reader.ReadUInt16();
        wav.BitsPerSample = reader.ReadUInt16();
        wav.AudioFormatName = GetAudioFormatName(wav.AudioFormat);

        info.SampleRate = (int)wav.SampleRate;
        info.Channels = wav.NumChannels;
        info.BitsPerSample = wav.BitsPerSample;
    }

    private static void ParseCueChunk(BinaryReader reader, AudioFileInfo info)
    {
        var numCuePoints = reader.ReadUInt32();
        for (uint i = 0; i < numCuePoints; i++)
        {
            info.CuePoints.Add(new CuePoint
            {
                Id = reader.ReadUInt32(),
                Position = reader.ReadUInt32(),
                FccChunk = ReadString(reader, 4),
                ChunkStart = reader.ReadUInt32(),
                BlockStart = reader.ReadUInt32(),
                SampleOffset = reader.ReadUInt32()
            });
        }
    }

    private static void ParseSmplChunk(BinaryReader reader, WavMetadata wav, AudioFileInfo info)
    {
        var smpl = new SmplChunk
        {
            Manufacturer = reader.ReadUInt32(),
            Product = reader.ReadUInt32(),
            SamplePeriod = reader.ReadUInt32(),
            MidiUnityNote = reader.ReadUInt32(),
            MidiPitchFraction = reader.ReadUInt32(),
            SmpteFormat = reader.ReadUInt32(),
            SmpteOffset = reader.ReadUInt32(),
            NumSampleLoops = reader.ReadUInt32(),
            SamplerData = reader.ReadUInt32()
        };
        wav.Smpl = smpl;

        for (uint i = 0; i < smpl.NumSampleLoops; i++)
        {
            info.LoopPoints.Add(new LoopPoint
            {
                CuePointId = reader.ReadUInt32(),
                Type = reader.ReadUInt32(),
                Start = reader.ReadUInt32(),
                End = reader.ReadUInt32(),
                Fraction = reader.ReadUInt32(),
                PlayCount = (int)reader.ReadUInt32()
            });
        }
    }

    private static void ParseListChunk(BinaryReader reader, uint chunkSize, AudioFileInfo info)
    {
        var startPos = reader.BaseStream.Position;
        var listType = ReadString(reader, 4);

        if (listType == "INFO")
        {
            var endOffset = startPos + chunkSize;
            while (reader.BaseStream.Position < endOffset - 8)
            {
                var tagId = ReadString(reader, 4);
                var tagSize = reader.ReadUInt32();
                var tagValue = ReadString(reader, (int)tagSize).TrimEnd('\0');

                info.Tags.Add(new MetadataTag
                {
                    Key = tagId,
                    Value = tagValue
                });

                // Word-align
                if (tagSize % 2 != 0 && reader.BaseStream.Position < reader.BaseStream.Length)
                    reader.BaseStream.Position++;
            }
        }
    }

    private static void ParseBextChunk(BinaryReader reader, WavMetadata wav)
    {
        wav.Bext = new BextChunk
        {
            Description = ReadString(reader, 256).TrimEnd('\0'),
            Originator = ReadString(reader, 32).TrimEnd('\0'),
            OriginatorReference = ReadString(reader, 32).TrimEnd('\0'),
            OriginationDate = ReadString(reader, 10),
            OriginationTime = ReadString(reader, 8),
            TimeReferenceLow = reader.ReadUInt32(),
            TimeReferenceHigh = reader.ReadUInt32()
        };
    }

    #endregion

    #region FLAC Parsing

    private static void ParseFlac(BinaryReader reader, AudioFileInfo info)
    {
        var flac = new FlacMetadata();
        info.FlacMetadata = flac;

        // Skip "fLaC" marker
        reader.ReadBytes(4);

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var blockHeader = reader.ReadByte();
            var isLastBlock = (blockHeader & 0x80) != 0;
            var blockType = blockHeader & 0x7F;
            var blockSize = ReadBigEndian24(reader);
            var blockStart = reader.BaseStream.Position;

            switch (blockType)
            {
                case 0: // STREAMINFO
                    ParseStreamInfo(reader, flac, info);
                    break;
                case 2: // APPLICATION
                    ParseApplicationBlock(reader, blockSize, flac);
                    break;
                case 4: // VORBIS_COMMENT
                    ParseVorbisComment(reader, blockSize, flac, info);
                    break;
                case 5: // CUESHEET
                    ParseCueSheet(reader, flac);
                    break;
                case 6: // PICTURE
                    ParsePicture(reader, flac);
                    break;
                default:
                    var dumpSize = Math.Min(blockSize, 256);
                    flac.RawBlocks.Add(new FlacRawBlock
                    {
                        BlockType = blockType,
                        BlockTypeName = GetFlacBlockTypeName(blockType),
                        BlockSize = blockSize,
                        HexDumpBytes = reader.ReadBytes(dumpSize)
                    });
                    break;
            }

            reader.BaseStream.Position = blockStart + blockSize;
            if (isLastBlock) break;
        }

        // Compute duration from StreamInfo
        if (flac.StreamInfo is { SampleRate: > 0 })
            info.DurationSeconds = flac.StreamInfo.TotalSamples / (double)flac.StreamInfo.SampleRate;
    }

    private static void ParseStreamInfo(BinaryReader reader, FlacMetadata flac, AudioFileInfo info)
    {
        var si = new FlacStreamInfo();

        si.MinBlockSize = ReadBigEndian16(reader);
        si.MaxBlockSize = ReadBigEndian16(reader);
        si.MinFrameSize = (uint)ReadBigEndian24(reader);
        si.MaxFrameSize = (uint)ReadBigEndian24(reader);

        // Next 8 bytes contain sample rate (20 bits), channels (3 bits), bps (5 bits), total samples (36 bits)
        var bytes = reader.ReadBytes(8);
        si.SampleRate = (bytes[0] << 12) | (bytes[1] << 4) | (bytes[2] >> 4);
        si.Channels = ((bytes[2] >> 1) & 0x07) + 1;
        si.BitsPerSample = (((bytes[2] & 0x01) << 4) | (bytes[3] >> 4)) + 1;
        si.TotalSamples = ((long)(bytes[3] & 0x0F) << 32) |
                          ((long)bytes[4] << 24) |
                          ((long)bytes[5] << 16) |
                          ((long)bytes[6] << 8) |
                          bytes[7];

        flac.StreamInfo = si;
        info.SampleRate = si.SampleRate;
        info.Channels = si.Channels;
        info.BitsPerSample = si.BitsPerSample;
    }

    private static void ParseApplicationBlock(BinaryReader reader, int blockSize, FlacMetadata flac)
    {
        var appId = ReadString(reader, 4);
        var dataSize = blockSize - 4;
        var data = dataSize > 0 ? reader.ReadBytes(dataSize) : Array.Empty<byte>();

        flac.ApplicationBlocks.Add(new FlacApplicationBlock
        {
            ApplicationId = appId,
            Data = data,
            IsForeignMetadata = appId.Equals("riff", StringComparison.OrdinalIgnoreCase)
        });
    }

    private static void ParseVorbisComment(BinaryReader reader, int blockSize, FlacMetadata flac, AudioFileInfo info)
    {
        var startPos = reader.BaseStream.Position;
        var endPos = startPos + blockSize;

        var vendorLength = reader.ReadUInt32();
        var vendor = ReadUtf8String(reader, (int)vendorLength);

        var numComments = reader.ReadUInt32();
        var vc = new FlacVorbisComment { Vendor = vendor };

        for (uint i = 0; i < numComments && reader.BaseStream.Position < endPos; i++)
        {
            var commentLength = reader.ReadUInt32();
            var comment = ReadUtf8String(reader, (int)commentLength);

            var eqIndex = comment.IndexOf('=');
            if (eqIndex < 0) continue;

            var key = comment[..eqIndex].Trim();
            var value = comment[(eqIndex + 1)..].Trim();

            vc.Comments.Add(new MetadataTag { Key = key, Value = value });
            info.Tags.Add(new MetadataTag { Key = key, Value = value });

            // Detect loop points from Vorbis comments
            ParseLoopPointsFromComment(key, value, info);
        }

        flac.VorbisComment = vc;
    }

    private static void ParseCueSheet(BinaryReader reader, FlacMetadata flac)
    {
        var catalogBytes = reader.ReadBytes(128);
        var catalogNumber = Encoding.ASCII.GetString(catalogBytes).TrimEnd('\0');

        // Skip to num tracks at offset 396 from cue sheet start
        // We've read 128 bytes, need to skip to byte 396
        reader.BaseStream.Position += (396 - 128);
        var numTracks = reader.ReadByte();

        flac.CueSheet = new FlacCueSheet
        {
            CatalogNumber = catalogNumber,
            NumTracks = numTracks
        };
    }

    private static void ParsePicture(BinaryReader reader, FlacMetadata flac)
    {
        var pictureType = ReadBigEndian32(reader);
        var mimeLength = ReadBigEndian32(reader);
        var mimeType = ReadUtf8String(reader, (int)mimeLength);

        var descLength = ReadBigEndian32(reader);
        var description = ReadUtf8String(reader, (int)descLength);

        var width = ReadBigEndian32(reader);
        var height = ReadBigEndian32(reader);
        var colorDepth = ReadBigEndian32(reader);
        _ = ReadBigEndian32(reader); // indexed colors
        var pictureDataLength = ReadBigEndian32(reader);

        // Don't load large picture data into memory for now
        var pictureData = pictureDataLength <= 1024 * 1024
            ? reader.ReadBytes((int)pictureDataLength)
            : null;

        if (pictureData is null)
            reader.BaseStream.Position += pictureDataLength;

        flac.Pictures.Add(new FlacPicture
        {
            PictureType = pictureType,
            PictureTypeName = GetPictureTypeName(pictureType),
            MimeType = mimeType,
            Description = description,
            Width = width,
            Height = height,
            ColorDepth = colorDepth,
            PictureDataLength = pictureDataLength,
            PictureData = pictureData
        });
    }

    #endregion

    #region OGG Parsing

    private static void ParseOgg(BinaryReader reader, AudioFileInfo info)
    {
        var ogg = new OggMetadata();
        info.OggMetadata = ogg;

        var stream = reader.BaseStream;
        var foundVorbisId = false;
        var foundOpusId = false;
        ushort opusPreSkip = 0;
        ulong lastGranulePosition = 0;

        while (stream.Position < stream.Length - 27)
        {
            // Scan for OggS signature
            var b0 = reader.ReadByte();
            if (b0 != 0x4F) continue;
            if (stream.Position >= stream.Length - 3) break;

            var b1 = reader.ReadByte();
            var b2 = reader.ReadByte();
            var b3 = reader.ReadByte();
            if (b1 != 0x67 || b2 != 0x67 || b3 != 0x53)
            {
                stream.Position -= 3; // back up and keep scanning
                continue;
            }

            // Parse OGG page header
            var version = reader.ReadByte();
            var headerType = reader.ReadByte();
            var granulePosition = reader.ReadUInt64();
            var serialNumber = reader.ReadUInt32();
            var pageSequence = reader.ReadUInt32();
            var checksum = reader.ReadUInt32();
            var pageSegments = reader.ReadByte();

            // Read segment table
            var segmentSizes = reader.ReadBytes(pageSegments);
            var totalPayloadSize = 0;
            foreach (var s in segmentSizes)
                totalPayloadSize += s;

            var payloadStart = stream.Position;

            ogg.Pages.Add(new OggPage
            {
                PageSequence = (int)pageSequence,
                HeaderType = headerType,
                SerialNumber = serialNumber,
                PayloadSize = totalPayloadSize
            });

            if (granulePosition != ulong.MaxValue && granulePosition > lastGranulePosition)
                lastGranulePosition = granulePosition;

            if (totalPayloadSize > 0 && payloadStart + totalPayloadSize <= stream.Length)
            {
                var payloadByte = reader.ReadByte();

                // Vorbis identification header (type 1)
                if (payloadByte == 1 && !foundVorbisId)
                {
                    var sig = ReadString(reader, 6);
                    if (sig == "vorbis")
                    {
                        foundVorbisId = true;
                        // Parse identification header
                        var vorbisVersion = reader.ReadUInt32();
                        var channels = reader.ReadByte();
                        var sampleRate = reader.ReadUInt32();
                        info.Channels = channels;
                        info.SampleRate = (int)sampleRate;
                    }
                }
                // Opus identification header ("OpusHead")
                else if (payloadByte == (byte)'O' && !foundOpusId)
                {
                    var sig = "O" + ReadString(reader, 7);
                    if (sig == "OpusHead")
                    {
                        foundOpusId = true;
                        _ = reader.ReadByte(); // version
                        var channels = reader.ReadByte();
                        opusPreSkip = reader.ReadUInt16();
                        var inputSampleRate = reader.ReadUInt32();
                        _ = reader.ReadInt16(); // output gain
                        _ = reader.ReadByte();  // channel mapping family

                        info.Channels = channels;
                        info.SampleRate = inputSampleRate > 0 ? (int)inputSampleRate : 48000;
                        if (info.BitsPerSample <= 0)
                            info.BitsPerSample = 16;
                    }
                }
                // Vorbis comment header (type 3)
                else if (payloadByte == 3)
                {
                    var sig = ReadString(reader, 6);
                    if (sig == "vorbis")
                    {
                        ParseOggComments(reader, totalPayloadSize - 7, ogg, info);
                    }
                }
                // Opus comment header ("OpusTags")
                else if (payloadByte == (byte)'O')
                {
                    var sig = "O" + ReadString(reader, 7);
                    if (sig == "OpusTags")
                    {
                        ParseOggComments(reader, totalPayloadSize - 8, ogg, info);
                    }
                }
            }

            // Move to end of this page's payload
            stream.Position = payloadStart + totalPayloadSize;
        }

        if (lastGranulePosition > 0)
        {
            var pcmSamples = lastGranulePosition;
            if (foundOpusId)
                pcmSamples = pcmSamples > opusPreSkip ? pcmSamples - opusPreSkip : 0;

            var baseRate = foundOpusId ? 48000.0 : (info.SampleRate > 0 ? info.SampleRate : 48000);
            info.DurationSeconds = pcmSamples / baseRate;
        }
    }

    private static void ParseOggComments(BinaryReader reader, int size, OggMetadata ogg, AudioFileInfo info)
    {
        var startPos = reader.BaseStream.Position;
        var endPos = startPos + size;

        try
        {
            var vendorLength = reader.ReadUInt32();
            if (reader.BaseStream.Position + vendorLength > endPos) return;
            var vendor = ReadUtf8String(reader, (int)vendorLength);

            if (reader.BaseStream.Position + 4 > endPos) return;
            var numComments = reader.ReadUInt32();

            var vc = new OggVorbisComment { Vendor = vendor };

            for (uint i = 0; i < numComments && reader.BaseStream.Position < endPos; i++)
            {
                if (reader.BaseStream.Position + 4 > endPos) break;
                var commentLength = reader.ReadUInt32();
                if (reader.BaseStream.Position + commentLength > endPos) break;

                var comment = ReadUtf8String(reader, (int)commentLength);

                var eqIndex = comment.IndexOf('=');
                if (eqIndex < 0) continue;

                var key = comment[..eqIndex].Trim();
                var value = comment[(eqIndex + 1)..].Trim();

                vc.Comments.Add(new MetadataTag { Key = key, Value = value });
                info.Tags.Add(new MetadataTag { Key = key, Value = value });

                // Store standard metadata
                var keyUpper = key.ToUpperInvariant();
                if (keyUpper == "TITLE" || keyUpper == "ARTIST" || keyUpper == "ALBUM" ||
                    keyUpper == "DATE" || keyUpper == "COMMENT" || keyUpper == "GENRE")
                {
                    // These are stored in Tags already
                }

                ParseLoopPointsFromComment(key, value, info);
            }

            ogg.VorbisComment = vc;
        }
        catch
        {
            // Gracefully handle malformed comments
        }
    }

    #endregion

    #region Shared Loop Point Detection

    private static void ParseLoopPointsFromComment(string key, string value, AudioFileInfo info)
    {
        var keyUpper = key.ToUpperInvariant();

        if (keyUpper is "LOOP_POINTS" or "LOOPPOINTS" or "LOOP-POINTS")
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.TryGetProperty("start", out var startEl) &&
                            elem.TryGetProperty("end", out var endEl))
                        {
                            uint ReadUInt(JsonElement node, params string[] names)
                            {
                                foreach (var name in names)
                                {
                                    if (node.TryGetProperty(name, out var valueEl) && valueEl.TryGetUInt32(out var parsed))
                                        return parsed;

                                    if (node.TryGetProperty(name, out valueEl) && valueEl.ValueKind == JsonValueKind.Number)
                                        return (uint)Math.Max(0, valueEl.GetInt64());
                                }

                                return 0;
                            }

                            int ReadInt(JsonElement node, params string[] names)
                            {
                                foreach (var name in names)
                                {
                                    if (node.TryGetProperty(name, out var valueEl) && valueEl.TryGetInt32(out var parsed))
                                        return parsed;

                                    if (node.TryGetProperty(name, out valueEl) && valueEl.ValueKind == JsonValueKind.Number)
                                        return (int)valueEl.GetInt64();
                                }

                                return 0;
                            }

                            var lp = new LoopPoint
                            {
                                Start = (uint)startEl.GetInt64(),
                                End = (uint)endEl.GetInt64(),
                                CuePointId = ReadUInt(elem, "cuePointID", "cuePointId", "CuePointId", "cue_point_id"),
                                Type = ReadUInt(elem, "type", "Type"),
                                Fraction = ReadUInt(elem, "fraction", "Fraction"),
                                PlayCount = ReadInt(elem, "playCount", "play_count", "PlayCount", "loopPlayCount")
                            };

                            if (!info.LoopPoints.Any(existing => existing.Start == lp.Start && existing.End == lp.End))
                                info.LoopPoints.Add(lp);
                        }
                    }
                }
            }
            catch { }
        }
        else if (keyUpper is "LOOP_START" or "LOOPSTART")
        {
            if (uint.TryParse(value, out var n))
            {
                if (info.LoopPoints.Count == 0)
                    info.LoopPoints.Add(new LoopPoint());
                info.LoopPoints[0].Start = n;
            }
        }
        else if (keyUpper is "LOOP_END" or "LOOPEND")
        {
            if (uint.TryParse(value, out var n))
            {
                if (info.LoopPoints.Count == 0)
                    info.LoopPoints.Add(new LoopPoint());
                info.LoopPoints[0].End = n;
            }
        }
        else if (keyUpper is "LOOP_PLAY_COUNT" or "LOOPPLAYCOUNT" or "PLAY_COUNT" or "PLAYCOUNT")
        {
            if (int.TryParse(value, out var n))
            {
                if (info.LoopPoints.Count == 0)
                    info.LoopPoints.Add(new LoopPoint());
                info.LoopPoints[0].PlayCount = n;
            }
        }
    }

    #endregion

    #region Helper Methods

    private static string ReadString(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes)
        {
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    private static string ReadUtf8String(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static ushort ReadBigEndian16(BinaryReader reader)
    {
        var b = reader.ReadBytes(2);
        return (ushort)((b[0] << 8) | b[1]);
    }

    private static int ReadBigEndian24(BinaryReader reader)
    {
        var b = reader.ReadBytes(3);
        return (b[0] << 16) | (b[1] << 8) | b[2];
    }

    private static uint ReadBigEndian32(BinaryReader reader)
    {
        var b = reader.ReadBytes(4);
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 Bytes";
        var k = 1024.0;
        string[] sizes = ["Bytes", "KB", "MB", "GB"];
        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        i = Math.Min(i, sizes.Length - 1);
        return $"{bytes / Math.Pow(k, i):F2} {sizes[i]}";
    }

    public static string CreateHexDump(byte[] data, int maxBytes = 256)
    {
        var sb = new StringBuilder();
        var length = Math.Min(data.Length, maxBytes);

        for (var i = 0; i < length; i += 16)
        {
            var hex = new StringBuilder(48);
            var ascii = new StringBuilder(16);

            for (var j = 0; j < 16 && i + j < length; j++)
            {
                var b = data[i + j];
                hex.Append(b.ToString("x2"));
                hex.Append(' ');
                ascii.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            sb.Append(hex.ToString().PadRight(48));
            sb.Append(" | ");
            sb.AppendLine(ascii.ToString());
        }

        return sb.ToString();
    }

    public static string GetAudioFormatName(ushort format)
    {
        return format switch
        {
            0x0001 => "PCM",
            0x0003 => "IEEE Float",
            0x0006 => "A-law",
            0x0007 => "µ-law",
            0x0011 => "IMA ADPCM",
            0x0016 => "ITU G.723 ADPCM",
            0x0031 => "GSM 6.10",
            0x0040 => "ITU G.721 ADPCM",
            0x0050 => "MPEG",
            0xFFFE => "Extensible",
            _ => "Unknown"
        };
    }

    public static string GetInfoTagName(string tag)
    {
        return tag switch
        {
            "IARL" => "Archival Location",
            "IART" => "Artist",
            "ICMS" => "Commissioned",
            "ICMT" => "Comments",
            "ICOP" => "Copyright",
            "ICRD" => "Creation Date",
            "ICRP" => "Cropped",
            "IDIM" => "Dimensions",
            "IDPI" => "Dots Per Inch",
            "IENG" => "Engineer",
            "IGNR" => "Genre",
            "IKEY" => "Keywords",
            "ILGT" => "Lightness",
            "IMED" => "Medium",
            "INAM" => "Title",
            "IPLT" => "Palette",
            "IPRD" => "Product",
            "ISBJ" => "Subject",
            "ISFT" => "Software",
            "ISHP" => "Sharpness",
            "ISRC" => "Source",
            "ISRF" => "Source Form",
            "ITCH" => "Technician",
            _ => tag
        };
    }

    public static string GetFlacBlockTypeName(int type)
    {
        return type switch
        {
            0 => "STREAMINFO",
            1 => "PADDING",
            2 => "APPLICATION",
            3 => "SEEKTABLE",
            4 => "VORBIS_COMMENT",
            5 => "CUESHEET",
            6 => "PICTURE",
            _ => $"Unknown ({type})"
        };
    }

    public static string GetPictureTypeName(uint type)
    {
        return type switch
        {
            0 => "Other",
            1 => "32x32 Icon",
            2 => "Other Icon",
            3 => "Cover (front)",
            4 => "Cover (back)",
            5 => "Leaflet page",
            6 => "Media",
            7 => "Lead artist",
            8 => "Artist",
            9 => "Conductor",
            10 => "Band",
            11 => "Composer",
            12 => "Lyricist",
            13 => "Recording Location",
            14 => "During recording",
            15 => "During performance",
            16 => "Screen capture",
            17 => "Fish",
            18 => "Illustration",
            19 => "Band logo",
            20 => "Publisher logo",
            _ => "Unknown"
        };
    }

    public static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1
            ? ts.ToString(@"hh\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }

    #endregion
}
