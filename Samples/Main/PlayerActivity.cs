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

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Google.Android.Exoplayer2.Drm;
using Com.Google.Android.Exoplayer2.Mediacodec;
using Com.Google.Android.Exoplayer2.Offline;
using Com.Google.Android.Exoplayer2.Source;
using Com.Google.Android.Exoplayer2.Source.Ads;
using Com.Google.Android.Exoplayer2.Source.Dash;
using Com.Google.Android.Exoplayer2.Source.Dash.Manifest;
using Com.Google.Android.Exoplayer2.Source.Hls;
using Com.Google.Android.Exoplayer2.Source.Hls.Playlist;
using Com.Google.Android.Exoplayer2.Source.Smoothstreaming;
using Com.Google.Android.Exoplayer2.Source.Smoothstreaming.Manifest;
using Com.Google.Android.Exoplayer2.Trackselection;
using Com.Google.Android.Exoplayer2.UI;
using Com.Google.Android.Exoplayer2.Upstream;
using Com.Google.Android.Exoplayer2.Util;
using Java.Lang;
using Java.Net;
using Java.Util;
using System;
using System.Collections.Generic;
using android = Android;
using Utils = Com.Google.Android.Exoplayer2.Util.Util;

namespace Com.Google.Android.Exoplayer2.Demo
{
    /** An activity that plays media using {@link SimpleExoPlayer}. */
    public class PlayerActivity : Activity, View.IOnClickListener, IPlaybackPreparer, PlayerControlView.IVisibilityListener
    {
        public static string DrmSchemeExtra = "drm_scheme";
        public static string DrmLicenseUrlExtra = "drm_license_url";
        public static string DrmKeyRequestPropertiesExtra = "drm_key_request_properties";
        public static string DrmMultiSessionExtra = "drm_multi_session";
        public static string PreferExtensionDecodersExtra = "prefer_extension_decoders";

        public static string ActionView = "com.google.android.exoplayer.demo.action.VIEW";
        public static string ExtensionExtra = "extension";

        public static string ActionViewList =
            "com.google.android.exoplayer.demo.action.VIEW_LIST";
        public static string UriListExtra = "uri_list";
        public static string ExtensionListExtra = "extension_list";

        public static string AdTagUriExtra = "ad_tag_uri";

        public static string AbrAlgorithmExtra = "abr_algorithm";
        private static readonly string AbrAlgorithmDefault = "default";
        private static readonly string AbrAlgorithmRandom = "random";

        // For backwards compatibility only.
        private static readonly string DrmSchemeUuidExtra = "drm_scheme_uuid";

        // Saved instance state keys.
        private const string KeyTrackSelectorParameters = "track_selector_parameters";
        private const string KeyWindow = "window";
        private const string KeyPosition = "position";
        private static readonly string KeyAutoPlay = "auto_play";

        private static readonly DefaultBandwidthMeter BandwidthMeter = new DefaultBandwidthMeter();
        private static readonly CookieManager DefaultCookieManager = new CookieManager();

        private EventLogger _eventLogger;
        private Handler _mainHandler;

        private PlayerView _playerView;
        private LinearLayout _debugRootView;
        //private TextView _debugTextView;

        private IDataSourceFactory _mediaDataSourceFactory;
        private SimpleExoPlayer _player;
        private FrameworkMediaDrm _mediaDrm;
        private IMediaSource _mediaSource;
        private DefaultTrackSelector _trackSelector;
        private DefaultTrackSelector.Parameters _trackSelectorParameters;
        //private DebugTextViewHelper _debugViewHelper;
        private TrackGroupArray _lastSeenTrackGroupArray;

        private bool _startAutoPlay;
        private int _startWindow;
        private long _startPosition;

        // Fields used only for ad playback. The ads loader is loaded via reflection.

        private IAdsLoader _adsLoader;
        private android.Net.Uri _loadedAdTagUri;
        private ViewGroup _adUiViewGroup;

        public PlayerActivity()
        {
            DefaultCookieManager.SetCookiePolicy(CookiePolicy.AcceptOriginalServer);
        }

