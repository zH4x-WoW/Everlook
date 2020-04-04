﻿//
//  EverlookGameLoadingDialog.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Everlook.Explorer;
using FileTree.ProgressReporters;
using Gtk;

namespace Everlook.UI
{
    /// <summary>
    /// The main dialog that is shown when the program is loading games. External processes update the information
    /// on it.
    /// </summary>
    public partial class EverlookGameLoadingDialog : Dialog
    {
        /// <summary>
        /// Gets the source of the cancellation token associated with this dialog.
        /// </summary>
        public CancellationTokenSource CancellationSource { get; }

        private readonly List<string> Jokes = new List<string>();

        private readonly uint JokeTimeoutID;

        private readonly uint SecondaryProgressPulserTimeoutID;
        private bool IsPulserDisabled;

        /// <summary>
        /// Gets the notifier object which can be used to update the loading dialog with new information about the
        /// currently loading game.
        /// </summary>
        public IProgress<GameLoadingProgress> GameLoadProgressNotifier { get; }

        /// <summary>
        /// Gets the notifier object used for tracking how many games have been loaded.
        /// </summary>
        public IProgress<OverallLoadingProgress> OverallProgressNotifier { get; }

        /// <summary>
        /// The current overall loading progress.
        /// </summary>
        private OverallLoadingProgress CurrentLoadingProgress;

        /// <summary>
        /// Creates a new dialog with the given window as its parent.
        /// </summary>
        /// <param name="parent">The parent window.</param>
        /// <returns>An initialized instance of the EverlookGameLoadingDialog class.</returns>
        public static EverlookGameLoadingDialog Create(Window parent)
        {
            using (Builder builder = new Builder(null, "Everlook.interfaces.EverlookGameLoadingDialog.glade", null))
            {
                return new EverlookGameLoadingDialog(builder, builder.GetObject("GameLoadingDialog").Handle, parent);
            }
        }

        private EverlookGameLoadingDialog(Builder builder, IntPtr handle, Window parent)
            : base(handle)
        {
            builder.Autoconnect(this);
            this.TransientFor = parent;

            this.CancellationSource = new CancellationTokenSource();

            this.CancelGameLoadingButton.Pressed += (o, args) =>
            {
                this.GameLoadingDialogLabel.Text = "Cancelling...";
                this.CancelGameLoadingButton.Sensitive = false;

                this.CancellationSource.Cancel();
            };

            this.OverallProgressNotifier = new Progress<OverallLoadingProgress>(overallProgress =>
            {
                this.CurrentLoadingProgress = overallProgress;
            });

            this.GameLoadProgressNotifier = new Progress<GameLoadingProgress>(loadingProgress =>
            {
                SetFraction(loadingProgress.CompletionPercentage);

                string statusText = string.Empty;
                switch (loadingProgress.State)
                {
                    case GameLoadingState.SettingUp:
                    {
                        statusText = "Setting up...";
                        break;
                    }
                    case GameLoadingState.Loading:
                    {
                        statusText = "Loading...";
                        break;
                    }
                    case GameLoadingState.LoadingPackages:
                    {
                        statusText = "Loading packages...";
                        break;
                    }
                    case GameLoadingState.LoadingNodeTree:
                    {
                        statusText = "Loading node tree...";
                        break;
                    }
                    case GameLoadingState.LoadingDictionary:
                    {
                        statusText = "Loading dictionary for node generation...";
                        break;
                    }
                    case GameLoadingState.BuildingNodeTree:
                    {
                        statusText = "Building node tree..";
                        break;
                    }
                }

                SetStatusMessage($"{loadingProgress.Alias} - {statusText}");

                if (!(loadingProgress.NodesCreationProgress is null))
                {
                    DisplayNodeCreationProgress(loadingProgress.CurrentPackage, loadingProgress.NodesCreationProgress);

                    this.IsPulserDisabled = true;
                }
                else if (!(loadingProgress.OptimizationProgress is null))
                {
                    DisplayTreeOptimizationProgress(loadingProgress.OptimizationProgress);

                    this.IsPulserDisabled = true;
                }
                else
                {
                    this.IsPulserDisabled = false;
                }
            });

            using (Stream shaderStream =
                Assembly.GetExecutingAssembly().GetManifestResourceStream("Everlook.Content.jokes.txt"))
            {
                if (shaderStream == null)
                {
                    return;
                }

                using (StreamReader sr = new StreamReader(shaderStream))
                {
                    while (sr.BaseStream.Length > sr.BaseStream.Position)
                    {
                        // Add italics to all jokes. Jokes are in Pango markup format
                        this.Jokes.Add($"<i>{sr.ReadLine()}</i>");
                    }
                }
            }

            RefreshJoke();

            this.JokeTimeoutID = GLib.Timeout.Add(6000, () =>
            {
                RefreshJoke();
                return true;
            });

            this.SecondaryProgressPulserTimeoutID = GLib.Timeout.Add(300, () =>
            {
                if (!this.IsPulserDisabled)
                {
                    this.TreeBuildingProgressBar.Pulse();
                }

                return true;
            });
        }

        /// <summary>
        /// Displays the current progress of tree optimization for the current tree.
        /// </summary>
        /// <param name="optimizationProgress">The progress information.</param>
        private void DisplayTreeOptimizationProgress(TreeOptimizationProgress optimizationProgress)
        {
            if (optimizationProgress.OptimizedNodes < optimizationProgress.NodeCount)
            {
                this.TreeBuildingProgressBar.Fraction =
                    (float)optimizationProgress.OptimizedNodes / optimizationProgress.NodeCount;

                this.TreeBuildingProgressBar.Text = "Optimizing node names...";
            }
            else
            {
                this.TreeBuildingProgressBar.Fraction =
                    (float)optimizationProgress.TracedNodes / optimizationProgress.NodeCount;

                this.TreeBuildingProgressBar.Text = "Applying file type traces...";
            }
        }

        /// <summary>
        /// Displays the current progress of node creation for the current package.
        /// </summary>
        /// <param name="currentPackage">The package.</param>
        /// <param name="nodesCreationProgress">The progress information.</param>
        private void DisplayNodeCreationProgress
        (
            string currentPackage,
            PackageNodesCreationProgress nodesCreationProgress
        )
        {
            this.TreeBuildingProgressBar.Fraction =
                (float)nodesCreationProgress.CompletedPaths / nodesCreationProgress.PathCount;

            this.TreeBuildingProgressBar.Text = $"Building nodes from paths in {currentPackage}...";
        }

        /// <summary>
        /// Picks a new random joke to display, and replaces the current one.
        /// </summary>
        private void RefreshJoke()
        {
            this.AdditionalInfoLabel.Markup = this.Jokes[new Random().Next(this.Jokes.Count)];
        }

        /// <summary>
        /// Sets the fraction of the dialog's loading bar.
        /// </summary>
        /// <param name="fraction">The fraction of the loading bar which is filled.</param>
        private void SetFraction(double fraction)
        {
            this.GameLoadingProgressBar.Fraction = fraction;
        }

        /// <summary>
        /// Sets the status message of the dialog.
        /// </summary>
        /// <param name="statusMessage">The status message to set.</param>
        private void SetStatusMessage(string statusMessage)
        {
            this.GameLoadingDialogLabel.Text = $"({this.CurrentLoadingProgress.FinishedOperations}/{this.CurrentLoadingProgress.OperationCount}) {statusMessage}";
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            base.Destroy();

            GLib.Timeout.Remove(this.JokeTimeoutID);
            GLib.Timeout.Remove(this.SecondaryProgressPulserTimeoutID);
        }
    }
}
