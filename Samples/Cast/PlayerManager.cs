using System;
using System.Collections.Generic;
using Android.Content;
using Android.Gms.Cast;
using Android.Gms.Cast.Framework;
using Android.Views;
using Com.Google.Android.Exoplayer2.Ext.Cast;
using Com.Google.Android.Exoplayer2.Source;
using Com.Google.Android.Exoplayer2.Trackselection;
using Com.Google.Android.Exoplayer2.UI;
using Java.Lang;
using static Com.Google.Android.Exoplayer2.CastDemo.DemoUtil;
using static Com.Google.Android.Exoplayer2.Timeline;
using android = Android;
using Com.Google.Android.Exoplayer2.Upstream;
using Com.Google.Android.Exoplayer2.Source.Smoothstreaming;
using Com.Google.Android.Exoplayer2.Source.Dash;
using Com.Google.Android.Exoplayer2.Source.Hls;

namespace Com.Google.Android.Exoplayer2.CastDemo
{
    /**
    * Manages players and an internal media queue for the ExoPlayer/Cast demo app.
    */
    /* package */
    internal class PlayerManager : PlayerDefaultEventListener, CastPlayer.ISessionAvailabilityListener
    {

        /**
         * Listener for changes in the media queue playback position.
         */
        public interface IQueuePositionListener
        {

            /**
             * Called when the currently played item of the media queue changes.
             */
            void OnQueuePositionChanged(int previousIndex, int newIndex);

        }

        private static readonly string UserAgent = "ExoCastDemoPlayer";
        private static DefaultBandwidthMeter _bandwidthMeter = new DefaultBandwidthMeter();
        private static readonly DefaultHttpDataSourceFactory DataSourceFactory = new DefaultHttpDataSourceFactory(UserAgent, _bandwidthMeter);

        private readonly PlayerView _localPlayerView;
        private readonly PlayerControlView _castControlView;
        private SimpleExoPlayer _exoPlayer;
        private CastPlayer _castPlayer;
        private List<Sample> _mediaQueue;
        private readonly IQueuePositionListener _queuePositionListener;
        private ConcatenatingMediaSource _concatenatingMediaSource;

        private bool _castMediaQueueCreationPending;
        private int _currentItemIndex;
        private IPlayer _currentPlayer;

        /**
         * @param queuePositionListener A {@link QueuePositionListener} for queue position changes.
         * @param localPlayerView The {@link PlayerView} for local playback.
         * @param castControlView The {@link PlayerControlView} to control remote playback.
         * @param context A {@link Context}.
         * @param castContext The {@link CastContext}.
         */
        public static PlayerManager CreatePlayerManager(IQueuePositionListener queuePositionListener, PlayerView localPlayerView, PlayerControlView castControlView, Context context, CastContext castContext)
        {
            PlayerManager playerManager = new PlayerManager(queuePositionListener, localPlayerView, castControlView, context, castContext);
            playerManager.Init();
            return playerManager;
        }

        private PlayerManager(IQueuePositionListener queuePositionListener, PlayerView localPlayerView, PlayerControlView castControlView, Context context, CastContext castContext)
        {
            _queuePositionListener = queuePositionListener;
            _localPlayerView = localPlayerView;
            _castControlView = castControlView;
            _mediaQueue = new List<Sample>();
            _currentItemIndex = C.IndexUnset;
            _concatenatingMediaSource = new ConcatenatingMediaSource();

            DefaultTrackSelector trackSelector = new DefaultTrackSelector(_bandwidthMeter);
            IRenderersFactory renderersFactory = new DefaultRenderersFactory(context);
            _exoPlayer = ExoPlayerFactory.NewSimpleInstance(renderersFactory, trackSelector);
            _exoPlayer.AddListener(this);
            localPlayerView.Player = _exoPlayer;

            _castPlayer = new CastPlayer(castContext);
            _castPlayer.AddListener(this);
            _castPlayer.SetSessionAvailabilityListener(this);
            castControlView.Player = _castPlayer;
        }

        // Queue manipulation methods.

        /**
         * Plays a specified queue item in the current player.
         *
         * @param itemIndex The index of the item to play.
         */
        public void SelectQueueItem(int itemIndex)
        {
            SetCurrentItem(itemIndex, C.TimeUnset, true);
        }

        /**
         * Returns the index of the currently played item.
         */
        public int GetCurrentItemIndex()
        {
            return _currentItemIndex;
        }

        /**
         * Appends {@code sample} to the media queue.
         *
         * @param sample The {@link Sample} to append.
         */
        public void AddItem(Sample sample)
        {
            _mediaQueue.Add(sample);
            _concatenatingMediaSource.AddMediaSource(BuildMediaSource(sample));
            if (_currentPlayer == _castPlayer)
            {
                _castPlayer.AddItems(BuildMediaQueueItem(sample));
            }
        }

        /**
         * Returns the size of the media queue.
         */
        public int GetMediaQueueSize()
        {
            return _mediaQueue.Count;
        }

