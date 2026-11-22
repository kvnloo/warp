// Plex data model (port of packages/plex/src/types.ts). Pure C# 8, no Tizen APIs: compiled into the TV app
// (tizen90 / .NET Core 3.1 runtime) and into the net8.0 unit-test project.
using System.Collections.Generic;

namespace Warp.Core
{
    public enum PlaybackMode { DirectPlay, DirectStream, Transcode }

    /// <summary>Which player judges compatibility. Native = the TV's own media pipeline (Tizen.Multimedia.Player,
    /// same decoder set AVPlay used). Html5 = a browser video element (kept for parity with the web port's tests).</summary>
    public enum Engine { Native, Html5 }

    public class Stream
    {
        public string Id;
        public bool Selected;
        public string Language;
        public int StreamType = 1; // 1 video, 2 audio, 3 subtitle
        public string Codec = "";
        public double? Bitrate;
        public int? Width;
        public int? Height;
        public string DisplayTitle;
        public int? Channels;
        public string ColorTrc;
        public bool DoviPresent;
        public int? DoviProfile;

        public Stream Clone() => (Stream)MemberwiseClone();
    }

    public class Part
    {
        public string Id;
        /// <summary>Human label of the Media version this part belongs to, e.g. "4K HEVC HDR".</summary>
        public string Label;
        public string Key = "";
        public string Container = "";
        public double? Duration;
        public List<Stream> Streams = new List<Stream>();

        public Part Clone()
        {
            var p = (Part)MemberwiseClone();
            p.Streams = new List<Stream>();
            foreach (var s in Streams) p.Streams.Add(s.Clone());
            return p;
        }
    }

    public class MediaItem
    {
        public string RatingKey = "";
        public string Key = "";
        public string Type = "";
        public string Title = "";
        public int? Year;
        public string Summary;
        public string Thumb;
        public string Art;
        public double? Duration;
        public double? ViewOffset;
        public string GrandparentTitle;
        public string ParentTitle;
        public int? Index;
        public int? ParentIndex;
        public string ParentRatingKey;
        public string GrandparentRatingKey;
        public string GrandparentArt;
        public string ContentRating;
        public double? AudienceRating;
        public string Tagline;
        public List<string> Genres;
        public int? LeafCount;
        public int? ViewedLeafCount;
        public int? ViewCount;
        public List<Part> Parts = new List<Part>();

        public MediaItem Clone()
        {
            var m = (MediaItem)MemberwiseClone();
            m.Parts = new List<Part>();
            foreach (var p in Parts) m.Parts.Add(p.Clone());
            if (Genres != null) m.Genres = new List<string>(Genres);
            return m;
        }
    }

    public class Hub
    {
        public string Identifier = "";
        public string Title = "";
        public List<MediaItem> Items = new List<MediaItem>();
    }

    public class PlaybackDecision
    {
        public PlaybackMode Mode;
        public string Reason = "";
        public string VideoCodec = "";
        public string AudioCodec = "";
        public string Hdr = "";
        public string Resolution = "";
        public double Bitrate;
        public string Container = "";
        public string PartKey = "";
        public string RatingKey = "";
        public double StartMs;
        public double DurationMs;

        public string ModeLabel => Mode == PlaybackMode.DirectPlay ? "direct-play" : Mode == PlaybackMode.DirectStream ? "direct-stream" : "transcode";
    }

    public class ServerConnection
    {
        public string Uri = "";
        public bool Local;
        public bool Relay;
    }

    public class PlexServer
    {
        public string Name = "";
        public string ClientIdentifier = "";
        public bool Owned;
        public List<ServerConnection> Connections = new List<ServerConnection>();
    }

    public class PinStart
    {
        public long Id;
        public string Code = "";
    }

    public class Sections
    {
        public string MovieSectionId;
        public string ShowSectionId;
    }

    /// <summary>Samsung QN85B (Tizen 6.5) decoder profile, unchanged from the web port.</summary>
    public static class Qn85bProfile
    {
        public const string Id = "samsung-qn85b-tizen-6.5";
        public static readonly string[] Video = { "h264", "hevc", "h265", "vp9", "av1", "mpeg4", "mpeg2video", "wmv3", "vc1" };
        public static readonly string[] Audio = { "aac", "mp3", "ac3", "eac3", "mp2", "pcm", "flac", "vorbis", "opus", "dts", "dca", "truehd" };
        public static readonly string[] Containers = { "mp4", "m4v", "mkv", "mov", "ts", "m2ts", "avi", "wmv", "asf" };
        public static readonly string[] Hdr = { "sdr", "hdr10", "hdr10+", "hlg" };
    }
}
