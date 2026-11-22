// Playback decision (port of packages/plex/src/decision.ts): direct play vs direct stream vs transcode,
// judged against the QN85B profile. Direct play keeps 4K/HDR at original quality via the TV's native player.
using System;
using System.Linq;

namespace Warp.Core
{
    public static class Decision
    {
        static readonly string[] Html5Video = { "h264" };
        static readonly string[] Html5Audio = { "aac", "mp3" };
        static readonly string[] Html5Container = { "mp4", "m4v", "mov" };

        static string Norm(string v) => (v ?? "").Trim().ToLowerInvariant();

        static Stream VideoStream(Part part) => part.Streams.FirstOrDefault(s => s.StreamType == 1);

        static Stream AudioStream(Part part)
        {
            var audio = part.Streams.Where(s => s.StreamType == 2).ToList();
            return audio.FirstOrDefault(s => s.Selected) ?? audio.FirstOrDefault();
        }

        public static string HdrOf(Stream s)
        {
            if (s == null) return "unknown";
            if (s.DoviPresent && s.DoviProfile == 5) return "dolby-vision-profile5";
            if (s.DoviPresent) return "dolby-vision-with-base";
            var trc = Norm(s.ColorTrc);
            if (trc == "smpte2084" || trc == "pq") return "hdr10";
            if (trc == "arib-std-b67") return "hlg";
            var title = Norm(s.DisplayTitle);
            if (title.Contains("hdr10+")) return "hdr10+";
            if (title.Contains("hdr")) return "hdr10";
            if (title.Contains("hlg")) return "hlg";
            return "sdr";
        }

        static string Resolution(Stream s) => s?.Width == null || s.Height == null || s.Width == 0 || s.Height == 0 ? "unknown" : $"{s.Width}x{s.Height}";

        static double Bitrate(Part part, Stream video)
        {
            if (video?.Bitrate is double b && b > 0) return b;
            return part.Streams.Sum(s => s.Bitrate ?? 0);
        }

        public static PlaybackDecision Decide(MediaItem item, Engine engine)
        {
            var part = item.Parts.FirstOrDefault();
            var video = part != null ? VideoStream(part) : null;
            var audio = part != null ? AudioStream(part) : null;
            var container = Norm(part?.Container);
            var videoCodec = Norm(video?.Codec);
            var audioCodec = Norm(audio?.Codec);
            var hdr = HdrOf(video);
            var d = new PlaybackDecision
            {
                VideoCodec = videoCodec.Length > 0 ? videoCodec : "unknown",
                AudioCodec = audioCodec.Length > 0 ? audioCodec : "none",
                Hdr = hdr,
                Resolution = Resolution(video),
                Bitrate = part != null ? Bitrate(part, video) : 0,
                Container = container.Length > 0 ? container : "unknown",
                PartKey = part?.Key ?? "",
                RatingKey = item.RatingKey,
                StartMs = item.ViewOffset ?? 0,
                DurationMs = item.Duration ?? part?.Duration ?? 0,
            };
            bool native = engine == Engine.Native;
            string name = native ? "native" : "html5";
            bool videoOk = native ? Qn85bProfile.Video.Contains(videoCodec) : Html5Video.Contains(videoCodec);
            bool audioOk = audioCodec.Length == 0 || (native ? Qn85bProfile.Audio.Contains(audioCodec) : Html5Audio.Contains(audioCodec));
            bool containerOk = native ? Qn85bProfile.Containers.Contains(container) : Html5Container.Contains(container);
            bool hdrOk = !native || hdr != "dolby-vision-profile5";

            if (!videoOk || !hdrOk)
            {
                d.Mode = PlaybackMode.Transcode;
                d.Reason = !videoOk ? $"{videoCodec} is outside the {name} profile" : "Dolby Vision profile 5 has no HDR10 base on this panel";
                return d;
            }
            if (!containerOk || !audioOk)
            {
                d.Mode = PlaybackMode.DirectStream;
                d.Reason = !containerOk ? $"{(container.Length > 0 ? container : "unknown")} container needs remux" : $"{audioCodec} audio needs a compatible stream";
                return d;
            }
            d.Mode = PlaybackMode.DirectPlay;
            d.Reason = $"{name} profile matches container, video, audio, and HDR";
            return d;
        }

        public static string PlaybackUrl(string serverUri, string token, string clientId, PlaybackDecision d)
        {
            var b = serverUri.TrimEnd('/');
            var common = $"X-Plex-Token={Uri.EscapeDataString(token)}&X-Plex-Client-Identifier={Uri.EscapeDataString(clientId)}&X-Plex-Platform=Tizen&X-Plex-Product=Warp";
            if (d.Mode == PlaybackMode.DirectPlay) return $"{b}{d.PartKey}?{common}";
            var ds = d.Mode == PlaybackMode.DirectStream ? "1" : "0";
            var path = $"/library/metadata/{d.RatingKey}";
            return $"{b}/video/:/transcode/universal/start.mkv?hasMDE=1&path={Uri.EscapeDataString(path)}&mediaIndex=0&partIndex=0&protocol=http&directPlay=0&directStream={ds}&copyts=1&subtitleSize=100&audioBoost=100&session={Uri.EscapeDataString(clientId)}&{common}";
        }
    }
}
