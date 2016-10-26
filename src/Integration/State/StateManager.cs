//-----------------------------------------------------------------------
// <copyright file="StateManager.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using Microsoft.VisualStudio.Imaging;
using SonarLint.VisualStudio.Integration.Resources;
using SonarLint.VisualStudio.Integration.Service;
using SonarLint.VisualStudio.Integration.TeamExplorer;
using SonarLint.VisualStudio.Integration.WPF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace SonarLint.VisualStudio.Integration.State
{
    /// <summary>
    /// Implementation of <see cref="IStateManager"/>
    /// </summary>
    internal sealed class StateManager : IStateManager, IDisposable
    {
        private bool isDisposed;

        public StateManager(IHost host, TransferableVisualState state)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            this.Host = host;
            this.ManagedState = state;
            this.ManagedState.PropertyChanged += this.OnStatePropertyChanged;
        }

        #region IStateManager
        public event EventHandler<bool> IsBusyChanged;

        public event EventHandler BindingStateChanged;

        public TransferableVisualState ManagedState
        {
            get;
        }

        public IHost Host
        {
            get;
        }

        public bool IsBusy
        {
            get
            {
                return this.ManagedState.IsBusy;
            }
            set
            {
                this.ManagedState.IsBusy = value;
            }
        }

        public bool HasBoundProject
        {
            get
            {
                return this.ManagedState.HasBoundProject;
            }
        }

        public bool IsConnected
        {
            get
            {
                return this.GetConnectedServers().Any();
            }
        }

        public IEnumerable<ConnectionInformation> GetConnectedServers()
        {
            return this.ManagedState.ConnectedServers.Select(s => s.ConnectionInformation);
        }

        public ConnectionInformation GetConnectedServer(ProjectInformation project)
        {
            return this.ManagedState.ConnectedServers
                .SingleOrDefault(s => s.Projects.Any(p => p.ProjectInformation == project))?.ConnectionInformation;
        }

        public string BoundProjectKey { get; set; }

        public void SetProjects(IProjectSystemHelper projectSystem, ConnectionInformation connection, IEnumerable<ProjectInformation> projects)
        {
            if (this.Host.UIDispatcher.CheckAccess())
            {
                this.SetProjectsUIThread(projectSystem, connection, projects);
            }
            else
            {
                this.Host.UIDispatcher.BeginInvoke(new Action(() => this.SetProjectsUIThread(projectSystem, connection, projects)));
            }
        }

        public void SetBoundProject(ProjectInformation project)
        {
            this.ClearBindingErrorNotifications();
            ProjectViewModel projectViewModel = this.ManagedState.ConnectedServers.SelectMany(s => s.Projects).SingleOrDefault(p => p.ProjectInformation == project);
            Debug.Assert(projectViewModel != null, "Expecting a single project mapped to project information");
            this.ManagedState.SetBoundProject(projectViewModel);
            Debug.Assert(this.HasBoundProject, "Expected to have a bound project");

            this.OnBindingStateChanged();
        }

        public void ClearBoundProject()
        {
            this.ClearBindingErrorNotifications();
            this.ManagedState.ClearBoundProject();
            Debug.Assert(!this.HasBoundProject, "Expected not to have a bound project");

            this.OnBindingStateChanged();
        }

        public void SyncCommandFromActiveSection()
        {
            foreach (ServerViewModel serverVM in this.ManagedState.ConnectedServers)
            {
                this.SetServerVMCommands(serverVM);
                this.SetServerProjectsVMCommands(serverVM);
            }
        }
        #endregion

        #region Non public API
        private void OnIsBusyChanged(bool isBusy)
        {
            this.IsBusyChanged?.Invoke(this, isBusy);
        }

        private void OnBindingStateChanged()
        {
            this.BindingStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnStatePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(this.ManagedState.IsBusy))
            {
                this.OnIsBusyChanged(this.IsBusy);
            }
        }

        private void SetProjectsUIThread(IProjectSystemHelper projectSystem, ConnectionInformation connection, IEnumerable<ProjectInformation> projects)
        {
            Debug.Assert(connection != null);
            this.ClearBindingErrorNotifications();

            // !!! Avoid using the service to detect disconnects since it's not thread safe !!!
            if (projects == null)
            {
                // Disconnected, clear all
                this.ClearBoundProject();
                this.DisposeConnections();
                this.ManagedState.ConnectedServers.Clear();
            }
            else
            {
                var existingServerVM = this.ManagedState.ConnectedServers.SingleOrDefault(serverVM => serverVM.Url == connection.ServerUri);
                ServerViewModel serverViewModel;
                if (existingServerVM == null)
                {
                    // Add new server
                    serverViewModel = new ServerViewModel(connection);
                    this.SetServerVMCommands(serverViewModel);
                    this.ManagedState.ConnectedServers.Add(serverViewModel);
                }
                else
                {
                    // Update existing server
                    serverViewModel = existingServerVM;
                }

                serverViewModel.SetProjects(projects);
                Debug.Assert(serverViewModel.ShowAllProjects, "ShowAllProjects should have been set");
                this.SetServerProjectsVMCommands(serverViewModel);
                this.RestoreBoundProject(serverViewModel);
                this.SelectSonarQubeProjectWithMoreMatches(projectSystem, serverViewModel);
            }
        }

        private void DisposeConnections()
        {
            this.ManagedState.ConnectedServers
                .Select(s => s.ConnectionInformation)
                .ToList()
                .ForEach(c => c.Dispose());
        }

        private void ClearBindingErrorNotifications()
        {
            this.Host.ActiveSection?.UserNotifications?.HideNotification(NotificationIds.FailedToFindBoundProjectKeyId);
        }

        private void RestoreBoundProject(ServerViewModel serverViewModel)
        {
            if (this.BoundProjectKey == null)
            {
                // Nothing to restore
                return;
            }

            ProjectInformation boundProject = serverViewModel.Projects.FirstOrDefault(pvm => ProjectInformation.KeyComparer.Equals(pvm.Key, this.BoundProjectKey))?.ProjectInformation;
            if (boundProject == null)
            {
                // Defensive coding: invoked asynchronous and it's safer to assume that value could be null
                // and just not do anything since if they are null it means that there's no solution open.
                this.Host.ActiveSection?.UserNotifications?.ShowNotificationError(string.Format(CultureInfo.CurrentCulture, Strings.BoundProjectNotFound, this.BoundProjectKey), NotificationIds.FailedToFindBoundProjectKeyId, null);
            }
            else
            {
                this.SetBoundProject(boundProject);
            }
        }

        private void SelectSonarQubeProjectWithMoreMatches(IProjectSystemHelper projectSystem, ServerViewModel serverViewModel)
        {
            if (this.BoundProjectKey != null)
            {
                // Project is bound, project selection is useless
                return;
            }

            if (projectSystem == null)
            {
                return;
            }

            var allSolutionProjectGuids = projectSystem.GetAggregateProjectGuids().Select(guid => guid.ToString()).ToList();

            // First, we create a tuple with the project and the count of matching GUIDs.
            var projecAndMatchCountTuples = serverViewModel.Projects.Select(p => new { Project = p, MatchCount = GetCountOfMatchingGuids(p, allSolutionProjectGuids) }).ToList();

            if (projecAndMatchCountTuples.Any())
            {
                // Then, we fold them to obtain the tuple with the highest count, on which we take the project.
                this.ManagedState.SelectedProject = projecAndMatchCountTuples.Aggregate((left, right) => left.MatchCount > right.MatchCount ? left : right)
                                                                             .Project;
            }
        }

        private int GetCountOfMatchingGuids(ProjectViewModel project, List<string> solutionProjectGuids)
        {
            return project.ProjectInformation.Components.Select(c => c.Key).Distinct().Intersect(solutionProjectGuids).Count();
        }

        private void SetServerVMCommands(ServerViewModel serverVM)
        {
            serverVM.Commands.Clear();
            if (this.Host.ActiveSection == null)
            {
                // Don't add command (which will be disabled).
                return;
            }


            var refreshContextualCommand = new ContextualCommandViewModel(serverVM, this.Host.ActiveSection.RefreshCommand)
            {
                DisplayText = Strings.RefreshCommandDisplayText,
                Tooltip = Strings.RefreshCommandTooltip,
                Icon = new IconViewModel(KnownMonikers.Refresh)
            };

            var disconnectContextualCommand = new ContextualCommandViewModel(serverVM, this.Host.ActiveSection.DisconnectCommand)
            {
                DisplayText = Strings.DisconnectCommandDisplayText,
                Tooltip = Strings.DisconnectCommandTooltip,
                Icon = new IconViewModel(KnownMonikers.Disconnect)
            };

            var browseServerContextualCommand = new ContextualCommandViewModel(serverVM.Url.ToString(), this.Host.ActiveSection.BrowseToUrlCommand)
            {
                DisplayText = Strings.BrowseServerMenuItemDisplayText,
                Tooltip = Strings.BrowserServerMenuItemTooltip,
                Icon = new IconViewModel(KnownMonikers.OpenWebSite)
            };

            var toggleShowAllProjectsCommand = new ContextualCommandViewModel(serverVM, this.Host.ActiveSection.ToggleShowAllProjectsCommand)
            {
                Tooltip = Strings.ToggleShowAllProjectsCommandTooltip
            };
            toggleShowAllProjectsCommand.SetDynamicDisplayText(x =>
            {
                ServerViewModel ctx = x as ServerViewModel;
                Debug.Assert(ctx != null, "Unexpected fixed context for ToggleShowAllProjects context command");
                return ctx?.ShowAllProjects ?? false ? Strings.HideUnboundProjectsCommandText : Strings.ShowAllProjectsCommandText;
            });

            serverVM.Commands.Add(refreshContextualCommand);
            serverVM.Commands.Add(disconnectContextualCommand);
            serverVM.Commands.Add(browseServerContextualCommand);
            serverVM.Commands.Add(toggleShowAllProjectsCommand);
        }

        private void SetServerProjectsVMCommands(ServerViewModel serverVM)
        {
            foreach (ProjectViewModel projectVM in serverVM.Projects)
            {
                projectVM.Commands.Clear();

                if (this.Host.ActiveSection == null)
                {
                    // Don't add command (which will be disabled).
                    continue;
                }

                var bindContextCommand = new ContextualCommandViewModel(projectVM, this.Host.ActiveSection.BindCommand);
                bindContextCommand.SetDynamicDisplayText(x =>
                {
                    var ctx = x as ProjectViewModel;
                    Debug.Assert(ctx != null, "Unexpected fixed context for bind context command");
                    return ctx?.IsBound ?? false ? Strings.SyncButtonText : Strings.BindButtonText;
                });
                bindContextCommand.SetDynamicIcon(x =>
                {
                    var ctx = x as ProjectViewModel;
                    Debug.Assert(ctx != null, "Unexpected fixed context for bind context command");
                    return new IconViewModel(ctx?.IsBound ?? false ? KnownMonikers.Sync : KnownMonikers.Link);
                });

                var openProjectDashboardCommand = new ContextualCommandViewModel(projectVM, this.Host.ActiveSection.BrowseToProjectDashboardCommand)
                {
                    DisplayText = Strings.ViewInSonarQubeMenuItemDisplayText,
                    Tooltip = Strings.ViewInSonarQubeMenuItemTooltip,
                    Icon = new IconViewModel(KnownMonikers.OpenWebSite)
                };

                projectVM.Commands.Add(bindContextCommand);
                projectVM.Commands.Add(openProjectDashboardCommand);
            }
        }
        #endregion

        #region IDisposable Support
        private void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    this.ManagedState.PropertyChanged -= this.OnStatePropertyChanged;
                    this.DisposeConnections();
                }

                this.isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
