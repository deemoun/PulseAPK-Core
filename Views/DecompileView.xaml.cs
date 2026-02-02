using System.Windows;
using System.Windows.Controls;
using PulseAPK.Utils;

namespace PulseAPK.Views
{
    public partial class DecompileView : UserControl
    {
        public DecompileView()
        {
            InitializeComponent();
        }

        private void Border_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void Border_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void Border_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var file = files[0];
                    var (isValid, message) = Utils.FileSanitizer.ValidateApk(file);

                    if (isValid)
                    {
                        if (DataContext is ViewModels.DecompileViewModel vm)
                        {
                            vm.ApkPath = file;
                        }
                    }
                    else
                    {
                        MessageBoxUtils.ShowError(message, "Invalid APK File");
                    }
                }
            }

            e.Handled = true;
        }
    }
}