        // Activity lifecycle


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _mainHandler = new Handler();

            _mediaDataSourceFactory = BuildDataSourceFactory(true);
            if (CookieHandler.Default != DefaultCookieManager)
            {
                CookieHandler.Default = DefaultCookieManager;
            }

            SetContentView(Resource.Layout.player_activity);
            var rootView = FindViewById(Resource.Id.root);
            rootView.SetOnClickListener(this);
            _debugRootView = (LinearLayout)FindViewById(Resource.Id.controls_root);
            //_debugTextView = (TextView)FindViewById(Resource.Id.debug_text_view);

            _playerView = (PlayerView)FindViewById(Resource.Id.player_view);
            _playerView.SetControllerVisibilityListener(this);
            _playerView.SetErrorMessageProvider(new PlayerErrorMessageProvider(this));
            _playerView.RequestFocus();

            if (savedInstanceState != null)
            {
                _trackSelectorParameters = (DefaultTrackSelector.Parameters)savedInstanceState.GetParcelable(KeyTrackSelectorParameters);
                _startAutoPlay = savedInstanceState.GetBoolean(KeyAutoPlay);
                _startWindow = savedInstanceState.GetInt(KeyWindow);
                _startPosition = savedInstanceState.GetLong(KeyPosition);
            }
            else
            {
                _trackSelectorParameters = new DefaultTrackSelector.ParametersBuilder().Build();
                ClearStartPosition();
            }
        }

        protected override void OnNewIntent(Intent intent)
        {
            ReleasePlayer();
            ClearStartPosition();
            Intent = intent;
        }

