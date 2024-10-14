using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Extensions;
using Plugin.BLE.Extensions;
using System.Text;

namespace NicFwBtCore
{
    public partial class MainPage : ContentPage
    {
        private readonly List<IDevice> discoveredDevices = [];
        private readonly Queue<byte> input = [];

        public MainPage()
        {
            InitializeComponent();
            RequestPermissionsAsync();
        }

        private async void RequestPermissionsAsync()
        {
            if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            {
                var locationStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                var bluetoothStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
                if (locationStatus != PermissionStatus.Granted)
                {
                    locationStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (bluetoothStatus != PermissionStatus.Granted)
                {
                    bluetoothStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
                }
                if (locationStatus == PermissionStatus.Granted && bluetoothStatus == PermissionStatus.Granted)
                {
                    StartScan.IsEnabled = true;
                }
                else
                {
                    await DisplayAlert("Permissions Required", "Location and Bluetooth permissions are required for this app.", "OK");
                }
            }
            else
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {
                StartScan.IsEnabled = true;
            }
            else
                await DisplayAlert("What is this crap?", "Get a proper phone/computer.", "OK");
        }

        private void Button_Clicked(object sender, EventArgs e)
        {
            _ = Bluetooth_Scan();
        }

        private async Task Bluetooth_Scan()
        {
            ConnectButton.IsEnabled = false;
            StartScan.IsEnabled = false;
            Activity.IsRunning = true;
            Devices.IsEnabled = false;
            Devices.Items.Clear();
            foreach(var device in discoveredDevices)
            {
                using (device) { }
            }
            discoveredDevices.Clear();
            Status.Text = "Scanning...";
            var adapter = CrossBluetoothLE.Current.Adapter;
            adapter.DeviceDiscovered += Adapter_DeviceDiscovered;
            await adapter.StartScanningForDevicesAsync();
            Activity.IsRunning = false;
            Status.Text = $"Scan finished. {discoveredDevices.Count} devices discovered.";
            foreach (var device in discoveredDevices)
            {
                Devices.Items.Add(device.Name);
            }
            if (discoveredDevices.Count > 0)
            {
                Devices.IsEnabled = true;
                Devices.SelectedIndex = 0;
            }
            StartScan.IsEnabled = true;
            adapter.DeviceDiscovered -= Adapter_DeviceDiscovered;
        }

        private void Adapter_DeviceDiscovered(object? sender, DeviceEventArgs e)
        {
            discoveredDevices.Add(e.Device);
        }

        private void Devices_SelectedIndexChanged(object sender, EventArgs e)
        {
            ConnectButton.IsEnabled = Devices.SelectedIndex >= 0;
        }

        private void ConnectButton_Clicked(object sender, EventArgs e)
        {
            _ = Bluetooth_Connect();
        }

        private async Task Bluetooth_Connect()
        {
            ChannelInfo.Text = string.Empty;
            ConnectButton.IsEnabled = false;
            StartScan.IsEnabled = false;
            Activity.IsRunning = true;
            Devices.IsEnabled = false;
            Status.Text = $"Connecting to {Devices.SelectedItem}.";
            var adapter = CrossBluetoothLE.Current.Adapter;
            var tdh3 = discoveredDevices[Devices.SelectedIndex];
            try
            {
                await adapter.ConnectToDeviceAsync(tdh3);
                Status.Text = "Enumerating Services";
                var services = await tdh3.GetServicesAsync();
                Status.Text = "Enumerating Characteristics";
                ICharacteristic? reader = null, writer = null;
                foreach (var service in services)
                {
                    if (service.Id.PartialFromUuid().ToLower().Equals("0xff00"))
                    {
                        var characteristics = await service.GetCharacteristicsAsync();
                        foreach (var characteristic in characteristics)
                        {
                            if (characteristic.CanUpdate)
                            {
                                reader = characteristic;
                                if (writer != null) break;
                            }
                            else
                            if (characteristic.CanWrite)
                            {
                                writer = characteristic;
                                if (reader != null) break;
                            }
                        }
                        break;
                    }
                }
                if (reader == null || writer == null)
                    Status.Text = "Unable to negotiate communication.";
                else
                {
                    input.Clear();
                    Status.Text = "Reading Channel 1.";
                    reader.ValueUpdated += (s, e) => 
                    {
                        foreach(byte b in e.Characteristic.Value)
                            input.Enqueue(b);
                    };
                    await reader.StartUpdatesAsync();
                    string? failure = null;                    
                    do
                    {
                        await writer.WriteAsync([0x45]);
                        if (await GetByte() != 0x45)
                        {
                            failure = "No Handshake ACK.";
                            break;
                        }
                        await writer.WriteAsync([0x30, 0x02]);
                        if (await GetByte() != 0x30)
                        {
                            failure = "No Block Read ACK.";
                            break;
                        }
                        byte[] channel1 = new byte[32];
                        byte cs = 0;
                        for (int i = 0; i < channel1.Length; i++)
                        {
                            int b = await GetByte();
                            if (b == -1)
                            {
                                failure = "Timeout during channel read.";
                                break;
                            }
                            cs += (byte)b;
                            channel1[i] = (byte)b;
                        }
                        if (failure != null) break;
                        if (await GetByte() != cs)
                        {
                            failure = "Bad checksum in channel data.";
                            break;
                        }
                        int rx = BitConverter.ToInt32(channel1, 0);
                        int tx = BitConverter.ToInt32(channel1, 4);
                        int rxst = BitConverter.ToUInt16(channel1, 8);
                        int txst = BitConverter.ToUInt16(channel1, 10);
                        int txpwr = channel1[12];
                        int groupw = BitConverter.ToUInt16(channel1, 13);
                        int modbw = channel1[15];
                        string name = Encoding.ASCII.GetString(channel1, 20, 12).Trim('\0');
                        ChannelInfo.Text =
                            $"Channel 001\r\n\r\n" +
                            $"RX Freq : {rx / 100000.0:F5}\r\n" +
                            $"TX Freq : {tx / 100000.0:F5}\r\n" +
                            $"RX Subtone : {SubTone(rxst)}\r\n" +
                            $"TX Subtone : {SubTone(txst)}\r\n" +
                            $"TX Power : {txpwr}\r\n" +
                            $"Groups : {Groups(groupw)}\r\n" +
                            $"Modulation : {Modulation((modbw >> 1) & 3)}\r\n" +
                            $"Bandwidth : {((modbw & 1) == 0 ? "Wide" : "Narrow")}\r\n" +
                            $"Name : {name}";
                    }
                    while (false);
                    if (failure != null)
                        Status.Text = failure;
                }

            }
            catch 
            {
                Status.Text = $"Connection to {Devices.SelectedItem} failed.";
            }
            try
            {
                Status.Text = "Completed. Disconnecting...";
                await adapter.DisconnectDeviceAsync(tdh3);
                Status.Text = "Disconnected.";
            }
            catch { }
            ConnectButton.IsEnabled = true;
            StartScan.IsEnabled = true;
            Activity.IsRunning = false;
            Devices.IsEnabled = true;
        }

        private static string Groups(int groupw)
        {
            string s = string.Empty;
            for (int i = 0; i < 4; i++)
            {
                int nyb = groupw & 0xf;
                groupw >>= 4;
                if (nyb == 0)
                    s += "-";
                else
                    s += (char)(nyb + 64);
            }
            return s;
        }

        private static string Modulation(int mod)
        {
            return mod switch
            {
                0 => "Auto",
                1 => "FM",
                2 => "AM",
                3 => "USB",
                _ => "Fook Knows",
            };
        }

        private static string SubTone(int st)
        {
            if (st == 0) return "Off";
            if (st < 0x8000) return $"{st / 10.0:F1}";
            return $"D{Convert.ToString(st & 0x3fff, 8).PadLeft(3, '0')}{((st & 0x4000) == 0 ? 'N' : 'I')}";
        }

        private async Task<int> GetByte()
        {
            int to = 0;
            while(input.Count == 0)
            {
                await Task.Delay(10);
                if (to++ > 200) return -1;
            }
            return input.Dequeue();
        }

    }


}