        /**
         * Returns the item at the given index in the media queue.
         *
         * @param position The index of the item.
         * @return The item at the given index in the media queue.
         */
        public Sample GetItem(int position)
        {
            return _mediaQueue[position];
        }

        /**
         * Removes the item at the given index from the media queue.
         *
         * @param itemIndex The index of the item to remove.
         * @return Whether the removal was successful.
         */
        public bool RemoveItem(int itemIndex)
        {
            _concatenatingMediaSource.RemoveMediaSource(itemIndex);
            if (_currentPlayer == _castPlayer)
            {
                if (_castPlayer.PlaybackState != Player.StateIdle)
                {
                    Timeline castTimeline = _castPlayer.CurrentTimeline;
                    if (castTimeline.PeriodCount <= itemIndex)
                    {
                        return false;
                    }
                    _castPlayer.RemoveItem((int)castTimeline.GetPeriod(itemIndex, new Period()).Id);
                }
            }
            _mediaQueue.Remove(_mediaQueue[itemIndex]);
            if (itemIndex == _currentItemIndex && itemIndex == _mediaQueue.Count)
            {
                MaybeSetCurrentItemAndNotify(C.IndexUnset);
            }
            else if (itemIndex < _currentItemIndex)
            {
                MaybeSetCurrentItemAndNotify(_currentItemIndex - 1);
            }
            return true;
        }

        /**
         * Moves an item within the queue.
         *
         * @param fromIndex The index of the item to move.
         * @param toIndex The target index of the item in the queue.
         * @return Whether the item move was successful.
         */
        public bool MoveItem(int fromIndex, int toIndex)
        {
            // Player update.
            _concatenatingMediaSource.MoveMediaSource(fromIndex, toIndex);
            if (_currentPlayer == _castPlayer && _castPlayer.PlaybackState != Player.StateIdle)
            {
                Timeline castTimeline = _castPlayer.CurrentTimeline;
                int periodCount = castTimeline.PeriodCount;
                if (periodCount <= fromIndex || periodCount <= toIndex)
                {
                    return false;
                }
                int elementId = (int)castTimeline.GetPeriod(fromIndex, new Period()).Id;
                _castPlayer.MoveItem(elementId, toIndex);
            }

            _mediaQueue.Insert(toIndex, _mediaQueue[fromIndex]);
            _mediaQueue.Remove(_mediaQueue[fromIndex]);

            // Index update.
            if (fromIndex == _currentItemIndex)
            {
                MaybeSetCurrentItemAndNotify(toIndex);
            }
            else if (fromIndex < _currentItemIndex && toIndex >= _currentItemIndex)
            {
                MaybeSetCurrentItemAndNotify(_currentItemIndex - 1);
            }
            else if (fromIndex > _currentItemIndex && toIndex <= _currentItemIndex)
            {
                MaybeSetCurrentItemAndNotify(_currentItemIndex + 1);
            }

            return true;
        }

        // Miscellaneous methods.

        /**
         * Dispatches a given {@link KeyEvent} to the corresponding view of the current player.
         *
         * @param event The {@link KeyEvent}.
         * @return Whether the event was handled by the target view.
         */
        public bool DispatchKeyEvent(KeyEvent @event)
        {
            if (_currentPlayer == _exoPlayer)
            {
                return _localPlayerView.DispatchKeyEvent(@event);
            }
            else /* currentPlayer == castPlayer */
            {
                return _castControlView.DispatchKeyEvent(@event);
            }
        }

        /**
         * Releases the manager and the players that it holds.
         */
        public void Release()
        {
            _currentItemIndex = C.IndexUnset;
            _mediaQueue.Clear();
            _concatenatingMediaSource.Clear();
            _castPlayer.SetSessionAvailabilityListener(null);
            _castPlayer.Release();
            _localPlayerView.Player = null;
            _exoPlayer.Release();
        }

        // Player.EventListener implementation.

        public override void OnPlayerStateChanged(bool playWhenReady, int playbackState)
        {
            UpdateCurrentItemIndex();
        }

        public override void OnPositionDiscontinuity(int reason)
        {
            UpdateCurrentItemIndex();
        }

        public void OnTimelineChanged(
                Timeline timeline, object manifest, int reason)
        {
            UpdateCurrentItemIndex();
            if (timeline.IsEmpty)
            {
                _castMediaQueueCreationPending = true;
            }
        }

        // CastPlayer.SessionAvailabilityListener implementation.

        public void OnCastSessionAvailable()
        {
            SetCurrentPlayer(_castPlayer);
        }

        public void OnCastSessionUnavailable()
        {
            SetCurrentPlayer(_exoPlayer);
        }

        // Internal methods.

        private void Init()
        {
            SetCurrentPlayer((_castPlayer.IsCastSessionAvailable ? (IPlayer)_castPlayer : _exoPlayer));
        }

