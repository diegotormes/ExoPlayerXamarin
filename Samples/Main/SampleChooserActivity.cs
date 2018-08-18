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

using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Google.Android.Exoplayer2.Upstream;
using Com.Google.Android.Exoplayer2.Util;
using Java.IO;
using Java.Lang;
using Utils = Com.Google.Android.Exoplayer2.Util.Util;
using Android.Graphics;
using android = Android;

namespace Com.Google.Android.Exoplayer2.Demo
{
    /** An activity for selecting from a list of media samples. */
    public class SampleChooserActivity : Activity, DownloadTracker.IListener, ExpandableListView.IOnChildClickListener
    {
        private const string Tag = "SampleChooserActivity";

        private DownloadTracker _downloadTracker;
        private SampleAdapter _sampleAdapter;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.sample_chooser_activity);
            _sampleAdapter = new SampleAdapter(this);
            var sampleListView = (ExpandableListView)FindViewById(Resource.Id.sample_list);
            sampleListView.SetAdapter(_sampleAdapter);
            sampleListView.SetOnChildClickListener(this);

            var intent = Intent;
            var dataUri = intent.DataString;
            string[] uris;
            if (dataUri != null)
            {
                uris = new[] { dataUri };
            }
            else
            {
                var uriList = new List<string>();
                var assetManager = Assets;
                try
                {
                    foreach (var asset in assetManager.List(""))
                    {
                        if (asset.EndsWith(".exolist.json"))
                        {
                            uriList.Add("asset:///" + asset);
                        }
                    }
                }
                catch (Java.IO.IOException e)
                {
                    Toast.MakeText(ApplicationContext, Resource.String.sample_list_load_error, ToastLength.Long).Show();
                    Toast.MakeText(ApplicationContext, e.Message, ToastLength.Long).Show();
                }

                uriList.Sort();
                uris = uriList.ToArray();
            }

            _downloadTracker = ((DemoApplication)Application).GetDownloadTracker();
            var loaderTask = new SampleListLoader(this);
            loaderTask.Execute(uris);

