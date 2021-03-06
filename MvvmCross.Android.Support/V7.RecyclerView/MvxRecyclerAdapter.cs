﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MS-PL license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using MvvmCross.Binding;
using MvvmCross.Binding.Attributes;
using MvvmCross.Binding.Extensions;
using MvvmCross.Droid.Support.V7.RecyclerView.ItemTemplates;
using MvvmCross.Droid.Support.V7.RecyclerView.Model;
using MvvmCross.Exceptions;
using MvvmCross.Logging;
using MvvmCross.Platform.Android.Binding.BindingContext;
using MvvmCross.WeakSubscription;
using Object = Java.Lang.Object;

namespace MvvmCross.Droid.Support.V7.RecyclerView
{
    [Register("mvvmcross.droid.support.v7.recyclerview.MvxRecyclerAdapter")]
    public class MvxRecyclerAdapter
        : Android.Support.V7.Widget.RecyclerView.Adapter, IMvxRecyclerAdapter, IMvxRecyclerAdapterBindableHolder
    {
        private readonly IMvxAndroidBindingContext _bindingContext;

        private ICommand _itemClick, _itemLongClick;
        private IEnumerable _itemsSource;
        private IDisposable _subscription;
        private IMvxTemplateSelector _itemTemplateSelector;

        protected IMvxAndroidBindingContext BindingContext => _bindingContext;

        public MvxRecyclerAdapter() : this(MvxAndroidBindingContextHelpers.Current())
        {
        }

        public MvxRecyclerAdapter(IMvxAndroidBindingContext bindingContext)
        {
            _bindingContext = bindingContext;
        }

        public MvxRecyclerAdapter(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
        }

        public bool ReloadOnAllItemsSourceSets { get; set; }

        public ICommand ItemClick
        {
            get
            {
                return _itemClick;
            }
            set
            {
                if (ReferenceEquals(_itemClick, value))
                {
                    return;
                }

                if (_itemClick != null)
                {
                    MvxAndroidLog.Instance.Warn("Changing ItemClick may cause inconsistencies where some items still call the old command.");
                }

                _itemClick = value;
            }
        }

        public ICommand ItemLongClick
        {
            get
            {
                return _itemLongClick;
            }
            set
            {
                if (ReferenceEquals(_itemLongClick, value))
                {
                    return;
                }

                if (_itemLongClick != null)
                {
                    MvxAndroidLog.Instance.Warn("Changing ItemLongClick may cause inconsistencies where some items still call the old command.");
                }

                _itemLongClick = value;
            }
        }

        [MvxSetToNullAfterBinding]
        public virtual IEnumerable ItemsSource
        {
            get { return _itemsSource; }
            set { SetItemsSource(value); }
        }

        public virtual IMvxTemplateSelector ItemTemplateSelector
        {
            get
            {
                return _itemTemplateSelector;
            }
            set
            {
                if (ReferenceEquals(_itemTemplateSelector, value))
                    return;

                _itemTemplateSelector = value;

                // since the template selector has changed then let's force the list to redisplay by firing NotifyDataSetChanged()
                if (_itemsSource != null)
                    NotifyDataSetChanged();
            }
        }

        public override void OnViewAttachedToWindow(Object holder)
        {
            base.OnViewAttachedToWindow(holder);

            var viewHolder = (IMvxRecyclerViewHolder)holder;
            viewHolder.OnAttachedToWindow();
        }

        public override void OnViewDetachedFromWindow(Object holder)
        {
            base.OnViewDetachedFromWindow(holder);

            var viewHolder = (IMvxRecyclerViewHolder)holder;
            viewHolder.OnDetachedFromWindow();
        }

        public override void OnViewRecycled(Object holder)
        {
            base.OnViewRecycled(holder);

            var viewHolder = (IMvxRecyclerViewHolder)holder;
            viewHolder.OnViewRecycled();
        }

        public override Android.Support.V7.Widget.RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var itemBindingContext = new MvxAndroidBindingContext(parent.Context, BindingContext.LayoutInflaterHolder);

            var vh = new MvxRecyclerViewHolder(InflateViewForHolder(parent, viewType, itemBindingContext), itemBindingContext)
            {
                Click = ItemClick,
                LongClick = ItemLongClick,
                Id = ItemTemplateSelector.GetItemLayoutId(viewType)
            };

            return vh;
        }

