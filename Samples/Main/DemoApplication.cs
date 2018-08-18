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

using System;
using Android.App;
using Com.Google.Android.Exoplayer2.Offline;
using Com.Google.Android.Exoplayer2.Source.Dash.Offline;
using Com.Google.Android.Exoplayer2.Source.Hls.Offline;
using Com.Google.Android.Exoplayer2.Source.Smoothstreaming.Offline;
using Utils = Com.Google.Android.Exoplayer2.Util.Util;
using Com.Google.Android.Exoplayer2.Upstream;
using Com.Google.Android.Exoplayer2.Upstream.Cache;
using android = Android;
using Java.IO;
using Android.Runtime;

namespace Com.Google.Android.Exoplayer2.Demo
{
    /**
     * Placeholder application to facilitate overriding Application methods for debugging and testing.
     */
     [Application]
    public class DemoApplication : Application
    {
        private const string DownloadActionFile = "actions";
        private const string DownloadTrackerActionFile = "tracked_actions";
        private const string DownloadContentDirectory = "downloads";
        private const int MaxSimultaneousDownloads = 2;
        private readonly DownloadAction.Deserializer[] _downloadDeserializers =
          {
            DashDownloadAction.Deserializer,
            HlsDownloadAction.Deserializer,
            SsDownloadAction.Deserializer,
            ProgressiveDownloadAction.Deserializer
        };

        protected string UserAgent;

        private File _downloadDirectory;
        private ICache _downloadCache;
        private Offline.DownloadManager _downloadManager;
        private DownloadTracker _downloadTracker;

        public DemoApplication(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public DemoApplication()
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
            UserAgent = Utils.GetUserAgent(this, "ExoPlayerDemo");
        }

        /** Returns a {@link DataSource.Factory}. */
        public IDataSourceFactory BuildDataSourceFactory(ITransferListener listener)
        {
            var upstreamFactory = new DefaultDataSourceFactory(this, listener, BuildHttpDataSourceFactory(listener));

            return BuildReadOnlyCacheDataSource(upstreamFactory, GetDownloadCache());
        }

        /** Returns a {@link HttpDataSource.Factory}. */
        public IHttpDataSourceFactory BuildHttpDataSourceFactory(ITransferListener listener)
        {
            return new DefaultHttpDataSourceFactory(UserAgent, listener);
        }

        /** Returns whether extension renderers should be used. */
        public bool UseExtensionRenderers()
        {
            return android.Support.Compat.BuildConfig.Flavor.Equals("withExtensions");
        }

        public Offline.DownloadManager GetDownloadManager()
        {
            InitDownloadManager();
            return _downloadManager;
        }

        public DownloadTracker GetDownloadTracker()
        {
            InitDownloadManager();
            return _downloadTracker;

        }

        private readonly object _lock = new object();

        private void InitDownloadManager()
        {
            lock (_lock)
            {
                if (_downloadManager != null) return;
                var downloaderConstructorHelper = new DownloaderConstructorHelper(
                    GetDownloadCache(),
                    BuildHttpDataSourceFactory(/* listener= */ null));

                _downloadManager =
                    new Offline.DownloadManager(
                        downloaderConstructorHelper,
                        MaxSimultaneousDownloads,
                        Offline.DownloadManager.DefaultMinRetryCount,
                        new File(GetDownloadDirectory(), DownloadActionFile),
                        _downloadDeserializers);

                _downloadTracker =
                    new DownloadTracker(
                        /* context= */ this,
                        BuildDataSourceFactory(/* listener= */ null),
                        new File(GetDownloadDirectory(), DownloadTrackerActionFile),
                        _downloadDeserializers);
                _downloadManager.AddListener(_downloadTracker);
            }
        }

        private ICache GetDownloadCache()
        {
            lock (_lock)
            {
                if (_downloadCache != null) return _downloadCache;
                var downloadContentDirectory = new File(GetDownloadDirectory(), DownloadContentDirectory);
                _downloadCache = new SimpleCache(downloadContentDirectory, new NoOpCacheEvictor());
            }
            return _downloadCache;

        }

        private File GetDownloadDirectory()
        {
            if (_downloadDirectory != null) return _downloadDirectory;
            _downloadDirectory = android.OS.Environment.ExternalStorageDirectory ?? FilesDir;
            return _downloadDirectory;
        }

        private static CacheDataSourceFactory BuildReadOnlyCacheDataSource(
            IDataSourceFactory upstreamFactory, ICache cache)
        {
            return new CacheDataSourceFactory(
                cache,
                upstreamFactory,
                new FileDataSourceFactory(),
                /* cacheWriteDataSinkFactory= */ null,
                CacheDataSource.FlagIgnoreCacheOnError,
                /* eventListener= */ null);
        }
    }
}
