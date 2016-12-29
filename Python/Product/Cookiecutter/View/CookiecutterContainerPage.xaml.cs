﻿// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.CookiecutterTools.Infrastructure;
using Microsoft.CookiecutterTools.Model;
using Microsoft.CookiecutterTools.Telemetry;
using Microsoft.CookiecutterTools.ViewModel;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.CookiecutterTools.View {
    /// <summary>
    /// Interaction logic for CookiecutterContainerPage.xaml
    /// </summary>
    internal partial class CookiecutterContainerPage : Page {
        private CookiecutterSearchPage _searchPage;
        private CookiecutterOptionsPage _optionsPage;
        private Action _updateCommandUI;
        private DispatcherTimer _checkForUpdatesTimer;

        public event EventHandler<PointEventArgs> ContextMenuRequested;

        private static TimeSpan CheckForUpdateInitialDelay = TimeSpan.FromSeconds(15);
        private static TimeSpan CheckForUpdateRetryDelay = TimeSpan.FromMinutes(2);

        public CookiecutterContainerPage() {
            InitializeComponent();
        }

        public CookiecutterContainerPage(IServiceProvider provider, Redirector outputWindow, ICookiecutterTelemetry telemetry, IGitClient gitClient, Uri feedUrl, Action<string> openFolder, IProjectSystemClient projectSystemClient, Action updateCommandUI) {
            _updateCommandUI = updateCommandUI;

            _checkForUpdatesTimer = new DispatcherTimer();
            _checkForUpdatesTimer.Tick += new EventHandler(CheckForUpdateTimer_Tick);

            var gitHubClient = new GitHubClient();
            ViewModel = new CookiecutterViewModel(
                CookiecutterClientProvider.Create(provider, outputWindow),
                gitHubClient,
                gitClient,
                telemetry,
                outputWindow,
                new LocalTemplateSource(CookiecutterViewModel.DefaultInstalledFolderPath, gitClient),
                new FeedTemplateSource(feedUrl),
                new GitHubTemplateSource(gitHubClient),
                openFolder,
                projectSystemClient
            );

            ViewModel.UserConfigFilePath = CookiecutterViewModel.GetUserConfigPath();
            ViewModel.OutputFolderPath = string.Empty; // leaving this empty for now, initialize on context creation
            ViewModel.ContextLoaded += ViewModel_ContextLoaded;
            ViewModel.HomeClicked += ViewModel_HomeClicked;

            _searchPage = new CookiecutterSearchPage { DataContext = ViewModel };
            _optionsPage = new CookiecutterOptionsPage { DataContext = ViewModel };

            var pages = new List<Page>();
            pages.Add(_searchPage);
            pages.Add(_optionsPage);

            _pageSequence = new CollectionViewSource {
                Source = new ObservableCollection<Page>(pages)
            };
            PageCount = _pageSequence.View.OfType<object>().Count();

            PageSequence = _pageSequence.View;
            PageSequence.MoveCurrentToFirst();

            DataContext = this;

            InitializeComponent();

            _searchPage.SelectedTemplateChanged += SearchPage_SelectedTemplateChanged;
        }

        public async Task InitializeAsync(bool checkForUpdates) {
            await ViewModel.SearchAsync();

            if (checkForUpdates) {
                _checkForUpdatesTimer.Interval = CheckForUpdateInitialDelay;
                _checkForUpdatesTimer.Start();
            }
        }

        private void CheckForUpdateTimer_Tick(object sender, EventArgs e) {
            AutomaticCheckForUpdates().DoNotWait();
        }

        private async Task AutomaticCheckForUpdates() {
            _checkForUpdatesTimer.Stop();

            bool reschedule = false;
            if (CanCheckForUpdates()) {
                await ViewModel.CheckForUpdatesAsync();

                if (ViewModel.CheckingUpdateStatus == OperationStatus.Canceled) {
                    reschedule = true;
                }
            } else {
                reschedule = true;
            }

            if (reschedule) {
                _checkForUpdatesTimer.Interval = CheckForUpdateRetryDelay;
                _checkForUpdatesTimer.Start();
            }
        }

        private void SearchPage_SelectedTemplateChanged(object sender, EventArgs e) {
            _updateCommandUI();
        }

        private void ViewModel_HomeClicked(object sender, EventArgs e) {
            Home();
        }

        private void ViewModel_ContextLoaded(object sender, EventArgs e) {
            PageSequence.MoveCurrentToLast();
            _updateCommandUI();
        }

        public CookiecutterViewModel ViewModel {
            get { return (CookiecutterViewModel)GetValue(SettingsProperty); }
            private set { SetValue(SettingsPropertyKey, value); }
        }

        private static readonly DependencyPropertyKey SettingsPropertyKey = DependencyProperty.RegisterReadOnly("Settings", typeof(CookiecutterViewModel), typeof(CookiecutterContainerPage), new PropertyMetadata());
        public static readonly DependencyProperty SettingsProperty = SettingsPropertyKey.DependencyProperty;

        public int PageCount {
            get { return (int)GetValue(PageCountProperty); }
            private set { SetValue(PageCountPropertyKey, value); }
        }

        private static readonly DependencyPropertyKey PageCountPropertyKey = DependencyProperty.RegisterReadOnly("PageCount", typeof(int), typeof(CookiecutterContainerPage), new PropertyMetadata(0));
        public static readonly DependencyProperty PageCountProperty = PageCountPropertyKey.DependencyProperty;

        private CollectionViewSource _pageSequence;

        public ICollectionView PageSequence {
            get { return (ICollectionView)GetValue(PageSequenceProperty); }
            private set { SetValue(PageSequencePropertyKey, value); }
        }

        private static readonly DependencyPropertyKey PageSequencePropertyKey = DependencyProperty.RegisterReadOnly("PageSequence", typeof(ICollectionView), typeof(CookiecutterContainerPage), new PropertyMetadata());
        public static readonly DependencyProperty PageSequenceProperty = PageSequencePropertyKey.DependencyProperty;

        internal void NavigateToGitHubHome() {
            ViewModel.NavigateToGitHubHome();
        }

        internal void NavigateToGitHubIssues() {
            ViewModel.NavigateToGitHubIssues();
        }

        internal void NavigateToGitHubWiki() {
            ViewModel.NavigateToGitHubWiki();
        }

        internal void Home() {
            PageSequence.MoveCurrentToFirst();
            _updateCommandUI();

            ViewModel.FixedOutputFolder = false;
            ViewModel.TargetProjectLocation = null;
            ViewModel.SearchTerm = string.Empty;
            ViewModel.SearchAsync().DoNotWait();
        }

        internal bool CanDeleteSelection() {
            return PageSequence.CurrentPosition == 0 && ViewModel.CanDeleteSelectedTemplate;
        }

        internal bool CanNavigateToGitHub() {
            return PageSequence.CurrentPosition == 0 && ViewModel.CanNavigateToGitHub;
        }

        internal void DeleteSelection() {
            if (!CanDeleteSelection()) {
                return;
            }

            var result = MessageBox.Show(string.Format(CultureInfo.CurrentUICulture, Strings.DeleteConfirmation, ViewModel.SelectedTemplate.ClonedPath), Strings.ProductTitle, MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
            if (result == MessageBoxResult.Yes) {
                ViewModel.DeleteTemplateAsync(ViewModel.SelectedTemplate).DoNotWait();
            }
        }

        internal bool CanRunSelection() {
            return PageSequence.CurrentPosition == 0 && ViewModel.CanRunSelectedTemplate;
        }

        internal void RunSelection() {
            if (!CanRunSelection()) {
                return;
            }

            _searchPage.LoadTemplate();
        }

        internal bool CanUpdateSelection() {
            return PageSequence.CurrentPosition == 0 && ViewModel.CanUpdateSelectedTemplate;
        }

        internal void UpdateSelection() {
            if (!CanUpdateSelection()) {
                return;
            }

            _searchPage.UpdateTemplate();
        }

        internal bool CanCheckForUpdates() {
            return PageSequence.CurrentPosition == 0 && ViewModel.CanCheckForUpdates;
        }

        internal void CheckForUpdates() {
            if (!CanCheckForUpdates()) {
                return;
            }

            // The user initiated check, so cancel any automatic check
            _checkForUpdatesTimer.Stop();

            _searchPage.CheckForUpdates();
        }

        internal void NewSession(ProjectLocation location) {
            Home();

            if (location != null) {
                ViewModel.OutputFolderPath = location.FolderPath;
                ViewModel.FixedOutputFolder = true;
                ViewModel.TargetProjectLocation = location;
            }
        }

        private void UserControl_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
            var element = (FrameworkElement)sender;
            var point = element.PointToScreen(GetPosition(e, element));

            ContextMenuRequested?.Invoke(this, new PointEventArgs(point));
        }

        private static Point GetPosition(InputEventArgs e, FrameworkElement fe) {
            var mouseEventArgs = e as MouseEventArgs;
            if (mouseEventArgs != null) {
                return mouseEventArgs.GetPosition(fe);
            }

            var touchEventArgs = e as TouchEventArgs;
            if (touchEventArgs != null) {
                return touchEventArgs.GetTouchPoint(fe).Position;
            }

            return new Point(0, 0);
        }

        private void UserControl_KeyUp(object sender, KeyEventArgs e) {
            if (e.Key == Key.Apps) {
                var element = (FrameworkElement)sender;
                var point = element.PointToScreen(new Point(0, 0));

                ContextMenuRequested?.Invoke(this, new PointEventArgs(point));
                e.Handled = true;
            }
        }
    }
}
