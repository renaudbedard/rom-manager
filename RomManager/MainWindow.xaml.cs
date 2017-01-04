using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace RomManager
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog
            {
                Title = "Choose ROM library location",
                IsFolderPicker = true,
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                AddToMostRecentlyUsedList = false,
                DefaultDirectory = AppDomain.CurrentDomain.BaseDirectory,
                EnsureFileExists = true,
                EnsurePathExists = true,
                EnsureReadOnly = false,
                EnsureValidNames = true,
                Multiselect = false,
                ShowPlacesList = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                var library = DataContext as GameLibrary;
                library.FolderPath = dialog.FileName;
                library.ToggleAnalyze();
            }
        }

        void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            var library = DataContext as GameLibrary;
            library.ToggleAnalyze();
        }

        void SortByRatingButton_Click(object sender, RoutedEventArgs e)
        {
            var direction = ListSortDirection.Ascending;
            if (GamesList.Items.SortDescriptions.Any(x => x.PropertyName == "MobyScore"))
            {
                var lastDirection = GamesList.Items.SortDescriptions.First(x => x.PropertyName == "MobyScore").Direction;
                direction = lastDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }

            GamesList.Items.SortDescriptions.Clear();
            GamesList.Items.SortDescriptions.Add(new SortDescription("MobyScore", direction));
        }
        void SortByNameButton_Click(object sender, RoutedEventArgs e)
        {
            var direction = ListSortDirection.Ascending;
            if (GamesList.Items.SortDescriptions.Any(x => x.PropertyName == "Title"))
            {
                var lastDirection = GamesList.Items.SortDescriptions.First(x => x.PropertyName == "Title").Direction;
                direction = lastDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }

            GamesList.Items.SortDescriptions.Clear();
            GamesList.Items.SortDescriptions.Add(new SortDescription("Title", direction));
        }
    }
}

