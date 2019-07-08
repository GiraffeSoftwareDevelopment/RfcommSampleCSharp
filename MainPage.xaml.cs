using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace RfcommSampleCSharp
{
    public sealed partial class MainPage : Page
    {
        private StreamSocket socket;
        private DataWriter writer;
        private RfcommServiceProvider rfcommProvider;
        private StreamSocketListener socketListener;
        private static readonly Guid RfcommChatServiceUuid = Guid.Parse("4E989E9F-F81A-4EE8-B9A7-BB74D5CD3C96");
        // The SDP Type of the Service Name SDP attribute.
        // The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        //    -  the Attribute Type size in the least significant 3 bits,
        //    -  the SDP Attribute Type value in the most significant 5 bits.
        private const byte SdpServiceNameAttributeType = (4 << 3) | 5;
        // The value of the Service Name SDP attribute
        private const string SdpServiceName = "bluetooth rfcomm sample";
        // The Id of the Service Name SDP attribute
        private const UInt16 SdpServiceNameAttributeId = 0x100;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void Button_server_start_Click(object sender, RoutedEventArgs e)
        {
            InitializeRfcommServer();
        }

        private void Button_server_disconnect_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
            TextBox_log.Text += ("Disconnected.\n");
        }

        private void Button_send_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }
        public void KeyboardKey_Pressed(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                SendMessage();
            }
        }
        private async void SendMessage()
        {
            if (TextBox_message.Text.Length != 0)
            {
                if (socket != null)
                {
                    string message = TextBox_message.Text;
                    writer.WriteString(message);
                    TextBox_log.Text += string.Format("Sent : {0}\n", message);
                    TextBox_message.Text = "";
                    await writer.StoreAsync();
                }
                else
                {
                    TextBox_log.Text += ("No clients connected, please wait for a client to connect before attempting to send a message");
                }
            }
        }
        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider)
        {
            var sdpWriter = new DataWriter();
            // Write the Service Name Attribute.
            sdpWriter.WriteByte(SdpServiceNameAttributeType);
            // The length of the UTF-8 encoded Service Name SDP Attribute.
            sdpWriter.WriteByte((byte)SdpServiceName.Length);
            // The UTF-8 encoded Service Name value.
            sdpWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            sdpWriter.WriteString(SdpServiceName);
            // Set the SDP Attribute on the RFCOMM Service Provider.
            rfcommProvider.SdpRawAttributes.Add(SdpServiceNameAttributeId, sdpWriter.DetachBuffer());
        }
        private async void InitializeRfcommServer()
        {
            Button_server_start.IsEnabled = false;
            Button_server_disconnect.IsEnabled = true;
            try
            {
                rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(RfcommChatServiceUuid));
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x800710DF)
            {
                // Catch exception HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE).
                TextBox_log.Text += string.Format("Make sure your Bluetooth Radio is on : {0}\n", ex.Message);
                Button_server_start.IsEnabled = true;
                Button_server_disconnect.IsEnabled = false;
                return;
            }
            socketListener = new StreamSocketListener();
            socketListener.ConnectionReceived += OnConnectionReceived;
            var rfcomm = rfcommProvider.ServiceId.AsString();
            await socketListener.BindServiceNameAsync(rfcommProvider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            // Set the SDP attributes and start Bluetooth advertising
            InitializeServiceSdpAttributes(rfcommProvider);
            try
            {
                rfcommProvider.StartAdvertising(socketListener, true);
            }
            catch (Exception e)
            {
                TextBox_log.Text += string.Format("InitializeRfcommServer : Exception occured : {0}\n", e.Message);
                Button_server_start.IsEnabled = true;
                Button_server_disconnect.IsEnabled = false;
                return;
            }
            TextBox_log.Text += ("Listening for incoming connections\n");
        }
        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            socketListener.Dispose();
            socketListener = null;
            try
            {
                socket = args.Socket;
            }
            catch (Exception e)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    TextBox_log.Text += string.Format("OnConnectionReceived : Exception occured : {0}\n", e.Message);
                });
                Disconnect();
                return;
            }
            var remoteDevice = await BluetoothDevice.FromHostNameAsync(socket.Information.RemoteHostName);
            writer = new DataWriter(socket.OutputStream);
            var reader = new DataReader(socket.InputStream);
            bool remoteDisconnection = false;
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                TextBox_log.Text += string.Format("Connected to Client : {0}\n", remoteDevice.Name);
            });
            string buffer = "";
            while (true)
            {
                try
                {
                    uint readLength = await reader.LoadAsync(1);
                    if (readLength < 1)
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    string dat = reader.ReadString(1);
                    if ("\n" == dat)
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            TextBox_log.Text += string.Format("Received : {0} : {1}\n", remoteDevice.Name, buffer);
                        });
                        buffer = "";
                    }
                    else
                    {
                        buffer += dat;
                    }
                }
                // Catch exception HRESULT_FROM_WIN32(ERROR_OPERATION_ABORTED).
                catch (Exception ex) when ((uint)ex.HResult == 0x800703E3)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        TextBox_log.Text += ("Client Disconnected Successfully\n");
                    });
                    break;
                }
            }
            reader.DetachStream();
            if (remoteDisconnection)
            {
                Disconnect();
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    TextBox_log.Text += ("Client disconnected\n");
                });
            }
        }
        private async void Disconnect()
        {
            if (rfcommProvider != null)
            {
                rfcommProvider.StopAdvertising();
                rfcommProvider = null;
            }

            if (socketListener != null)
            {
                socketListener.Dispose();
                socketListener = null;
            }

            if (writer != null)
            {
                writer.DetachStream();
                writer = null;
            }

            if (socket != null)
            {
                socket.Dispose();
                socket = null;
            }
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Button_server_start.IsEnabled = true;
                Button_server_disconnect.IsEnabled = false;
            });
        }
    }
}
