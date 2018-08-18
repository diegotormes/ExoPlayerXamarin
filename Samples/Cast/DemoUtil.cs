using System.Collections.Generic;
using Com.Google.Android.Exoplayer2.Util;

namespace Com.Google.Android.Exoplayer2.CastDemo
{
    /**
 * Utility methods and constants for the Cast demo application.
 */
    /* package */
    internal class DemoUtil
    {

        public const string MimeTypeDash = MimeTypes.ApplicationMpd;
        public const string MimeTypeHls = MimeTypes.ApplicationM3u8;
        public const string MimeTypeSs = MimeTypes.ApplicationSs;
        public const string MimeTypeVideoMp4 = MimeTypes.VideoMp4;
        public const string MimeTypeAudio = MimeTypes.AudioAac;

        /**
         * The list of samples available in the cast demo app.
         */
        public static readonly List<Sample> Samples = new List<Sample> {
            new Sample("https://storage.googleapis.com/wvmedia/clear/h264/tears/tears.mpd", "DASH (clear,MP4,H264)", MimeTypeDash),
            new Sample("https://commondatastorage.googleapis.com/gtv-videos-bucket/CastVideos/hls/TearsOfSteel.m3u8", "Tears of Steel (HLS)", MimeTypeHls),
            new Sample("https://html5demos.com/assets/dizzy.mp4", "Dizzy (MP4)", MimeTypeVideoMp4),
            new Sample("https://storage.googleapis.com/exoplayer-test-media-1/ogg/play.ogg", "Google Play (Ogg/Vorbis Audio)", MimeTypeAudio)
        };

        /**
         * Represents a media sample.
         */
        public class Sample
        {

            /**
             * The uri from which the media sample is obtained.
             */
            public string Uri;
            /**
             * A descriptive name for the sample.
             */
            public string Name;
            /**
             * The mime type of the media sample, as required by {@link MediaInfo#setContentType}.
             */
            public string MimeType;

            /**
             * @param uri See {@link #uri}.
             * @param name See {@link #name}.
             * @param mimeType See {@link #mimeType}.
             */
            public Sample(string uri, string name, string mimeType)
            {
                Uri = uri;
                Name = name;
                MimeType = mimeType;
            }

            public override string ToString()
            {
                return Name;
            }

        }

        private DemoUtil() { }

    }
}
