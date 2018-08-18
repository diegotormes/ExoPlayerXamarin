using Android.App;
using Android.Content;
using Android.Gms.Cast.Framework;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.Graphics;
using Android.Support.V7.App;
using Android.Support.V7.Widget;
using Android.Support.V7.Widget.Helper;
using Android.Views;
using Android.Widget;
using Com.Google.Android.Exoplayer2.UI;
using static Android.Views.View;
using static Android.Widget.AdapterView;
using android = Android;

namespace Com.Google.Android.Exoplayer2.CastDemo
{
    [Activity(Label = "MainActivity")]
    public class MainActivity : AppCompatActivity, IOnClickListener, PlayerManager.IQueuePositionListener
    {

        private PlayerView _localPlayerView;
        private PlayerControlView _castControlView;
        private PlayerManager _playerManager;
        private RecyclerView _mediaQueueList;
        private MediaQueueListAdapter _mediaQueueListAdapter;
        private CastContext _castContext;

        // Activity lifecycle methods.

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            //Getting the cast context later than onStart can cause device discovery not to take place.
            _castContext = CastContext.GetSharedInstance(this);

            SetContentView(Resource.Layout.main_activity);

            _localPlayerView = (PlayerView)FindViewById(Resource.Id.local_player_view);
            _localPlayerView.RequestFocus();

            _castControlView = (PlayerControlView)FindViewById(Resource.Id.cast_control_view);

            _mediaQueueListAdapter = new MediaQueueListAdapter();
            _mediaQueueList = (RecyclerView)FindViewById(Resource.Id.sample_list);
            _mediaQueueList.SetLayoutManager(new LinearLayoutManager(this));
            _mediaQueueList.HasFixedSize = true;

            ItemTouchHelper helper = new ItemTouchHelper(new RecyclerViewCallback(_playerManager, _mediaQueueListAdapter));
            helper.AttachToRecyclerView(_mediaQueueList);

            FindViewById(Resource.Id.add_sample_button).SetOnClickListener(this);
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            base.OnCreateOptionsMenu(menu);
            MenuInflater.Inflate(Resource.Menu.menu, menu);
            CastButtonFactory.SetUpMediaRouteButton(this, menu, Resource.Id.media_route_menu_item);
            return true;
        }

        protected override void OnResume()
        {
            base.OnResume();
            _playerManager = PlayerManager.CreatePlayerManager(this, _localPlayerView, _castControlView, this, _castContext);
            _mediaQueueListAdapter.PlayerManager = _playerManager;
            _mediaQueueList.SetAdapter(_mediaQueueListAdapter);
        }

        protected override void OnPause()
        {
            base.OnPause();
            _mediaQueueListAdapter.NotifyItemRangeRemoved(0, _mediaQueueListAdapter.ItemCount);
            _mediaQueueList.SetAdapter(null);
            _playerManager.Release();
        }

        // Activity input.

        public override bool DispatchKeyEvent(KeyEvent @event)
        {
            //If the event was not handled then see if the player view can handle it.
            return base.DispatchKeyEvent(@event) || _playerManager.DispatchKeyEvent(@event);
        }

        public void OnClick(View view)
        {
            new android.Support.V7.App.AlertDialog.Builder(this).SetTitle(Resource.String.sample_list_dialog_title)
                .SetView(BuildSampleListView()).SetPositiveButton(android.Resource.String.Ok, (IDialogInterfaceOnClickListener)null).Create()
                .Show();
        }

        // PlayerManager.QueuePositionListener implementation.

        public void OnQueuePositionChanged(int previousIndex, int newIndex)
        {
            if (previousIndex != C.IndexUnset)
            {
                _mediaQueueListAdapter.NotifyItemChanged(previousIndex);
            }
            if (newIndex != C.IndexUnset)
            {
                _mediaQueueListAdapter.NotifyItemChanged(newIndex);
            }
        }

        // Internal methods.

        private View BuildSampleListView()
        {
            View dialogList = LayoutInflater.Inflate(Resource.Layout.sample_list, null);
            ListView sampleList = (ListView)dialogList.FindViewById(Resource.Id.sample_list);
            sampleList.Adapter = new SampleListAdapter(this);
            sampleList.OnItemClickListener = new OnItemClickListener(_playerManager, _mediaQueueListAdapter);
            return dialogList;
        }

