// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WorkspacesEditor.Helpers;
using WorkspacesEditor.Models;
using WorkspacesEditor.ViewModels;

namespace WorkspacesEditor.Views
{
    public sealed partial class MainPage : Page
    {
        private MainViewModel _subscribedViewModel;

        public MainViewModel ViewModel { get; private set; }

        public MainPage()
        {
            this.InitializeComponent();
            ThemeHelper.TrackActualTheme(this);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                ViewModel = vm;
                this.DataContext = vm;
                Bindings.Update();

                _subscribedViewModel = vm;
                vm.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _subscribedViewModel = null;
            }

            base.OnNavigatedFrom(e);
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ViewModel.IsWorkspacesViewEmpty) && ViewModel.IsWorkspacesViewEmpty)
            {
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(EmptyStateText);
                peer?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
            }
        }

        private void NewProjectButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EnterSnapshotMode(false);
        }

        private void SortByLastLaunched_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OrderByIndex = 0;
        }

        private void SortByCreated_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OrderByIndex = 1;
        }

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OrderByIndex = 2;
        }

        private void WorkspaceCardClicked(object sender, RoutedEventArgs e)
        {
            Project project = GetProjectFromSender(sender);
            if (project != null)
            {
                ViewModel.CloseAllPopups();
                ViewModel.EditProject(project);
            }
        }

        private void WorkspaceItemClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Project project)
            {
                ViewModel.CloseAllPopups();
                ViewModel.EditProject(project);
            }
        }

        private static Project GetProjectFromSender(object sender)
        {
            if (sender is FrameworkElement element)
            {
                if (element.Tag is Project taggedProject)
                {
                    return taggedProject;
                }

                // Direct DataContext (works for card button with DataContext="{x:Bind}")
                if (element.DataContext is Project project)
                {
                    return project;
                }

                // For MenuFlyoutItems inside a flyout, walk up the visual tree
                var parent = element;
                while (parent != null)
                {
                    if (parent.DataContext is Project p)
                    {
                        return p;
                    }

                    parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent) as FrameworkElement;
                }
            }

            return null;
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            Project selectedProject = GetProjectFromSender(sender);
            if (selectedProject != null)
            {
                try
                {
                    await ViewModel.LaunchProjectAsync(selectedProject);
                }
                catch (System.Exception ex)
                {
                    ManagedCommon.Logger.LogError($"LaunchProject failed: {ex.Message}");
                }
            }
        }

        private void EditButtonClicked(object sender, RoutedEventArgs e)
        {
            Project project = GetProjectFromSender(sender);
            if (project != null)
            {
                ViewModel.CloseAllPopups();
                ViewModel.EditProject(project);
            }
        }

        private async void DeleteButtonClicked(object sender, RoutedEventArgs e)
        {
            Project project = GetProjectFromSender(sender);
            if (project == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = ResourceLoaderInstance.ResourceLoader?.GetString("Are_You_Sure") ?? "Are you sure?",
                Content = ResourceLoaderInstance.ResourceLoader?.GetString("Are_You_Sure_Description") ?? "Are you sure you want to delete this Workspace?",
                PrimaryButtonText = ResourceLoaderInstance.ResourceLoader?.GetString("Delete") ?? "Remove",
                CloseButtonText = ResourceLoaderInstance.ResourceLoader?.GetString("Cancel") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.DeleteProject(project);
            }
        }
    }
}
