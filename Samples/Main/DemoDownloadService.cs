﻿/*
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

using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Com.Google.Android.Exoplayer2.Scheduler;
using Com.Google.Android.Exoplayer2.UI;
using Com.Google.Android.Exoplayer2.Util;
using Com.Google.Android.Exoplayer2.Offline;
using Utils = Com.Google.Android.Exoplayer2.Util.Util;
using Android.Runtime;
using System;
using Java.Interop;
using DownloadManager = Com.Google.Android.Exoplayer2.Offline.DownloadManager;

namespace Com.Google.Android.Exoplayer2.Demo
{
    /** A service for downloading media. */
    [Service(Exported = false, Name = "com.google.android.exoplayer2.demo.DemoDownloadService")]
    [IntentFilter(actions: new[] { "com.google.android.exoplayer.downloadService.action.INIT" }, Categories = new[] { "android.intent.category.DEFAULT" })]
    public class DemoDownloadService : DownloadService
    {
        public static readonly string ChannelId = "download_channel";
        private static readonly int JobId = 1;
        public static readonly int ForegroundNotificationId = 1;

        [Export(SuperArgumentsString = "1, 1000, \"download_channel\", 2131427508")]
        public DemoDownloadService() : this (ForegroundNotificationId, DefaultForegroundNotificationUpdateInterval, ChannelId, Resource.String.exo_download_notification_channel_name)
        {
        }

        protected DemoDownloadService(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected DemoDownloadService(int foregroundNotificationId) : base(foregroundNotificationId)
        {
        }

        protected DemoDownloadService(int foregroundNotificationId, long foregroundNotificationUpdateInterval) : base(foregroundNotificationId, foregroundNotificationUpdateInterval)
        {
        }

        protected DemoDownloadService(int foregroundNotificationId, long foregroundNotificationUpdateInterval, string channelId, int channelName) : base(foregroundNotificationId, foregroundNotificationUpdateInterval, channelId, channelName)
        {
        }

        protected override Offline.DownloadManager DownloadManager
        {
            get
            {
                return ((DemoApplication)Application).GetDownloadManager();
            }
        }

        protected override IScheduler Scheduler
        {
            get
            {
                return Utils.SdkInt >= 21 ? new PlatformScheduler(this, JobId) : null;
            }
        }

        public override IBinder OnBind(Intent intent)
        {
            // Return null because this is a pure started service. A hybrid service would return a binder that would allow communication back and forth
            return null;
        }

        public override void OnCreate()
        {
            base.OnCreate();
            Log.Debug("DemoDownloadService", "Service started.");
        }

        protected Offline.DownloadManager GetDownloadManager()
        {
            return ((DemoApplication)Application).GetDownloadManager();
        }

        protected PlatformScheduler GetScheduler()
        {
            return Utils.SdkInt >= 21 ? new PlatformScheduler(this, JobId) : null;
        }

        protected override Notification GetForegroundNotification(DownloadManager.TaskState[] taskStates)
        {
            return DownloadNotificationUtil.BuildProgressNotification(
                /* context= */ this,
                Resource.Drawable.exo_controls_play,
                ChannelId,
                /* contentIntent= */ null,
                /* message= */ null,
                taskStates);
        }

        protected override void OnTaskStateChanged(DownloadManager.TaskState taskState)
        {
            if (taskState.Action.IsRemoveAction)
            {
                return;
            }
            Notification notification = null;

            var bytes = new byte[taskState.Action.Data.Count];
            taskState.Action.Data.CopyTo(bytes, 0);

            if (taskState.State == DownloadManager.TaskState.StateCompleted)
            {
                notification =
                    DownloadNotificationUtil.BuildDownloadCompletedNotification(
                        /* context= */ this,
                        Resource.Drawable.exo_controls_play,
                        ChannelId,
                        /* contentIntent= */ null,
                        Utils.FromUtf8Bytes(bytes));
            }
            else if (taskState.State == DownloadManager.TaskState.StateFailed)
            {
                notification =
                    DownloadNotificationUtil.BuildDownloadFailedNotification(
                        /* context= */ this,
                        Resource.Drawable.exo_controls_play,
                        ChannelId,
                        /* contentIntent= */ null,
                       Utils.FromUtf8Bytes(bytes));
            }
            var notificationId = ForegroundNotificationId + 1 + taskState.TaskId;
            NotificationUtil.SetNotification(this, notificationId, notification);
        }
    }
}
