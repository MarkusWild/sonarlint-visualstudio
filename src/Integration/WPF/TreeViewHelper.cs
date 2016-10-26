﻿//-----------------------------------------------------------------------
// <copyright file="TreeViewHelper.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SonarLint.VisualStudio.Integration.WPF
{
    public class TreeViewHelper
    {
        private static Dictionary<DependencyObject, TreeViewSelectedItemBehavior> behaviors =
            new Dictionary<DependencyObject, TreeViewSelectedItemBehavior>();

        public static object GetSelectedItem(DependencyObject obj)
        {
            return obj.GetValue(SelectedItemProperty);
        }

        public static void SetSelectedItem(DependencyObject obj, object value)
        {
            obj.SetValue(SelectedItemProperty, value);
        }

        // Using a DependencyProperty as the backing store for SelectedItem.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.RegisterAttached("SelectedItem", typeof(object), typeof(TreeViewHelper), new UIPropertyMetadata(null, SelectedItemChanged));

        private static void SelectedItemChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (!(obj is TreeView))
            {
                return;
            }

            if (!behaviors.ContainsKey(obj))
            {
                behaviors.Add(obj, new TreeViewSelectedItemBehavior(obj as TreeView));
            }

            TreeViewSelectedItemBehavior view = behaviors[obj];
            view.ChangeSelectedItem(e.NewValue);
        }

        private class TreeViewSelectedItemBehavior
        {
            private readonly TreeView view;

            public TreeViewSelectedItemBehavior(TreeView view)
            {
                this.view = view;
                view.SelectedItemChanged += (sender, e) => SetSelectedItem(view, e.NewValue);
            }

            internal void ChangeSelectedItem(object p)
            {
                TreeViewItem item = (TreeViewItem)view.ItemContainerGenerator.ContainerFromItem(p);
                item.IsSelected = true;
            }
        }
    }
}