        protected override void OnStart()
        {
            base.OnStart();
            if (Utils.SdkInt > 23)
            {
                InitializePlayer();
            }
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (Utils.SdkInt <= 23 || _player == null)
            {
                InitializePlayer();
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
            if (Utils.SdkInt <= 23)
            {
                ReleasePlayer();
            }
        }

        protected override void OnStop()
        {
            base.OnStop();
            if (Utils.SdkInt > 23)
            {
                ReleasePlayer();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseAdsLoader();
        }

        // ReSharper disable once UnusedMember.Global
        public void OnRequestPermissionsResult(int requestCode, string[] permissions, int[] grantResults)
        {
            if (grantResults.Length == 0)
            {
                // Empty results are triggered if a permission is requested while another request was already
                // pending and can be safely ignored in this case.
                return;
            }
            if (grantResults[0] == (int)Permission.Granted)
            {
                InitializePlayer();
            }
            else
            {
                ShowToast(Resource.String.storage_permission_denied);
                Finish();
            }
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            UpdateTrackSelectorParameters();
            UpdateStartPosition();
            outState.PutParcelable(KeyTrackSelectorParameters, _trackSelectorParameters);
            outState.PutBoolean(KeyAutoPlay, _startAutoPlay);
            outState.PutInt(KeyWindow, _startWindow);
            outState.PutLong(KeyPosition, _startPosition);
        }
        // Activity input

        public override bool DispatchKeyEvent(KeyEvent @event)
        {
            // See whether the player view wants to handle media or DPAD keys events.
            return _playerView.DispatchKeyEvent(@event) || base.DispatchKeyEvent(@event);
        }

        // OnClickListener methods

        public void OnClick(View view)
        {
            if (view.Parent != _debugRootView) return;
            var mappedTrackInfo = _trackSelector.CurrentMappedTrackInfo;
            if (mappedTrackInfo == null) return;
            var title = ((Button)view).Text;
            var rendererIndex = (int)view.GetTag(view.Id);
            var rendererType = mappedTrackInfo.GetRendererType(rendererIndex);
            var allowAdaptiveSelections =
                rendererType == C.TrackTypeVideo
                || (rendererType == C.TrackTypeAudio
                    && mappedTrackInfo.GetTypeSupport(C.TrackTypeVideo)
                    == MappingTrackSelector.MappedTrackInfo.RendererSupportNoTracks);
            var dialogPair = TrackSelectionView.GetDialog(this, title, _trackSelector, rendererIndex);

            ((TrackSelectionView)dialogPair.Second).SetShowDisableOption(true);
            ((TrackSelectionView)dialogPair.Second).SetAllowAdaptiveSelections(allowAdaptiveSelections);
            ((AlertDialog)dialogPair.First).Show();
        }

        // PlaybackControlView.PlaybackPreparer implementation
        public void PreparePlayback()
        {
            InitializePlayer();
        }

        // PlaybackControlView.VisibilityListener implementation
        public void OnVisibilityChange(int visibility)
        {
            _debugRootView.Visibility = (ViewStates)visibility;
        }

        // Internal methods

        private void InitializePlayer()
        {
            if (_player == null)
            {
                var intent = Intent;
                var action = intent.Action;
                android.Net.Uri[] uris;
                string[] extensions;

                if (ActionView.Equals(action))
                {
                    uris = new[] { intent.Data };
                    extensions = new[] { intent.GetStringExtra(ExtensionExtra) };
                }
                else if (ActionViewList.Equals(action))
                {
                    var uristrings = intent.GetStringArrayExtra(UriListExtra);
                    uris = new android.Net.Uri[uristrings.Length];
                    for (var i = 0; i < uristrings.Length; i++)
                    {
                        uris[i] = android.Net.Uri.Parse(uristrings[i]);
                    }
                    extensions = intent.GetStringArrayExtra(ExtensionListExtra) ?? new string[uristrings.Length];
                }
                else
                {
                    ShowToast(GetString(Resource.String.unexpected_intent_action, action));
                    Finish();
                    return;
                }
                if (Utils.MaybeRequestReadExternalStoragePermission(this, uris))
                {
                    // The player will be reinitialized if the permission is granted.
                    return;
                }

                DefaultDrmSessionManager drmSessionManager = null;
                if (intent.HasExtra(DrmSchemeExtra) || intent.HasExtra(DrmSchemeUuidExtra))
                {
                    var drmLicenseUrl = intent.GetStringExtra(DrmLicenseUrlExtra);
                    var keyRequestPropertiesArray =
                        intent.GetStringArrayExtra(DrmKeyRequestPropertiesExtra);
                    var multiSession = intent.GetBooleanExtra(DrmMultiSessionExtra, false);
                    var errorstringId = Resource.String.error_drm_unknown;
                    if (Utils.SdkInt < 18)
                        errorstringId = Resource.String.error_drm_not_supported;
                    else
                    {
                        try
                        {
                            var drmSchemeExtra = intent.HasExtra(DrmSchemeExtra) ? DrmSchemeExtra
                                : DrmSchemeUuidExtra;
                            var drmSchemeUuid = Utils.GetDrmUuid(intent.GetStringExtra(drmSchemeExtra));
                            if (drmSchemeUuid == null)
                            {
                                errorstringId = Resource.String.error_drm_unsupported_scheme;
                            }
                            else
                            {
                                drmSessionManager =
                                    BuildDrmSessionManagerV18(
                                        drmSchemeUuid, drmLicenseUrl, keyRequestPropertiesArray, multiSession);
                            }
                        }
                        catch (UnsupportedDrmException e)
                        {
                            errorstringId = e.Reason == UnsupportedDrmException.ReasonUnsupportedScheme
                                ? Resource.String.error_drm_unsupported_scheme : Resource.String.error_drm_unknown;
                        }
                    }
                    if (drmSessionManager == null)
                    {
                        ShowToast(errorstringId);
                        Finish();
                        return;
                    }
                }

                ITrackSelectionFactory trackSelectionFactory;
                var abrAlgorithm = intent.GetStringExtra(AbrAlgorithmExtra);
                if (abrAlgorithm == null || AbrAlgorithmDefault.Equals(abrAlgorithm))
                {
                    trackSelectionFactory = new AdaptiveTrackSelection.Factory(BandwidthMeter);
                }
                else if (AbrAlgorithmRandom.Equals(abrAlgorithm))
                {
                    trackSelectionFactory = new RandomTrackSelection.Factory();
                }
                else
                {
                    ShowToast(Resource.String.error_unrecognized_abr_algorithm);
                    Finish();
                    return;
                }

                var preferExtensionDecoders =
                    intent.GetBooleanExtra(PreferExtensionDecodersExtra, false);
                var extensionRendererMode =
                    ((DemoApplication)Application).UseExtensionRenderers()
                        ? (preferExtensionDecoders ? DefaultRenderersFactory.ExtensionRendererModePrefer
                        : DefaultRenderersFactory.ExtensionRendererModeOn)
                        : DefaultRenderersFactory.ExtensionRendererModeOff;
                var renderersFactory =
                    new DefaultRenderersFactory(this, extensionRendererMode);

                _trackSelector = new DefaultTrackSelector(trackSelectionFactory);
                _trackSelector.SetParameters(_trackSelectorParameters);
                _lastSeenTrackGroupArray = null;

                _player = ExoPlayerFactory.NewSimpleInstance(renderersFactory, _trackSelector, drmSessionManager);

                _eventLogger = new EventLogger(_trackSelector);

                _player.AddListener(new PlayerEventListener(this));
                _player.PlayWhenReady = _startAutoPlay;

                _player.AddListener(_eventLogger);

                // Cannot implement the AnalyticsListener because the binding doesn't work.

                //Todo: implement IAnalyticsListener
                //player.AddAnalyticsListener(eventLogger);

                //_player.AddAudioDebugListener(_eventLogger);
                //_player.AddVideoDebugListener(_eventLogger);

                _player.AddMetadataOutput(_eventLogger);
                //end Todo

                _playerView.Player = _player;
                _playerView.SetPlaybackPreparer(this);
                //_debugViewHelper = new DebugTextViewHelper(_player, _debugTextView);
                //_debugViewHelper.Start();

                var mediaSources = new IMediaSource[uris.Length];
                for (var i = 0; i < uris.Length; i++)
                {
                    mediaSources[i] = BuildMediaSource(uris[i], extensions[i]);
                }
                _mediaSource =
                    mediaSources.Length == 1 ? mediaSources[0] : new ConcatenatingMediaSource(mediaSources);
                var adTagUristring = intent.GetStringExtra(AdTagUriExtra);
                if (adTagUristring != null)
                {
                    var adTagUri = android.Net.Uri.Parse(adTagUristring);
                    if (!adTagUri.Equals(_loadedAdTagUri))
                    {
                        ReleaseAdsLoader();
                        _loadedAdTagUri = adTagUri;
                    }
                    var adsMediaSource = CreateAdsMediaSource(_mediaSource, android.Net.Uri.Parse(adTagUristring));
                    if (adsMediaSource != null)
                    {
                        _mediaSource = adsMediaSource;
                    }
                    else
                    {
                        ShowToast(Resource.String.ima_not_loaded);
                    }
                }
                else
                {
                    ReleaseAdsLoader();
                }
            }
            var haveStartPosition = _startWindow != C.IndexUnset;
            if (haveStartPosition)
            {
                _player.SeekTo(_startWindow, _startPosition);
            }
            _player.Prepare(_mediaSource, !haveStartPosition, false);
            UpdateButtonVisibilities();
        }

        private IMediaSource BuildMediaSource(android.Net.Uri uri, string overrideExtension = null)
        {
            var type = Utils.InferContentType(uri, overrideExtension);

            IMediaSource src;

            switch (type)
            {
                case C.TypeDash:
                    src = new DashMediaSource.Factory(new DefaultDashChunkSource.Factory(_mediaDataSourceFactory), BuildDataSourceFactory(false))
                        .SetManifestParser(new FilteringManifestParser(new DashManifestParser(), GetOfflineStreamKeys(uri)))
                        .CreateMediaSource(uri);
                    break;
                case C.TypeSs:
                    src = new SsMediaSource.Factory(new DefaultSsChunkSource.Factory(_mediaDataSourceFactory), BuildDataSourceFactory(false))
                        .SetManifestParser(new FilteringManifestParser(new SsManifestParser(), GetOfflineStreamKeys(uri)))
                        .CreateMediaSource(uri);
                    break;
                case C.TypeHls:
                    src = new HlsMediaSource.Factory(_mediaDataSourceFactory)
                        .SetPlaylistParser(new FilteringManifestParser(new HlsPlaylistParser(), GetOfflineStreamKeys(uri)))
                        .CreateMediaSource(uri);
                    break;
                case C.TypeOther:
                    src = new ExtractorMediaSource.Factory(_mediaDataSourceFactory).CreateMediaSource(uri);
                    break;
                default:
                    throw new IllegalStateException("Unsupported type: " + type);
            }

            //Todo: implement IAnalyticsListener
            src.AddEventListener(_mainHandler, _eventLogger);
            return src;
        }

        private List<object> GetOfflineStreamKeys(android.Net.Uri uri)
        {
            return ((DemoApplication)Application).GetDownloadTracker().GetOfflineStreamKeys(uri);
        }

        private DefaultDrmSessionManager BuildDrmSessionManagerV18(UUID uuid, string licenseUrl, IReadOnlyList<string> keyRequestPropertiesArray, bool multiSession)
        {
            var licenseDataSourceFactory = ((DemoApplication)Application).BuildHttpDataSourceFactory(/* listener= */ null);
            var drmCallback =
            new HttpMediaDrmCallback(licenseUrl, licenseDataSourceFactory);
            if (keyRequestPropertiesArray != null)
            {
                for (var i = 0; i < keyRequestPropertiesArray.Count - 1; i += 2)
                {
                    drmCallback.SetKeyRequestProperty(keyRequestPropertiesArray[i],
                        keyRequestPropertiesArray[i + 1]);
                }
            }
            ReleaseMediaDrm();
            _mediaDrm = FrameworkMediaDrm.NewInstance(uuid);
            return new DefaultDrmSessionManager(uuid, _mediaDrm, drmCallback, null, multiSession);

            //Todo: implement IAnalyticsListener
            //return new DefaultDrmSessionManager(uuid, FrameworkMediaDrm.NewInstance(uuid), drmCallback,
            //    null, _mainHandler, _eventLogger);
        }

        private void ReleasePlayer()
        {
            if (_player != null)
            {
                UpdateTrackSelectorParameters();
                UpdateStartPosition();
                //_debugViewHelper.Stop();
                //_debugViewHelper = null;
                _player.Release();
                _player = null;
                _mediaSource = null;
                _trackSelector = null;

                //Todo: implement IAnalyticsListener
                _eventLogger = null;
            }
            ReleaseMediaDrm();
        }

        private void ReleaseMediaDrm()
        {
            if (_mediaDrm == null) return;
            _mediaDrm.Release();
            _mediaDrm = null;
        }

        private void ReleaseAdsLoader()
        {
            if (_adsLoader == null) return;
            _adsLoader.Release();
            _adsLoader = null;
            _loadedAdTagUri = null;
            _playerView.OverlayFrameLayout.RemoveAllViews();
        }

        private void UpdateTrackSelectorParameters()
        {
            if (_trackSelector != null)
            {
                _trackSelectorParameters = _trackSelector.GetParameters();
            }
        }

        private void UpdateStartPosition()
        {
            if (_player == null) return;
            _startAutoPlay = _player.PlayWhenReady;
            _startWindow = _player.CurrentWindowIndex;
            _startPosition = Java.Lang.Math.Max(0, _player.ContentPosition);
        }

        private void ClearStartPosition()
        {
            _startAutoPlay = true;
            _startWindow = C.IndexUnset;
            _startPosition = C.TimeUnset;
        }

        /**
         * Returns a new DataSource factory.
         *
         * @param useBandwidthMeter Whether to set {@link #BANDWIDTH_METER} as a listener to the new
         *     DataSource factory.
         * @return A new DataSource factory.
         */
        private IDataSourceFactory BuildDataSourceFactory(bool useBandwidthMeter)
        {
            return ((DemoApplication)Application)
                .BuildDataSourceFactory(useBandwidthMeter ? BandwidthMeter : null);
        }

        private IMediaSource CreateAdsMediaSource(IMediaSource mediaSource, android.Net.Uri adTagUri)
        {
            // Load the extension source using reflection so the demo app doesn't have to depend on it.
            // The ads loader is reused for multiple playbacks, so that ad playback can resume.
            try
            {
                var loaderClass = Class.ForName("com.google.android.exoplayer2.ext.ima.ImaAdsLoader");
                if (_adsLoader == null)
                {
                    var loaderConstructor = loaderClass.AsSubclass(Class.FromType(typeof(IAdsLoader))).GetConstructor(Class.FromType(typeof(Context)), Class.FromType(typeof(android.Net.Uri)));

                    _adsLoader = (IAdsLoader)loaderConstructor.NewInstance(this, adTagUri);
                    _adUiViewGroup = new FrameLayout(this);
                    // The demo app has a non-null overlay frame layout.
                    _playerView.OverlayFrameLayout.AddView(_adUiViewGroup);
                }
                var adMediaSourceFactory = new AdMediaSourceFactory(this);

                return new AdsMediaSource(mediaSource, adMediaSourceFactory, _adsLoader, _adUiViewGroup);
            }
            catch (ClassNotFoundException)
            {
                // IMA extension not loaded.
                return null;
            }
            catch (Java.Lang.Exception e)
            {
                throw new RuntimeException(e);
            }
        }

        private class AdMediaSourceFactory : Java.Lang.Object, AdsMediaSource.IMediaSourceFactory
        {
            readonly PlayerActivity _activity;

            public AdMediaSourceFactory(PlayerActivity activity)
            {
                _activity = activity;
            }

            public IMediaSource CreateMediaSource(android.Net.Uri uri)
            {
                return _activity.BuildMediaSource(uri);
            }

            public int[] GetSupportedTypes()
            {
                return new[] { C.TypeDash, C.TypeSs, C.TypeHls, C.TypeOther };
            }
        }

        // User controls
        private void UpdateButtonVisibilities()
        {
            _debugRootView.RemoveAllViews();
            if (_player == null)
            {
                return;
            }

            var mappedTrackInfo = _trackSelector.CurrentMappedTrackInfo;
            if (mappedTrackInfo == null)
            {
                return;
            }

            for (var i = 0; i < mappedTrackInfo.RendererCount; i++)
            {
                var trackGroups = mappedTrackInfo.GetTrackGroups(i);
                if (trackGroups.Length == 0) continue;
                var button = new Button(this);
                int label;
                switch (_player.GetRendererType(i))
                {
                    case C.TrackTypeAudio:
                        label = Resource.String.exo_track_selection_title_audio;
                        break;
                    case C.TrackTypeVideo:
                        label = Resource.String.exo_track_selection_title_video;
                        break;
                    case C.TrackTypeText:
                        label = Resource.String.exo_track_selection_title_text;
                        break;
                    default:
                        continue;
                }
                button.SetText(label);
                button.SetTag(button.Id, i);
                button.SetOnClickListener(this);
                _debugRootView.AddView(button);
            }
        }

        private void ShowControls()
        {
            _debugRootView.Visibility = ViewStates.Visible;
        }

        private void ShowToast(int messageId)
        {
            ShowToast(GetString(messageId));
        }

        private void ShowToast(string message)
        {
            Toast.MakeText(ApplicationContext, message, ToastLength.Long).Show();
        }

        private static bool IsBehindLiveWindow(ExoPlaybackException e)
        {
            if (e.Type != ExoPlaybackException.TypeSource)
            {
                return false;
            }
            Throwable cause = e.SourceException;
            while (cause != null)
            {
                if (cause is BehindLiveWindowException)
                {
                    return true;
                }
                cause = cause.Cause;
            }
            return false;
        }

        private class PlayerEventListener : PlayerDefaultEventListener
        {
            readonly PlayerActivity _activity;

            public PlayerEventListener(PlayerActivity activity)
            {
                _activity = activity;
            }

            public override void OnPlayerStateChanged(bool playWhenReady, int playbackState)
            {
                if (playbackState == Player.StateEnded)
                {
                    _activity.ShowControls();
                }
                _activity.UpdateButtonVisibilities();
            }

            public override void OnPositionDiscontinuity(int reason)
            {
                if (_activity._player.PlaybackError != null)
                {
                    // The user has performed a seek whilst in the error state. Update the resume position so
                    // that if the user then retries, playback resumes from the position to which they seeked.
                    _activity.UpdateStartPosition();
                }
            }

            public override void OnPlayerError(ExoPlaybackException e)
            {
                if (IsBehindLiveWindow(e))
                {
                    _activity.ClearStartPosition();
                    _activity.InitializePlayer();
                }
                else
                {
                    _activity.UpdateStartPosition();
                    _activity.UpdateButtonVisibilities();
                    _activity.ShowControls();
                }
            }

            public override void OnTracksChanged(TrackGroupArray trackGroups, TrackSelectionArray trackSelections)
            {
                _activity.UpdateButtonVisibilities();
                if (trackGroups != _activity._lastSeenTrackGroupArray)
                {
                    var mappedTrackInfo = _activity._trackSelector.CurrentMappedTrackInfo;
                    if (mappedTrackInfo != null)
                    {
                        if (mappedTrackInfo.GetTypeSupport(C.TrackTypeVideo)
                            == MappingTrackSelector.MappedTrackInfo.RendererSupportUnsupportedTracks)
                        {
                            _activity.ShowToast(Resource.String.error_unsupported_video);
                        }
                        if (mappedTrackInfo.GetTypeSupport(C.TrackTypeAudio)
                            == MappingTrackSelector.MappedTrackInfo.RendererSupportUnsupportedTracks)
                        {
                            _activity.ShowToast(Resource.String.error_unsupported_audio);
                        }
                    }
                    _activity._lastSeenTrackGroupArray = trackGroups;
                }
            }
        }

        internal class PlayerErrorMessageProvider : Java.Lang.Object, IErrorMessageProvider
        {
            private readonly Activity _activity;

            public PlayerErrorMessageProvider(Activity activity)
            {
                _activity = activity;
            }

            public Pair GetErrorMessage(ExoPlaybackException e)
            {
                var errorstring = _activity.ApplicationContext.GetString(Resource.String.error_generic);
                if (e.Type != ExoPlaybackException.TypeRenderer) return Pair.Create(0, errorstring);
                var cause = e.RendererException;

                var decoderInitializationException = cause as MediaCodecRenderer.DecoderInitializationException;
                if (decoderInitializationException == null) return Pair.Create(0, errorstring);
                // Special case for decoder initialization failures.
                if (decoderInitializationException.DecoderName == null)
                {
                    if (decoderInitializationException.Cause is MediaCodecUtil.DecoderQueryException)
                    {
                        errorstring = _activity.ApplicationContext.GetString(Resource.String.error_querying_decoders);
                    }
                    else if (decoderInitializationException.SecureDecoderRequired)
                    {
                        errorstring =
                            _activity.ApplicationContext.GetString(Resource.String.error_no_secure_decoder, decoderInitializationException.MimeType);
                    }
                    else
                    {
                        errorstring =
                            _activity.ApplicationContext.GetString(Resource.String.error_no_decoder, decoderInitializationException.MimeType);
                    }
                }
                else
                {
                    errorstring =
                        _activity.ApplicationContext.GetString(Resource.String.error_instantiating_decoder, decoderInitializationException.DecoderName);
                }

                return Pair.Create(0, errorstring);
            }

            public Pair GetErrorMessage(Java.Lang.Object p0)
            {
                throw new NotImplementedException();
            }
        }
    }
}
