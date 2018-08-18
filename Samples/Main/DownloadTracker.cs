/*
 * Copyright (C) 2017 The Android Open Source Project
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
using System.Collections.Generic;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Com.Google.Android.Exoplayer2.Offline;
using Com.Google.Android.Exoplayer2.UI;
using Com.Google.Android.Exoplayer2.Upstream;
using Utils = Com.Google.Android.Exoplayer2.Util.Util;
using Java.IO;
using Android.Util;
using android = Android;
using Java.Lang;
using Com.Google.Android.Exoplayer2.Source.Dash.Offline;
using Com.Google.Android.Exoplayer2.Source.Smoothstreaming.Offline;
using Com.Google.Android.Exoplayer2.Source.Hls.Offline;
using System.Linq;
using DownloadManager = Com.Google.Android.Exoplayer2.Offline.DownloadManager;

namespace Com.Google.Android.Exoplayer2.Demo
{
    public class DownloadTracker : Java.Lang.Object, DownloadManager.IListener
    {
        /** Listens for changes in the tracked downloads. */
        public interface IListener
        {
            /** Called when the tracked downloads changed. */
            void OnDownloadsChanged();
        }

        private const string Tag = "DownloadTracker";

        private readonly Context _context;
        private readonly IDataSourceFactory _dataSourceFactory;
        private readonly ITrackNameProvider _trackNameProvider;
        private readonly List<IListener> _listeners;
        private readonly Dictionary<string, DownloadAction> _trackedDownloadStates;
        private readonly ActionFile _actionFile;
        private readonly Handler _actionFileWriteHandler;

        internal Context Context { get { return _context; } }
        internal ITrackNameProvider TrackNameProvider
        {
            get
            {
                return _trackNameProvider;
            }
        }

        public DownloadTracker(
            Context context,
            IDataSourceFactory dataSourceFactory,
            File actionFile,
            DownloadAction.Deserializer[] deserializers)
        {
            _context = context.ApplicationContext;
            _dataSourceFactory = dataSourceFactory;
            _actionFile = new ActionFile(actionFile);
            _trackNameProvider = new DefaultTrackNameProvider(context.Resources);
            _listeners = new List<IListener>();
            _trackedDownloadStates = new Dictionary<string, DownloadAction>();
            var actionFileWriteThread = new HandlerThread("DownloadTracker");
            actionFileWriteThread.Start();
            _actionFileWriteHandler = new Handler(actionFileWriteThread.Looper);
            LoadTrackedActions(deserializers);
        }

        public void AddListener(IListener listener)
        {
            _listeners.Add(listener);
        }

        public void RemoveListener(IListener listener)
        {
            _listeners.Remove(listener);
        }

        public bool IsDownloaded(android.Net.Uri uri)
        {
            return _trackedDownloadStates.ContainsKey(uri.ToString());
        }

        public List<object> GetOfflineStreamKeys(android.Net.Uri uri)
        {
            if (!_trackedDownloadStates.ContainsKey(uri.ToString()))
            {
                return new List<object>();
            }
            var action = _trackedDownloadStates[uri.ToString()];

            if (action is SegmentDownloadAction)
            {
                var objs = new List<object>(((SegmentDownloadAction)action).Keys.ToArray());

                return objs;
            }

            return new List<object>();
        }

        public void ToggleDownload(Activity activity, string name, android.Net.Uri uri, string extension)
        {
            if (IsDownloaded(uri))
            {
                var removeAction =
                    GetDownloadHelper(uri, extension).GetRemoveAction(Utils.GetUtf8Bytes(name));

                StartServiceWithAction(removeAction);
            }
            else
            {
                var helper = new StartDownloadDialogHelper(activity, GetDownloadHelper(uri, extension), this, name);
                helper.Prepare();
            }
        }

        // DownloadManager.Listener

        public void OnInitialized(Offline.DownloadManager downloadManager)
        {
            // Do nothing.
        }

        public void OnTaskStateChanged(Offline.DownloadManager downloadManager, DownloadManager.TaskState taskState)
        {
            var action = taskState.Action;
            var uri = action.Uri;
            if ((action.IsRemoveAction && taskState.State == DownloadManager.TaskState.StateCompleted)
                || (!action.IsRemoveAction && taskState.State == DownloadManager.TaskState.StateFailed))
            {
                // A download has been removed, or has failed. Stop tracking it.
                if (_trackedDownloadStates.Remove(uri.ToString()) != false)
                {
                    HandleTrackedDownloadStatesChanged();
                }
            }
        }

        public void OnIdle(Offline.DownloadManager downloadManager)
        {
            // Do nothing.
        }

        // Internal methods

        private void LoadTrackedActions(DownloadAction.Deserializer[] deserializers)
        {
            try
            {
                var allActions = _actionFile.Load(deserializers);

                foreach (var action in allActions)
                {
                    _trackedDownloadStates[action.Uri.ToString()] = action;
                }
            }
            catch (IOException e)
            {
                Log.Error(Tag, "Failed to load tracked actions", e);
            }
        }

        private void HandleTrackedDownloadStatesChanged()
        {
            foreach (var listener in _listeners)
            {
                listener.OnDownloadsChanged();
            }

            var actions = _trackedDownloadStates.Select(aa => aa.Value).ToList().ToArray();

            _actionFileWriteHandler.Post(new Action(() =>
            {
                try
                {
                    _actionFile.Store(actions);
                }
                catch (IOException e)
                {
                    Log.Error(Tag, string.Format("Failed to store tracked actions\r\n{0}", e.ToString()), e);
                }
            }));
        }

        internal void StartDownload(DownloadAction action)
        {
            if (_trackedDownloadStates.ContainsKey(action.Uri.ToString()))
            {
                // This content is already being downloaded. Do nothing.
                return;
            }
            _trackedDownloadStates[action.Uri.ToString()] = action;
            HandleTrackedDownloadStatesChanged();
            StartServiceWithAction(action);
        }

        private void StartServiceWithAction(DownloadAction action)
        {
            DownloadService.StartWithAction(_context, Class.FromType(typeof(DemoDownloadService)), action, false);
        }

        private DownloadHelper GetDownloadHelper(android.Net.Uri uri, string extension)
        {
            var type = Utils.InferContentType(uri, extension);
            switch (type)
            {
                case C.TypeDash:
                    return new DashDownloadHelper(uri, _dataSourceFactory);
                case C.TypeSs:
                    return new SsDownloadHelper(uri, _dataSourceFactory);
                case C.TypeHls:
                    return new HlsDownloadHelper(uri, _dataSourceFactory);
                case C.TypeOther:
                    return new ProgressiveDownloadHelper(uri);
                default:
                    throw new IllegalStateException("Unsupported type: " + type);
            }
        }

        internal class StartDownloadDialogHelper : Java.Lang.Object, DownloadHelper.ICallback, IDialogInterfaceOnClickListener
        {
            private readonly DownloadHelper _downloadHelper;
            private readonly DownloadTracker _downloadTracker;
            private readonly string _name;

            private readonly AlertDialog.Builder _builder;
            private readonly View _dialogView;
            private readonly List<TrackKey> _trackKeys;
            private readonly ArrayAdapter<string> _trackTitles;
            private readonly ListView _representationList;

            public StartDownloadDialogHelper(Activity activity, DownloadHelper downloadHelper, DownloadTracker downloadTracker, string name)
            {
                _downloadHelper = downloadHelper;
                _downloadTracker = downloadTracker;
                _name = name;
                _builder =
                    new AlertDialog.Builder(activity)
                        .SetTitle(Resource.String.exo_download_description)
                        .SetPositiveButton(android.Resource.String.Ok, this)
                        .SetNegativeButton(android.Resource.String.Cancel, (IDialogInterfaceOnClickListener)null);

                // Inflate with the builder's context to ensure the correct style is used.
                var dialogInflater = LayoutInflater.From(_builder.Context);
                _dialogView = dialogInflater.Inflate(Resource.Layout.start_download_dialog, null);

                _trackKeys = new List<TrackKey>();
                _trackTitles = new ArrayAdapter<string>(_builder.Context, android.Resource.Layout.SimpleListItemMultipleChoice);

                _representationList = (ListView)_dialogView.FindViewById(Resource.Id.representation_list);
                _representationList.ChoiceMode = ChoiceMode.Multiple;
                _representationList.Adapter = _trackTitles;
            }

            public void Prepare()
            {
                _downloadHelper.Prepare(this);
            }

            public void OnPrepared(DownloadHelper helper)
            {
                for (var i = 0; i < _downloadHelper.PeriodCount; i++)
                {
                    var trackGroups = _downloadHelper.GetTrackGroups(i);
                    for (var j = 0; j < trackGroups.Length; j++)
                    {
                        var trackGroup = trackGroups.Get(j);
                        for (var k = 0; k < trackGroup.Length; k++)
                        {
                            _trackKeys.Add(new TrackKey(i, j, k));

                            var trackNameProvider = _downloadTracker.TrackNameProvider;

                            var trackName = trackNameProvider.GetTrackName(trackGroup.GetFormat(k));

                            _trackTitles.Add(trackName);
                        }
                    }
                    if (_trackKeys.Count != 0)
                    {
                        _builder.SetView(_dialogView);
                    }
                    _builder.Create().Show();
                }
            }

            public void OnPrepareError(DownloadHelper helper, IOException e)
            {
                Toast.MakeText(
                       _downloadTracker.Context.ApplicationContext, Resource.String.download_start_error, ToastLength.Long)
                    .Show();
            }

            public void OnClick(IDialogInterface dialog, int which)
            {
                var selectedTrackKeys = new Java.Util.ArrayList();
                for (var i = 0; i < _representationList.ChildCount; i++)
                {
                    if (_representationList.IsItemChecked(i))
                    {
                        selectedTrackKeys.Add(_trackKeys[i]);
                    }
                }
                if (!selectedTrackKeys.IsEmpty || _trackKeys.Count == 0)
                {
                    // We have selected keys, or we're dealing with single stream content.
                    var downloadAction =
                        _downloadHelper.GetDownloadAction(Utils.GetUtf8Bytes(_name), selectedTrackKeys);

                    _downloadTracker.StartDownload(downloadAction);
                }
            }
        }
    }
}