            // Start the download service if it should be running but it's not currently.
            // Starting the service in the foreground causes notification flicker if there is no scheduled
            // action. Starting it in the background throws an exception if the app is in the background too
            // (e.g. if device screen is locked).
            try
            {
                Offline.DownloadService.Start(this, Class.FromType(typeof(DemoDownloadService)));
            }
            catch (IllegalStateException)
            {
                Offline.DownloadService.StartForeground(this, Class.FromType(typeof(DemoDownloadService)));
            }
        }

        protected override void OnStart()
        {
            _downloadTracker.AddListener(this);
            _sampleAdapter.NotifyDataSetChanged();
            base.OnStart();
        }

        protected override void OnStop()
        {
            _downloadTracker.RemoveListener(this);
            base.OnStop();
        }

        public void OnDownloadsChanged()
        {
            _sampleAdapter.NotifyDataSetChanged();
        }

        private void OnSampleGroups(List<SampleGroup> groups, bool sawError)
        {
            if (sawError)
            {
                Toast.MakeText(ApplicationContext, Resource.String.sample_list_load_error, ToastLength.Long)
                    .Show();
            }

            _sampleAdapter.SetSampleGroups(groups);
        }

        public bool OnChildClick(ExpandableListView parent, View view, int groupPosition, int childPosition, long id)
        {
            var sample = (Sample)view.GetTag(view.Id);
            StartActivity(sample.BuildIntent(this));
            return true;
        }

        private void OnSampleDownloadButtonClicked(Sample sample)
        {
            var downloadUnsupportedstringId = GetDownloadUnsupportedstringId(sample);
            if (downloadUnsupportedstringId != 0)
            {
                Toast.MakeText(ApplicationContext, downloadUnsupportedstringId, ToastLength.Long)
                    .Show();
            }
            else
            {
                var uriSample = (UriSample)sample;
                _downloadTracker.ToggleDownload(this, sample.Name, uriSample.Uri, uriSample.Extension);
            }
        }

        private static int GetDownloadUnsupportedstringId(Sample sample)
        {
            if (sample is PlaylistSample)
            {
                return Resource.String.download_playlist_unsupported;
            }

            var uriSample = (UriSample)sample;

            if (uriSample.DrmInfo != null)
            {
                return Resource.String.download_drm_unsupported;
            }

            if (uriSample.AdTagUri != null)
            {
                return Resource.String.download_ads_unsupported;
            }

            var scheme = uriSample.Uri.Scheme;

            if (!("http".Equals(scheme) || "https".Equals(scheme)))
            {
                return Resource.String.download_scheme_unsupported;
            }
            return 0;
        }

        private class SampleListLoader : AsyncTask<string, int, List<SampleGroup>>
        {
            private readonly SampleChooserActivity _activity;

            public SampleListLoader(SampleChooserActivity activity)
            {
                _activity = activity;
            }

            private bool _sawError;

            protected override List<SampleGroup> RunInBackground(params string[] uris)
            {
                var result = new List<SampleGroup>();
                var context = _activity.ApplicationContext;
                var userAgent = Utils.GetUserAgent(context, "ExoPlayerDemo");
                IDataSource dataSource = new DefaultDataSource(context, null, userAgent, false);
                foreach (var uri in uris)
                {
                    var dataSpec = new DataSpec(android.Net.Uri.Parse(uri));
                    var inputStream = new DataSourceInputStream(dataSource, dataSpec);
                    var memory = new MemoryStream();
                    var buffer = new byte[1024];
                    int read;
                    while ((read = inputStream.Read(buffer)) > 0)
                    {
                        memory.Write(buffer, 0, read);
                    }
                    memory.Seek(0, SeekOrigin.Begin);

                    try
                    {
                        ReadSampleGroups(new JsonReader(new InputStreamReader(memory, "UTF-8")), result);
                    }
                    catch (Exception e)
                    {
                        Log.Error(Tag, "Error loading sample list: " + uri, e);
                        _sawError = true;
                    }
                    finally
                    {
                        Utils.CloseQuietly(dataSource);
                    }
                }

                return result;
            }

            // ReSharper disable once RedundantOverriddenMember
            // This overload is required so that the following overload is also called?! ¯\_(ツ)_/¯
            protected override void OnPostExecute(Object result)
            {
                base.OnPostExecute(result);
            }

            protected override void OnPostExecute(List<SampleGroup> result)
            {
                base.OnPostExecute(result);
                _activity.OnSampleGroups(result, _sawError);
            }

            private void ReadSampleGroups(JsonReader reader, List<SampleGroup> groups)
            {
                reader.BeginArray();
                while (reader.HasNext)
                {
                    ReadSampleGroup(reader, groups);
                }
                reader.EndArray();
            }

            private void ReadSampleGroup(JsonReader reader, List<SampleGroup> groups)
            {
                var groupName = "";
                var samples = new List<Sample>();

                reader.BeginObject();
                while (reader.HasNext)
                {
                    var name = reader.NextName();
                    switch (name)
                    {
                        case "name":
                            groupName = reader.NextString();
                            break;
                        case "samples":
                            reader.BeginArray();
                            while (reader.HasNext)
                            {
                                samples.Add(ReadEntry(reader, false));
                            }
                            reader.EndArray();
                            break;
                        case "_comment":
                            reader.NextString(); // Ignore.
                            break;
                        default:
                            throw new ParserException("Unsupported name: " + name);
                    }
                }
                reader.EndObject();

                var group = GetGroup(groupName, groups);
                group.Samples.AddRange(samples);
            }

            private static Sample ReadEntry(JsonReader reader, bool insidePlaylist)
            {
                string sampleName = null;
                android.Net.Uri uri = null;
                string extension = null;
                string drmScheme = null;
                string drmLicenseUrl = null;
                string[]
                drmKeyRequestProperties = null;
                var drmMultiSession = false;
                var preferExtensionDecoders = false;
                List<UriSample> playlistSamples = null;
                string adTagUri = null;
                string abrAlgorithm = null;

                reader.BeginObject();
                while (reader.HasNext)
                {
                    var name = reader.NextName();
                    switch (name)
                    {
                        case "name":
                            sampleName = reader.NextString();
                            break;
                        case "uri":
                            uri = android.Net.Uri.Parse(reader.NextString());
                            break;
                        case "extension":
                            extension = reader.NextString();
                            break;
                        case "drm_scheme":
                            Assertions.CheckState(!insidePlaylist, "Invalid attribute on nested item: drm_scheme");
                            drmScheme = reader.NextString();
                            break;
                        case "drm_license_url":
                            Assertions.CheckState(!insidePlaylist,
                                "Invalid attribute on nested item: drm_license_url");
                            drmLicenseUrl = reader.NextString();
                            break;
                        case "drm_key_request_properties":
                            Assertions.CheckState(!insidePlaylist,
                                "Invalid attribute on nested item: drm_key_request_properties");
                            var drmKeyRequestPropertiesList = new List<string>();
                            reader.BeginObject();
                            while (reader.HasNext)
                            {
                                drmKeyRequestPropertiesList.Add(reader.NextName());
                                drmKeyRequestPropertiesList.Add(reader.NextString());
                            }
                            reader.EndObject();
                            drmKeyRequestProperties = drmKeyRequestPropertiesList.ToArray();
                            break;
                        case "drm_multi_session":
                            drmMultiSession = reader.NextBoolean();
                            break;
                        case "prefer_extension_decoders":
                            Assertions.CheckState(!insidePlaylist,
                                "Invalid attribute on nested item: prefer_extension_decoders");
                            preferExtensionDecoders = reader.NextBoolean();
                            break;
                        case "playlist":
                            Assertions.CheckState(!insidePlaylist, "Invalid nesting of playlists");
                            playlistSamples = new List<UriSample>();
                            reader.BeginArray();
                            while (reader.HasNext)
                            {
                                playlistSamples.Add((UriSample)ReadEntry(reader, true));
                            }
                            reader.EndArray();
                            break;
                        case "ad_tag_uri":
                            adTagUri = reader.NextString();
                            break;
                        case "abr_algorithm":
                            Assertions.CheckState(
                                !insidePlaylist, "Invalid attribute on nested item: abr_algorithm");
                            abrAlgorithm = reader.NextString();
                            break;
                        default:
                            throw new ParserException("Unsupported attribute name: " + name);
                    }
                }
                reader.EndObject();
                var drmInfo =
                      drmScheme == null
                          ? null
                          : new DrmInfo(drmScheme, drmLicenseUrl, drmKeyRequestProperties, drmMultiSession);
                if (playlistSamples != null)
                {
                    var playlistSamplesArray = playlistSamples.ToArray();
                    return new PlaylistSample(
                        sampleName, preferExtensionDecoders, abrAlgorithm, drmInfo, playlistSamplesArray);
                }
                else
                {
                    return new UriSample(
                        sampleName, preferExtensionDecoders, abrAlgorithm, drmInfo, uri, extension, adTagUri);
                }
            }

            private SampleGroup GetGroup(string groupName, List<SampleGroup> groups)
            {
                foreach (var t in groups)
                {
                    if (Utils.AreEqual(groupName, t.Title))
                    {
                        return t;
                    }
                }
                var group = new SampleGroup(groupName);
                groups.Add(group);
                return group;
            }
        }

        internal class SampleAdapter : BaseExpandableListAdapter, View.IOnClickListener
        {
            readonly SampleChooserActivity _activity;
            private List<SampleGroup> _sampleGroups;

            public SampleAdapter(SampleChooserActivity activity)
            {
                _activity = activity;
                _sampleGroups = new List<SampleGroup>();
            }

            public void SetSampleGroups(List<SampleGroup> sampleGroups)
            {
                _sampleGroups = sampleGroups;
                NotifyDataSetChanged();
            }

            public override Object GetChild(int groupPosition, int childPosition)
            {
                return ((SampleGroup)GetGroup(groupPosition)).Samples[childPosition];
            }

            public override long GetChildId(int groupPosition, int childPosition)
            {
                return childPosition;
            }

            public override View GetChildView(int groupPosition, int childPosition, bool isLastChild,
                View convertView, ViewGroup parent)
            {
                var view = convertView;
                if (view == null)
                {
                    view = _activity.LayoutInflater.Inflate(Resource.Layout.sample_list_item, parent, false);
                    var downloadButton = (ImageView)view.FindViewById(Resource.Id.download_button);
                    downloadButton.SetOnClickListener(this);
                    //downloadButton.SetFocusable(ViewFocusability.NotFocusable);

                    downloadButton.Focusable = false;
                }
                InitializeChildView(view, (Sample)GetChild(groupPosition, childPosition));
                return view;
            }

            public override int GetChildrenCount(int groupPosition)
            {
                return ((SampleGroup)GetGroup(groupPosition)).Samples.Count;
            }

            public override Object GetGroup(int groupPosition)
            {
                return _sampleGroups[groupPosition];
            }

            public override long GetGroupId(int groupPosition)
            {
                return groupPosition;
            }

            public override View GetGroupView(int groupPosition, bool isExpanded, View convertView,
                ViewGroup parent)
            {
                var view = convertView ?? 
                           _activity.LayoutInflater.Inflate(android.Resource.Layout.SimpleExpandableListItem1, parent, false);
                ((TextView)view).SetText(((SampleGroup)GetGroup(groupPosition)).Title, TextView.BufferType.Normal);
                return view;
            }

            public override int GroupCount
            {
                get
                {
                    return _sampleGroups.Count;
                }
            }

            public override bool HasStableIds
            {
                get
                {
                    return false;
                }
            }

            public override bool IsChildSelectable(int groupPosition, int childPosition)
            {
                return true;
            }

            public void OnClick(View view)
            {
                _activity.OnSampleDownloadButtonClicked((Sample)view.GetTag(view.Id));
            }

            private void InitializeChildView(View view, Sample sample)
            {
                view.SetTag(view.Id, sample);
                var sampleTitle = (TextView)view.FindViewById(Resource.Id.sample_title);
                sampleTitle.SetText(sample.Name, TextView.BufferType.Normal);

                var canDownload = GetDownloadUnsupportedstringId(sample) == 0;
                var isDownloaded = canDownload && _activity._downloadTracker.IsDownloaded(((UriSample)sample).Uri);
                var downloadButton = (ImageButton)view.FindViewById(Resource.Id.download_button);
                downloadButton.SetTag(downloadButton.Id, sample);
                downloadButton.SetColorFilter(new Color((canDownload ? (isDownloaded ? int.Parse("FF42A5F5", System.Globalization.NumberStyles.HexNumber) : int.Parse("FFBDBDBD", System.Globalization.NumberStyles.HexNumber)) : int.Parse("FFEEEEEE", System.Globalization.NumberStyles.HexNumber))));
                downloadButton.SetImageResource(
                    isDownloaded ? Resource.Drawable.ic_download_done : Resource.Drawable.ic_download);
            }
        }

        internal class SampleGroup : Object
        {
            public string Title;
            public List<Sample> Samples;

            public SampleGroup(string title)
            {
                Title = title;
                Samples = new List<Sample>();
            }
        }

        internal class DrmInfo
        {
            public string DrmScheme;
            public string DrmLicenseUrl;
            public string[] DrmKeyRequestProperties;
            public bool DrmMultiSession;

            public DrmInfo(
                string drmScheme,
                string drmLicenseUrl,
                string[] drmKeyRequestProperties,
                bool drmMultiSession)
            {
                DrmScheme = drmScheme;
                DrmLicenseUrl = drmLicenseUrl;
                DrmKeyRequestProperties = drmKeyRequestProperties;
                DrmMultiSession = drmMultiSession;
            }

            public void UpdateIntent(Intent intent)
            {
                Assertions.CheckNotNull(intent);
                intent.PutExtra(PlayerActivity.DrmSchemeExtra, DrmScheme);
                intent.PutExtra(PlayerActivity.DrmLicenseUrlExtra, DrmLicenseUrl);
                intent.PutExtra(PlayerActivity.DrmKeyRequestPropertiesExtra, DrmKeyRequestProperties);
                intent.PutExtra(PlayerActivity.DrmMultiSessionExtra, DrmMultiSession);
            }
        }

        internal abstract class Sample : Object
        {
            public string Name;
            public bool PreferExtensionDecoders;
            public string AbrAlgorithm;
            public DrmInfo DrmInfo;

            protected Sample(string name, bool preferExtensionDecoders, string abrAlgorithm, DrmInfo drmInfo)
            {
                Name = name;
                PreferExtensionDecoders = preferExtensionDecoders;
                AbrAlgorithm = abrAlgorithm;
                DrmInfo = drmInfo;
            }

            public virtual Intent BuildIntent(Context context)
            {
                var intent = new Intent(context, Class.FromType(typeof(PlayerActivity)));
                intent.PutExtra(PlayerActivity.PreferExtensionDecodersExtra, PreferExtensionDecoders);
                intent.PutExtra(PlayerActivity.AbrAlgorithmExtra, AbrAlgorithm);
                if (DrmInfo != null)
                {
                    DrmInfo.UpdateIntent(intent);
                }
                return intent;
            }
        }

        internal class UriSample : Sample
        {
            public android.Net.Uri Uri;
            public string Extension;
            public string AdTagUri;

            public UriSample(
                string name,
                bool preferExtensionDecoders,
                string abrAlgorithm,
                DrmInfo drmInfo,
                android.Net.Uri uri,
                string extension,
                string adTagUri) : base(name, preferExtensionDecoders, abrAlgorithm, drmInfo)
            {
                Uri = uri;
                Extension = extension;
                AdTagUri = adTagUri;
            }

            public override Intent BuildIntent(Context context)
            {
                return base.BuildIntent(context)
                    .SetData(Uri)
                    .PutExtra(PlayerActivity.ExtensionExtra, Extension)
                    .PutExtra(PlayerActivity.AdTagUriExtra, AdTagUri)
                    .SetAction(PlayerActivity.ActionView);
            }
        }

        internal class PlaylistSample : Sample
        {
            public readonly UriSample[] Children;

            public PlaylistSample(
                string name,
                bool preferExtensionDecoders,
                string abrAlgorithm,
                DrmInfo drmInfo,
                params UriSample[] children) : base(name, preferExtensionDecoders, abrAlgorithm, drmInfo)
            {
                Children = children;
            }

            public override Intent BuildIntent(Context context)
            {
                var uris = new string[Children.Length];
                var extensions = new string[Children.Length];
                for (var i = 0; i < Children.Length; i++)
                {
                    uris[i] = Children[i].Uri.ToString();
                    extensions[i] = Children[i].Extension;
                }
                return base.BuildIntent(context)
                    .PutExtra(PlayerActivity.UriListExtra, uris)
                    .PutExtra(PlayerActivity.ExtensionListExtra, extensions)
                    .SetAction(PlayerActivity.ActionViewList);
            }
        }
    }
}
