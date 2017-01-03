using Microsoft.WindowsAPICodePack.Dialogs;
using System;
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
    }
}