        public override int GetItemViewType(int position)
        {
            var itemAtPosition = GetItem(position);
            return ItemTemplateSelector.GetItemViewType(itemAtPosition);
        }

        protected virtual View InflateViewForHolder(ViewGroup parent, int viewType, IMvxAndroidBindingContext bindingContext)
        {
            var layoutId = ItemTemplateSelector.GetItemLayoutId(viewType);
            return bindingContext.BindingInflate(layoutId, parent, false);
        }

        public override void OnBindViewHolder(Android.Support.V7.Widget.RecyclerView.ViewHolder holder, int position)
        {
            var dataContext = GetItem(position);
            if (((IMvxRecyclerViewHolder) holder).Id == global::Android.Resource.Layout.SimpleListItem1)
                ((TextView) holder.ItemView).Text = dataContext?.ToString();
            ((IMvxRecyclerViewHolder)holder).DataContext = dataContext;
            OnMvxViewHolderBound(new MvxViewHolderBoundEventArgs(position, dataContext, holder));
        }

        public override int ItemCount => _itemsSource.Count();

        public virtual object GetItem(int viewPosition)
        {
            var itemsSourcePosition = GetItemsSourcePosition(viewPosition);

            if (itemsSourcePosition >= 0 && itemsSourcePosition < _itemsSource.Count())
            {
                return _itemsSource.ElementAt(itemsSourcePosition);
            }

            return null;
        }

        protected virtual int GetViewPosition(object item)
        {
            var itemsSourcePosition = _itemsSource.GetPosition(item);
            return GetViewPosition(itemsSourcePosition);
        }

        protected virtual int GetViewPosition(int itemsSourcePosition)
        {
            return itemsSourcePosition;
        }

        protected virtual int GetItemsSourcePosition(int viewPosition)
        {
            return viewPosition;
        }

        public int ItemTemplateId { get; set; }

        protected virtual void SetItemsSource(IEnumerable value)
        {
            if (ReferenceEquals(_itemsSource, value) && !ReloadOnAllItemsSourceSets)
            {
                return;
            }

            _subscription?.Dispose();
            _subscription = null;

            _itemsSource = value;

            if (_itemsSource != null && !(_itemsSource is IList))
            {
                MvxBindingLog.Warning("Binding to IEnumerable rather than IList - this can be inefficient, especially for large lists");
            }

            var newObservable = _itemsSource as INotifyCollectionChanged;
            if (newObservable != null)
            {
                _subscription = newObservable.WeakSubscribe(OnItemsSourceCollectionChanged);
            }

            NotifyDataSetChanged();
        }

        protected virtual void OnItemsSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (Looper.MainLooper == Looper.MyLooper())
            {
                NotifyDataSetChanged(e);
            }
            else
            {
                var h = new Handler(Looper.MainLooper);
                h.Post(() => NotifyDataSetChanged(e));
            }
        }

        public virtual void NotifyDataSetChanged(NotifyCollectionChangedEventArgs e)
        {
            try
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        NotifyItemRangeInserted(GetViewPosition(e.NewStartingIndex), e.NewItems.Count);
                        break;
                    case NotifyCollectionChangedAction.Move:
                        for (int i = 0; i < e.NewItems.Count; i++)
                            NotifyItemMoved(GetViewPosition(e.OldStartingIndex + i), GetViewPosition(e.NewStartingIndex + i));
                        break;
                    case NotifyCollectionChangedAction.Replace:
                        NotifyItemRangeChanged(GetViewPosition(e.NewStartingIndex), e.NewItems.Count);
                        break;
                    case NotifyCollectionChangedAction.Remove:
                        NotifyItemRangeRemoved(GetViewPosition(e.OldStartingIndex), e.OldItems.Count);
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        NotifyDataSetChanged();
                        break;
                }
            }
            catch (Exception exception)
            {
                MvxAndroidLog.Instance.Warn(
                    "Exception masked during Adapter RealNotifyDataSetChanged {0}. Are you trying to update your collection from a background task? See http://goo.gl/0nW0L6",
                    exception.ToLongString());
            }
        }

        public event Action<MvxViewHolderBoundEventArgs> MvxViewHolderBound;

        protected virtual void OnMvxViewHolderBound(MvxViewHolderBoundEventArgs obj)
        {
            MvxViewHolderBound?.Invoke(obj);
        }
    }
}
