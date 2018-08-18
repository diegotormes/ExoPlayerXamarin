/*
 * Copyright (C) 2016 The Android Open Source Project
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Android.OS;
using Android.Util;
using Com.Google.Android.Exoplayer2.Audio;
using Com.Google.Android.Exoplayer2.Decoder;
using Com.Google.Android.Exoplayer2.Drm;
using Com.Google.Android.Exoplayer2.Metadata;
using Com.Google.Android.Exoplayer2.Metadata.Emsg;
using Com.Google.Android.Exoplayer2.Metadata.Id3;
using Com.Google.Android.Exoplayer2.Source;
using Com.Google.Android.Exoplayer2.Trackselection;
using Com.Google.Android.Exoplayer2.Video;
using Java.IO;
using Java.Text;
using Java.Util;
using Java.Lang;
using MetadataObj = Com.Google.Android.Exoplayer2.Metadata.Metadata;
using Android.Views;

namespace Com.Google.Android.Exoplayer2.Demo
{
    /**
	 * Logs player events using {@link Log}.
	 */
    internal sealed class EventLogger : Object, IPlayerEventListener, IAudioRendererEventListener,
        IVideoRendererEventListener, IMediaSourceEventListener, IDefaultDrmSessionEventListener, IMetadataOutput
    {

        private const string Tag = "EventLogger";
        private const int MaxTimelineItemLines = 3;
        private static readonly NumberFormat TimeFormat;
        static EventLogger()
        {
            TimeFormat = NumberFormat.GetInstance(Locale.Us);
            TimeFormat.MinimumFractionDigits = 2;
            TimeFormat.MaximumFractionDigits = 2;
            TimeFormat.GroupingUsed = false;
        }

        private readonly DefaultTrackSelector _trackSelector;
        private readonly Timeline.Window _window;
        private readonly Timeline.Period _period;
        private readonly long _startTimeMs;

        public EventLogger(DefaultTrackSelector trackSelector)
        {
            _trackSelector = trackSelector;
            _window = new Timeline.Window();
            _period = new Timeline.Period();
            _startTimeMs = SystemClock.ElapsedRealtime();
        }

        #region Player.EventListener
        public void OnLoadingChanged(bool isLoading)
        {
            Log.Debug(Tag, "loading [" + isLoading + "]");
        }

        public void OnPlayerStateChanged(bool playWhenReady, int state)
        {
            Log.Debug(Tag, "state [" + GetSessionTimeString() + ", " + playWhenReady + ", "
                + GetStateString(state) + "]");
        }

        public void OnPositionDiscontinuity(int reason)
        {
            Log.Debug(Tag, "discontinuity [" + GetSessionTimeString() + ", " + reason + "]");
        }

        public void OnRepeatModeChanged(int repeatMode)
        {
            Log.Debug(Tag, "repeatMode [" + GetRepeatModeString(repeatMode) + "]");
        }

        public void OnSeekProcessed()
        {
            Log.Debug(Tag, "seek [" + GetSessionTimeString() + "]");
        }

        public void OnShuffleModeEnabledChanged(bool enabled)
        {
            Log.Debug(Tag, "shuffle [" + GetSessionTimeString() + ", " + enabled + "]");
        }

        public void OnPlaybackParametersChanged(PlaybackParameters playbackParameters)
        {
            Log.Debug(Tag, "playbackParameters " + String.Format(
                "[speed=%.2f, pitch=%.2f]", playbackParameters.Speed, playbackParameters.Pitch));
        }

        public void OnTimelineChanged(Timeline timeline, Object manifest, int reason)
        {
            var periodCount = timeline.PeriodCount;
            var windowCount = timeline.WindowCount;
            Log.Debug(Tag, "sourceInfo [periodCount=" + periodCount + ", windowCount=" + windowCount);
            for (var i = 0; i < Math.Min(periodCount, MaxTimelineItemLines); i++)
            {
                timeline.GetPeriod(i, _period);
                Log.Debug(Tag, "  " + "period [" + GetTimeString(_period.DurationMs) + "]");
            }
            if (periodCount > MaxTimelineItemLines)
            {
                Log.Debug(Tag, "  ...");
            }
            for (var i = 0; i < Math.Min(windowCount, MaxTimelineItemLines); i++)
            {
                timeline.GetWindow(i, _window);
                Log.Debug(Tag, "  " + "window [" + GetTimeString(_window.DurationMs) + ", "
                    + _window.IsSeekable + ", " + _window.IsDynamic + "]");
            }
            if (windowCount > MaxTimelineItemLines)
            {
                Log.Debug(Tag, "  ...");
            }
            Log.Debug(Tag, ", " + reason + "]");
        }

        public void OnPlayerError(ExoPlaybackException e)
        {
            Log.Error(Tag, "playerFailed [" + GetSessionTimeString() + "]", e);
        }

        public void OnTracksChanged(TrackGroupArray ignored, TrackSelectionArray trackSelections)
        {
            var mappedTrackInfo = _trackSelector.CurrentMappedTrackInfo;
            if (mappedTrackInfo == null)
            {
                Log.Debug(Tag, "Tracks []");
                return;
            }
            Log.Debug(Tag, "Tracks [");
            // Log tracks associated to renderers.
            for (var rendererIndex = 0; rendererIndex < mappedTrackInfo.Length; rendererIndex++)
            {
                var rendererTrackGroups = mappedTrackInfo.GetTrackGroups(rendererIndex);
                var trackSelection = trackSelections.Get(rendererIndex);
                if (rendererTrackGroups.Length > 0)
                {
                    Log.Debug(Tag, "  Renderer:" + rendererIndex + " [");
                    for (var groupIndex = 0; groupIndex < rendererTrackGroups.Length; groupIndex++)
                    {
                        var trackGroup = rendererTrackGroups.Get(groupIndex);
                        var adaptiveSupport = GetAdaptiveSupportString(trackGroup.Length,
                            mappedTrackInfo.GetAdaptiveSupport(rendererIndex, groupIndex, false));
                        Log.Debug(Tag, "    Group:" + groupIndex + ", adaptive_supported=" + adaptiveSupport + " [");
                        for (var trackIndex = 0; trackIndex < trackGroup.Length; trackIndex++)
                        {
                            var status = GetTrackStatusString(trackSelection, trackGroup, trackIndex);
                            var formatSupport = GetFormatSupportString(
                                mappedTrackInfo.GetTrackSupport(rendererIndex, groupIndex, trackIndex));

                            Log.Debug(Tag, "      " + status + " Track:" + trackIndex + ", "
                                + Format.ToLogString(trackGroup.GetFormat(trackIndex))
                                + ", supported=" + formatSupport);
                        }
                        Log.Debug(Tag, "    ]");
                    }
                    // Log metadata for at most one of the tracks selected for the renderer.
                    if (trackSelection != null)
                    {
                        for (var selectionIndex = 0; selectionIndex < trackSelection.Length(); selectionIndex++)
                        {
                            var metadata = trackSelection.GetFormat(selectionIndex).Metadata;
                            if (metadata != null)
                            {
                                Log.Debug(Tag, "    Metadata [");
                                PrintMetadata(metadata, "      ");
                                Log.Debug(Tag, "    ]");
                                break;
                            }
                        }
                    }
                    Log.Debug(Tag, "  ]");
                }
            }
            // Log tracks not associated with a renderer.
            var unassociatedTrackGroups = mappedTrackInfo.UnmappedTrackGroups;
            if (unassociatedTrackGroups.Length > 0)
            {
                Log.Debug(Tag, "  Renderer:None [");
                for (var groupIndex = 0; groupIndex < unassociatedTrackGroups.Length; groupIndex++)
                {
                    Log.Debug(Tag, "    Group:" + groupIndex + " [");
                    var trackGroup = unassociatedTrackGroups.Get(groupIndex);
                    for (var trackIndex = 0; trackIndex < trackGroup.Length; trackIndex++)
                    {
                        var status = GetTrackStatusString(false);
                        var formatSupport = GetFormatSupportString(
                            RendererCapabilities.FormatUnsupportedType);
                        Log.Debug(Tag, "      " + status + " Track:" + trackIndex + ", "
                            + Format.ToLogString(trackGroup.GetFormat(trackIndex))
                            + ", supported=" + formatSupport);
                    }
                    Log.Debug(Tag, "    ]");
                }
                Log.Debug(Tag, "  ]");
            }
            Log.Debug(Tag, "]");
        }
        #endregion

        #region MetadataRenderer.Output
        public void OnMetadata(MetadataObj metadata)
        {
            Log.Debug(Tag, "onMetadata [");
            PrintMetadata(metadata, "  ");
            Log.Debug(Tag, "]");
        }
        #endregion

        #region AudioRendererEventListener
        public void OnAudioEnabled(DecoderCounters counters)
        {
            Log.Debug(Tag, "audioEnabled [" + GetSessionTimeString() + "]");
        }

        public void OnAudioSessionId(int audioSessionId)
        {
            Log.Debug(Tag, "audioSessionId [" + audioSessionId + "]");
        }

        public void OnAudioSinkUnderrun(int p0, long p1, long p2)
        {
            throw new System.NotImplementedException();
        }

        public void OnAudioDecoderInitialized(string decoderName, long elapsedRealtimeMs,
            long initializationDurationMs)
        {
            Log.Debug(Tag, "audioDecoderInitialized [" + GetSessionTimeString() + ", " + decoderName + "]");
        }

        public void OnAudioInputFormatChanged(Format format)
        {
            Log.Debug(Tag, "audioFormatChanged [" + GetSessionTimeString() + ", " + Format.ToLogString(format)
                + "]");
        }

        public void OnAudioDisabled(DecoderCounters counters)
        {
            Log.Debug(Tag, "audioDisabled [" + GetSessionTimeString() + "]");
        }

        public void OnAudioTrackUnderrun(int bufferSize, long bufferSizeMs, long elapsedSinceLastFeedMs)
        {
            PrintInternalError("audioTrackUnderrun [" + bufferSize + ", " + bufferSizeMs + ", "
                + elapsedSinceLastFeedMs + "]", null);
        }
        #endregion

        #region VideoRendererEventListener
        public void OnVideoEnabled(DecoderCounters counters)
        {
            Log.Debug(Tag, "videoEnabled [" + GetSessionTimeString() + "]");
        }

        public void OnVideoDecoderInitialized(string decoderName, long elapsedRealtimeMs,
            long initializationDurationMs)
        {
            Log.Debug(Tag, "videoDecoderInitialized [" + GetSessionTimeString() + ", " + decoderName + "]");
        }

        public void OnVideoInputFormatChanged(Format format)
        {
            Log.Debug(Tag, "videoFormatChanged [" + GetSessionTimeString() + ", " + Format.ToLogString(format)
                + "]");
        }

        public void OnVideoDisabled(DecoderCounters counters)
        {
            Log.Debug(Tag, "videoDisabled [" + GetSessionTimeString() + "]");
        }

        public void OnDroppedFrames(int count, long elapsed)
        {
            Log.Debug(Tag, "droppedFrames [" + GetSessionTimeString() + ", " + count + "]");
        }

        public void OnVideoSizeChanged(int width, int height, int unappliedRotationDegrees,
            float pixelWidthHeightRatio)
        {
            Log.Debug(Tag, "videoSizeChanged [" + width + ", " + height + "]");
        }

        public void OnRenderedFirstFrame(Surface surface)
        {
            Log.Debug(Tag, "renderedFirstFrame [" + surface + "]");
        }
        #endregion

        #region DefaultDrmSessionManager.EventListener
        public void OnDrmSessionManagerError(Exception e)
        {
            PrintInternalError("drmSessionManagerError", e);
        }

        public void OnDrmKeysRestored()
        {
            Log.Debug(Tag, "drmKeysRestored [" + GetSessionTimeString() + "]");
        }

        public void OnDrmKeysRemoved()
        {
            Log.Debug(Tag, "drmKeysRemoved [" + GetSessionTimeString() + "]");
        }

        public void OnDrmKeysLoaded()
        {
            Log.Debug(Tag, "drmKeysLoaded [" + GetSessionTimeString() + "]");
        }
        #endregion

        #region Internal methods
        private void PrintInternalError(string type, Exception e)
        {
            Log.Error(Tag, "internalError [" + GetSessionTimeString() + ", " + type + "]", e);
        }

        private void PrintMetadata(MetadataObj metadata, string prefix)
        {
            for (var i = 0; i < metadata.Length(); i++)
            {
                var entry = metadata.Get(i);
                if (entry is TextInformationFrame)
                {
                    var textInformationFrame = (TextInformationFrame)entry;
                    Log.Debug(Tag, prefix + String.Format("%s: value=%s", textInformationFrame.Id,
                        textInformationFrame.Value));
                }
                else if (entry is UrlLinkFrame)
                {
                    var urlLinkFrame = (UrlLinkFrame)entry;
                    Log.Debug(Tag, prefix + String.Format("%s: url=%s", urlLinkFrame.Id, urlLinkFrame.Url));
                }
                else if (entry is PrivFrame)
                {
                    var privFrame = (PrivFrame)entry;
                    Log.Debug(Tag, prefix + String.Format("%s: owner=%s", PrivFrame.Id, privFrame.Owner));
                }
                else if (entry is GeobFrame)
                {
                    var geobFrame = (GeobFrame)entry;
                    Log.Debug(Tag, prefix + String.Format("%s: mimeType=%s, filename=%s, description=%s",
                        GeobFrame.Id, geobFrame.MimeType, geobFrame.Filename, geobFrame.Description));
                }
                else if (entry is ApicFrame)
                {
                    var apicFrame = (ApicFrame)entry;
                    Log.Debug(Tag, prefix + String.Format("%s: mimeType=%s, description=%s",
                        ApicFrame.Id, apicFrame.MimeType, apicFrame.Description));
                }
                else if (entry is CommentFrame)
                {
                    var commentFrame = (CommentFrame)entry;
                    Log.Debug(Tag, prefix + String.Format("%s: language=%s, description=%s", CommentFrame.Id,
                        commentFrame.Language, commentFrame.Description));
                }
                else if (entry is Id3Frame)
                {
                    var id3Frame = (Id3Frame)entry;
                    Log.Debug(Tag, prefix + String.Format("%s", id3Frame.Id));
                }
                else if (entry is EventMessage)
                {
                    var eventMessage = (EventMessage)entry;
                    Log.Debug(Tag, prefix + String.Format("EMSG: scheme=%s, id=%d, value=%s",
                        eventMessage.SchemeIdUri, eventMessage.Id, eventMessage.Value));
                }
            }
        }

        private string GetSessionTimeString()
        {
            return GetTimeString(SystemClock.ElapsedRealtime() - _startTimeMs);
        }

        private static string GetTimeString(long timeMs)
        {
            return timeMs == C.TimeUnset ? "?" : TimeFormat.Format((timeMs) / 1000f);
        }

        private static string GetStateString(int state)
        {
            switch (state)
            {
                case Player.StateBuffering:
                    return "B";
                case Player.StateEnded:
                    return "E";
                case Player.StateIdle:
                    return "I";
                case Player.StateReady:
                    return "R";
                default:
                    return "?";
            }
        }

        private static string GetFormatSupportString(int formatSupport)
        {
            switch (formatSupport)
            {
                case RendererCapabilities.FormatHandled:
                    return "YES";
                case RendererCapabilities.FormatExceedsCapabilities:
                    return "NO_EXCEEDS_CAPABILITIES";
                case RendererCapabilities.FormatUnsupportedSubtype:
                    return "NO_UNSUPPORTED_TYPE";
                case RendererCapabilities.FormatUnsupportedType:
                    return "NO";
                default:
                    return "?";
            }
        }

        private static string GetAdaptiveSupportString(int trackCount, int adaptiveSupport)
        {
            if (trackCount < 2)
            {
                return "N/A";
            }
            switch (adaptiveSupport)
            {
                case RendererCapabilities.AdaptiveSeamless:
                    return "YES";
                case RendererCapabilities.AdaptiveNotSeamless:
                    return "YES_NOT_SEAMLESS";
                case RendererCapabilities.AdaptiveNotSupported:
                    return "NO";
                default:
                    return "?";
            }
        }

        private static string GetTrackStatusString(ITrackSelection selection, TrackGroup group,
            int trackIndex)
        {
            return GetTrackStatusString(selection != null && selection.TrackGroup == group
                && selection.IndexOf(trackIndex) != C.IndexUnset);
        }

        private static string GetTrackStatusString(bool enabled)
        {
            return enabled ? "[X]" : "[ ]";
        }

        private static string GetRepeatModeString(int repeatMode)
        {
            switch (repeatMode)
            {
                case Player.RepeatModeOff:
                    return "OFF";
                case Player.RepeatModeOne:
                    return "ONE";
                case Player.RepeatModeAll:
                    return "ALL";
                default:
                    return "?";
            }
        }
        #endregion

        #region AdaptiveMediaSourceEventListener
        public void OnDownstreamFormatChanged(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId, MediaSourceEventListenerMediaLoadData mediaLoadData)
        {
            // Do nothing.
        }

        public void OnLoadCanceled(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId, MediaSourceEventListenerLoadEventInfo loadEventInfo, MediaSourceEventListenerMediaLoadData mediaLoadData)
        {
            // Do nothing.
        }

        public void OnLoadCompleted(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId, MediaSourceEventListenerLoadEventInfo loadEventInfo, MediaSourceEventListenerMediaLoadData mediaLoadData)
        {
            // Do nothing.
        }

        public void OnLoadError(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId, MediaSourceEventListenerLoadEventInfo loadEventInfo, MediaSourceEventListenerMediaLoadData mediaLoadData, IOException error, bool wasCanceled)
        {
            PrintInternalError("loadError", error);
        }

        public void OnLoadStarted(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId, MediaSourceEventListenerLoadEventInfo loadEventInfo, MediaSourceEventListenerMediaLoadData mediaLoadData)
        {
            // Do nothing.
        }

        public void OnMediaPeriodCreated(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId)
        {
            // Do nothing.
        }

        public void OnMediaPeriodReleased(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId)
        {
            // Do nothing.
        }

        public void OnReadingStarted(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId)
        {
            // Do nothing.
        }

        public void OnUpstreamDiscarded(int windowIndex, MediaSourceMediaPeriodId mediaPeriodId, MediaSourceEventListenerMediaLoadData mediaLoadData)
        {
            // Do nothing.
        }
        #endregion
    }
}