        private void UpdateCurrentItemIndex()
        {
            int playbackState = _currentPlayer.PlaybackState;
            MaybeSetCurrentItemAndNotify(
                    playbackState != Player.StateIdle && playbackState != Player.StateEnded
                            ? _currentPlayer.CurrentWindowIndex : C.IndexUnset);
        }

        private void SetCurrentPlayer(IPlayer currentPlayer)
        {
            if (_currentPlayer == currentPlayer)
            {
                return;
            }

            // View management.
            if (currentPlayer == _exoPlayer)
            {
                _localPlayerView.Visibility = ViewStates.Visible;
                _castControlView.Hide();
            }
            else /* currentPlayer == castPlayer */
            {
                _localPlayerView.Visibility = ViewStates.Gone;
                _castControlView.Show();
            }

            // Player state management.
            long playbackPositionMs = C.TimeUnset;
            int windowIndex = C.IndexUnset;
            bool playWhenReady = false;
            if (_currentPlayer != null)
            {
                int playbackState = _currentPlayer.PlaybackState;
                if (playbackState != Player.StateEnded)
                {
                    playbackPositionMs = _currentPlayer.CurrentPosition;
                    playWhenReady = _currentPlayer.PlayWhenReady;
                    windowIndex = _currentPlayer.CurrentWindowIndex;
                    if (windowIndex != _currentItemIndex)
                    {
                        playbackPositionMs = C.TimeUnset;
                        windowIndex = _currentItemIndex;
                    }
                }
                _currentPlayer.Stop(true);
            }
            else
            {
                // This is the initial setup. No need to save any state.
            }

            _currentPlayer = currentPlayer;

            // Media queue management.
            _castMediaQueueCreationPending = currentPlayer == _castPlayer;
            if (currentPlayer == _exoPlayer)
            {
                _exoPlayer.Prepare(_concatenatingMediaSource);
            }

            // Playback transition.
            if (windowIndex != C.IndexUnset)
            {
                SetCurrentItem(windowIndex, playbackPositionMs, playWhenReady);
            }
        }

        /**
         * Starts playback of the item at the given position.
         *
         * @param itemIndex The index of the item to play.
         * @param positionMs The position at which playback should start.
         * @param playWhenReady Whether the player should proceed when ready to do so.
         */
        private void SetCurrentItem(int itemIndex, long positionMs, bool playWhenReady)
        {
            MaybeSetCurrentItemAndNotify(itemIndex);
            if (_castMediaQueueCreationPending)
            {
                MediaQueueItem[] items = new MediaQueueItem[_mediaQueue.Count];
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = BuildMediaQueueItem(_mediaQueue[i]);
                }
                _castMediaQueueCreationPending = false;
                _castPlayer.LoadItems(items, itemIndex, positionMs, Player.RepeatModeOff);
            }
            else
            {
                _currentPlayer.SeekTo(itemIndex, positionMs);
                _currentPlayer.PlayWhenReady = playWhenReady;
            }
        }

        private void MaybeSetCurrentItemAndNotify(int currentItemIndex)
        {
            if (_currentItemIndex != currentItemIndex)
            {
                int oldIndex = _currentItemIndex;
                _currentItemIndex = currentItemIndex;
                _queuePositionListener.OnQueuePositionChanged(oldIndex, currentItemIndex);
            }
        }

        private static IMediaSource BuildMediaSource(Sample sample)
        {
            android.Net.Uri uri = android.Net.Uri.Parse(sample.Uri);
            switch (sample.MimeType)
            {
                case MimeTypeSs:
                    return new SsMediaSource.Factory(new DefaultSsChunkSource.Factory(DataSourceFactory), DataSourceFactory).CreateMediaSource(uri);
                case MimeTypeDash:
                    return new DashMediaSource.Factory(new DefaultDashChunkSource.Factory(DataSourceFactory), DataSourceFactory).CreateMediaSource(uri);
                case MimeTypeHls:
                    return new HlsMediaSource.Factory(DataSourceFactory).CreateMediaSource(uri);
                case MimeTypeVideoMp4:
                    return new ExtractorMediaSource.Factory(DataSourceFactory).CreateMediaSource(uri);
                case MimeTypeAudio:
                    return new ExtractorMediaSource.Factory(DataSourceFactory).CreateMediaSource(uri);
                default:
                    {
                        throw new IllegalStateException("Unsupported type: " + sample.MimeType);
                    }
            }
        }

        private static MediaQueueItem BuildMediaQueueItem(Sample sample)
        {
            MediaMetadata movieMetadata = new MediaMetadata(MediaMetadata.MediaTypeMovie);
            movieMetadata.PutString(MediaMetadata.KeyTitle, sample.Name);
            MediaInfo mediaInfo = new MediaInfo.Builder(sample.Uri)
                    .SetStreamType(MediaInfo.StreamTypeBuffered).SetContentType(sample.MimeType)
                    .SetMetadata(movieMetadata).Build();
            return new MediaQueueItem.Builder(mediaInfo).Build();
        }

    }

}
