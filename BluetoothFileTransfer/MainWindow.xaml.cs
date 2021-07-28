using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using InTheHand.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace BluetoothFileTransfer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Dictionary<string, string> MyDeviceAddressNameComboBoxItems { get; set; } = new Dictionary<string, string>();
        private ObexListener ObexListener { get; set; }
        private BluetoothDeviceInfo SendToDeviceInfo { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            this.ObexListener = new ObexListener(ObexTransport.Bluetooth);

            BluetoothRadio[] btRadios = BluetoothRadio.AllRadios;
            Array.ForEach(btRadios, e => MyDeviceAddressNameComboBoxItems.Add(e.LocalAddress.ToString("C"), e.Name));

            this.ReceiveFileFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.Personal);

            string[] args = Environment.GetCommandLineArgs();
            switch (args.Length)
            {
                case 2:
                    this.ReceiveFileFolder.Text = args[1];
                    this.StartReceiveFile();
                    break;
                case 3:
                    var bluetoothDeviceInfo = this.GetBluetoothDeviceInfoByMacAddress(args[1]);
                    string filePath = args[2];
                    if (bluetoothDeviceInfo != null)
                    {
                        this.SendToDeviceInfo = bluetoothDeviceInfo;
                        if (File.Exists(filePath))
                        {
                            this.SendFile(filePath);
                        }
                    }
                    this.Close();
                    break;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.ObexListener.Stop();
        }

        private void SelectSendToDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SelectBluetoothDeviceDialog();
            dialog.ShowAuthenticated = true;
            dialog.ShowRemembered = true;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                this.SendToDeviceInfo = dialog.SelectedDevice;
                this.SendToDeviceName.Text = this.SendToDeviceInfo.DeviceName;
                this.SendToDeviceAddress.Text = this.SendToDeviceInfo.DeviceAddress.ToString("C");
                this.SendFileButton.IsEnabled = true;
            }
            else
            {
                this.SendToDeviceInfo = null;
                this.SendToDeviceName.Text = "Click to";
                this.SendToDeviceAddress.Text = "Select device";
                this.SendFileButton.IsEnabled = false;
            }
        }

        private void SendFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.SendToDeviceInfo == null) return;

            var dialog = new CommonOpenFileDialog();
            dialog.Title = "Select send file";
            if (dialog.ShowDialog() != CommonFileDialogResult.Ok) return;

            Task.Factory.StartNew(() => this.SendFile(dialog.FileName));
        }

        private void SendFileButton_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
            {
                var dropFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropFiles != null)
                {
                    var dropFile = dropFiles[0];
                    if (Path.GetExtension(dropFile) != string.Empty)
                    {
                        e.Effects = DragDropEffects.Copy;
                        e.Handled = true;
                        return;
                    }
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void SendFileButton_PreviewDrop(object sender, DragEventArgs e)
        {
            var dropFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropFiles == null) return;

            Task.Factory.StartNew(() => this.SendFile(dropFiles[0]));
        }

        private void BrowseReceiveFileFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            dialog.Title = "Select receive file folder";
            if (dialog.ShowDialog() != CommonFileDialogResult.Ok) return;

            this.ReceiveFileFolder.Text = dialog.FileName;
        }

        private void StartStopReceiveFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.ObexListener.IsListening)
            {
                this.StopReceiveFile();
            }
            else
            {
                this.StartReceiveFile();
            }
        }

        private BluetoothDeviceInfo GetBluetoothDeviceInfoByMacAddress(string _deviceMacAddress)
        {
            BluetoothRadio.PrimaryRadio.Mode = RadioMode.Connectable;
            BluetoothClient client = new BluetoothClient();
            var bluetoothDeviceInfos = client.DiscoverDevicesInRange();
            foreach (var bluetoothDeviceInfo in bluetoothDeviceInfos)
            {
                if (bluetoothDeviceInfo.DeviceAddress.ToString() == _deviceMacAddress.Replace(":", string.Empty))
                {
                    return bluetoothDeviceInfo;
                }
            }
            return null;
        }

        private void SendFile(string _filePath)
        {
            var fileName = Path.GetFileName(_filePath);
            this.AppendLogMessage("Send file requested | " + fileName);

            var uri = new Uri("obex://" + this.SendToDeviceInfo.DeviceAddress + "/" + fileName);
            var request = new ObexWebRequest(uri);
            request.ReadFile(_filePath);
            var response = (ObexWebResponse)request.GetResponse();
            response.Close();

            this.AppendLogMessage("File sent | " + fileName + " | " + new FileInfo(_filePath).Length + " bytes");
        }

        private void StartReceiveFile()
        {
            this.ObexListener.Start();
            Task.Factory.StartNew(() => this.ReceiveFile());
            this.StartStopReceiveFileButton.Content = " Stop ";
            this.AppendLogMessage("Receiver started");
        }

        private void StopReceiveFile()
        {
            this.ObexListener.Stop();
            this.StartStopReceiveFileButton.Content = " Start ";
            this.AppendLogMessage("Receiver stopped");
        }

        private void ReceiveFile()
        {
            while (this.ObexListener.IsListening)
            {
                ObexListenerContext context = this.ObexListener.GetContext();
                if (context == null) return;

                string directoryName = string.Empty;
                Application.Current.Dispatcher.Invoke((Action)(() => {
                    directoryName = this.ReceiveFileFolder.Text;
                }));
                if (!Directory.Exists(directoryName))
                {
                    context = null;
                    this.AppendLogMessage("Receive file folder not exist");
                    continue;
                }

                ObexListenerRequest request = context.Request;
                string fileUri = Uri.UnescapeDataString(request.RawUrl);
                string fileName = Path.GetFileName(fileUri);
                request.WriteFile(Path.Combine(directoryName, fileName));

                this.AppendLogMessage("File received | " + fileName + " | " + request.ContentLength64 + " bytes");
            }
        }

        private void AppendLogMessage(string _message)
        {
            Application.Current.Dispatcher.Invoke((Action)(() => {

                string currentLogMessage = this.LogMessage.Text;
                bool isFirstLine = string.IsNullOrEmpty(currentLogMessage);

                string appendMessage = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + _message;

                if (isFirstLine)
                {
                    this.LogMessage.Text = appendMessage;
                }
                else
                {
                    this.LogMessage.Text = appendMessage + Environment.NewLine + currentLogMessage;
                }

            }));
        }
    }
}