        private class OnItemClickListener : Java.Lang.Object, IOnItemClickListener
        {
            private PlayerManager _playerManager;
            private MediaQueueListAdapter _mediaQueueListAdapter;

            public OnItemClickListener(PlayerManager playerManager, MediaQueueListAdapter mediaQueueListAdapter)
            {
                _playerManager = playerManager;
                _mediaQueueListAdapter = mediaQueueListAdapter;
            }

            public void OnItemClick(AdapterView parent, View view, int position, long id)
            {
                _playerManager.AddItem(DemoUtil.Samples[position]);
                _mediaQueueListAdapter.NotifyItemInserted(_playerManager.GetMediaQueueSize() - 1);
            }
        }

        // Internal classes.

        private class QueueItemViewHolder : RecyclerView.ViewHolder, IOnClickListener
        {

            public readonly TextView TextView;
            private PlayerManager _playerManager;

            public QueueItemViewHolder(TextView textView, PlayerManager playerManager) : base(textView)
            {
                TextView = textView;
                textView.SetOnClickListener(this);
                _playerManager = playerManager;
            }

            public void OnClick(View v)
            {
                _playerManager.SelectQueueItem(AdapterPosition);
            }
        }

        private class MediaQueueListAdapter : RecyclerView.Adapter
        {
            internal PlayerManager PlayerManager { get; set; }

            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
            {
                TextView v = (TextView)LayoutInflater.From(parent.Context).Inflate(android.Resource.Layout.SimpleListItem1, parent, false);
                return new QueueItemViewHolder(v, PlayerManager);
            }

            public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
            {
                TextView view = (TextView)holder.ItemView;
                view.Text = PlayerManager.GetItem(position).Name;
                //TODO: Solve coloring using the theme's ColorStateList.
                view.SetTextColor(new Color(ColorUtils.SetAlphaComponent(view.CurrentTextColor, position == PlayerManager.GetCurrentItemIndex() ? 255 : 100)));
            }

            public override int ItemCount
            {
                get { return PlayerManager.GetMediaQueueSize(); }
            }
        }

        private class RecyclerViewCallback : ItemTouchHelper.SimpleCallback
        {
            private int _draggingFromPosition;
            private int _draggingToPosition;
            private MediaQueueListAdapter _mediaQueueListAdapter;
            private PlayerManager _playerManager;

            public RecyclerViewCallback(PlayerManager playerManager, MediaQueueListAdapter mediaQueueListAdapter) : base(ItemTouchHelper.Up | ItemTouchHelper.Down, ItemTouchHelper.Start | ItemTouchHelper.End)
            {
                _playerManager = playerManager;
                _mediaQueueListAdapter = mediaQueueListAdapter;
                _draggingFromPosition = C.IndexUnset;
                _draggingToPosition = C.IndexUnset;
            }

            public override bool OnMove(RecyclerView list, RecyclerView.ViewHolder origin, RecyclerView.ViewHolder target)
            {
                int fromPosition = origin.AdapterPosition;
                int toPosition = target.AdapterPosition;
                if (_draggingFromPosition == C.IndexUnset)
                {
                    // A drag has started, but changes to the media queue will be reflected in clearView().
                    _draggingFromPosition = fromPosition;
                }
                _draggingToPosition = toPosition;
                _mediaQueueListAdapter.NotifyItemMoved(fromPosition, toPosition);
                return true;
            }

            public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction)
            {
                int position = viewHolder.AdapterPosition;
                if (_playerManager.RemoveItem(position))
                {
                    _mediaQueueListAdapter.NotifyItemRemoved(position);
                }
            }

            public override void ClearView(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
            {
                base.ClearView(recyclerView, viewHolder);
                if (_draggingFromPosition != C.IndexUnset)
                {
                    // A drag has ended. We reflect the media queue change in the player.
                    if (!_playerManager.MoveItem(_draggingFromPosition, _draggingToPosition))
                    {
                        // The move failed. The entire sequence of onMove calls since the drag started needs to be
                        // invalidated.
                        _mediaQueueListAdapter.NotifyDataSetChanged();
                    }
                }
                _draggingFromPosition = C.IndexUnset;
                _draggingToPosition = C.IndexUnset;
            }
        }

        private class SampleListAdapter : ArrayAdapter
        {
            public SampleListAdapter(Context context) : base(context, android.Resource.Layout.SimpleListItem1, DemoUtil.Samples)
            {
            }
        }
    }
}
