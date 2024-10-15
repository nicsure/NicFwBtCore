using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Extensions;
using Plugin.BLE.Extensions;
using System;
using System.Text;

namespace NicFwBtCore
{
    public partial class MainPage : ContentPage
    {
        private readonly List<IDevice> discoveredDevices = [];
        private readonly Queue<byte> input = [];
        private ICharacteristic? reader = null, writer = null;
        private IDevice? tdh3 = null;

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
            ChannelRead.IsEnabled = false;
            ChannelWrite.IsEnabled = false;
            StartScan.IsEnabled = false;
            Activity.IsRunning = true;
            ChannelView.IsEnabled = false;
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
            ChannelView.IsEnabled = true;
            adapter.DeviceDiscovered -= Adapter_DeviceDiscovered;
        }

        private void Adapter_DeviceDiscovered(object? sender, DeviceEventArgs e)
        {
            discoveredDevices.Add(e.Device);
        }

        private void Devices_SelectedIndexChanged(object sender, EventArgs e)
        {
            ChannelRead.IsEnabled = Devices.SelectedIndex >= 0;
            ChannelWrite.IsEnabled = Devices.SelectedIndex >= 0;
        }

        private async void ChannelRead_Clicked(object sender, EventArgs e)
        {
            if(await Bluetooth_Connect2())
            {
                await ReadChannels();
            }
        }

        private async void ChannelWrite_Clicked(object sender, EventArgs e)
        {
            if (await Bluetooth_Connect2())
            {
                await WriteChannels();
            }
        }

        private async Task<bool> Bluetooth_Connect2()
        {
            ChannelRead.IsEnabled = false;
            ChannelWrite.IsEnabled = false;
            StartScan.IsEnabled = false;
            Activity.IsRunning = true;
            ChannelView.IsEnabled = false;
            Devices.IsEnabled = false;
            Status.Text = $"Connecting to {Devices.SelectedItem}.";
            string? failure = null;
            var adapter = CrossBluetoothLE.Current.Adapter;
            tdh3 = discoveredDevices[Devices.SelectedIndex];
            try
            {
                await adapter.ConnectToDeviceAsync(tdh3);
                Status.Text = "Enumerating Services";
                var services = await tdh3.GetServicesAsync();
                Status.Text = "Enumerating Characteristics";
                reader = null;
                writer = null;
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
                    failure = "Unable to negotiate communication.";
                else
                {
                    input.Clear();
                    reader.ValueUpdated += (s, e) =>
                    {
                        foreach (byte b in e.Characteristic.Value)
                            input.Enqueue(b);
                    };
                    await reader.StartUpdatesAsync();                    
                    await writer.WriteAsync([0x45]);
                    if (await GetByte() != 0x45)
                    {
                        failure = "No Handshake ACK.";
                    }
                }
            }
            catch
            {
                failure = $"Connection to {Devices.SelectedItem} failed.";
            }
            if(failure != null)
            {
                try
                {
                    await adapter.DisconnectDeviceAsync(tdh3);
                }
                catch { }
                Status.Text = failure;
                ChannelRead.IsEnabled = true;
                ChannelWrite.IsEnabled = true;
                StartScan.IsEnabled = true;
                Activity.IsRunning = false;
                ChannelView.IsEnabled = true;
                Devices.IsEnabled = true;
                return false;
            }
            return true;
        }

        private async Task WriteChannels()
        {
            var adapter = CrossBluetoothLE.Current.Adapter;
            string? failure = null;
            if (reader != null && writer != null && tdh3 != null)
            {
                for (int chan = 1; chan < 199; chan++)
                {
                    Status.Text = $"Writing Channel {chan}.";
                    Channel channel = Channel.Get(chan);
                    try
                    {
                        writer.WriteType = Plugin.BLE.Abstractions.CharacteristicWriteType.WithoutResponse;
                        await writer.WriteAsync([0x31]);
                        await writer.WriteAsync([(byte)(chan + 1)]);
                        byte cs = 0;
                        foreach (byte byt in channel.Data)
                        {
                            await writer.WriteAsync([byt]);
                            cs += byt;
                        }
                        await writer.WriteAsync([cs]);
                        int ack = await GetByte();
                        if (ack != 0x31)
                        {
                            failure = "No Block Write ACK.";
                            break;
                        }
                    }
                    catch
                    {
                        failure = "Communication exception";
                        break;
                    }
                }
                try { await writer.WriteAsync([0x49]); } catch { }
            }
            else
                failure = "Some crazy crap occurred, apologies.";
            Status.Text = failure ?? "Done";
            try
            {
                await adapter.DisconnectDeviceAsync(tdh3);
            }
            catch { }
            ChannelRead.IsEnabled = true;
            ChannelWrite.IsEnabled = true;
            StartScan.IsEnabled = true;
            Activity.IsRunning = false;
            ChannelView.IsEnabled = true;
            Devices.IsEnabled = true;

        }

        private async Task ReadChannels()
        {
            var adapter = CrossBluetoothLE.Current.Adapter;
            string? failure = null;
            if (reader != null && writer != null && tdh3 != null)
            {
                for (int chan = 1; chan < 199; chan++)
                {
                    Status.Text = $"Reading Channel {chan}.";
                    try
                    {
                        await writer.WriteAsync([0x30, (byte)(chan + 1)]);
                    }
                    catch 
                    {
                        failure = "Communication exception";
                        break;
                    }
                    if (await GetByte() != 0x30)
                    {
                        failure = "No Block Read ACK.";
                        break;
                    }
                    byte[] channelData = Channel.Get(chan).Data;
                    byte cs = 0;
                    for (int i = 0; i < channelData.Length; i++)
                    {
                        int b = await GetByte();
                        if (b == -1)
                        {
                            failure = "Timeout during channel read.";
                            break;
                        }
                        cs += (byte)b;
                        channelData[i] = (byte)b;
                    }
                    if (failure != null) break;
                    if (await GetByte() != cs)
                    {
                        failure = "Bad checksum in channel data.";
                        break;
                    }
                }
                try
                {
                    await writer.WriteAsync([0x46]);
                    if (await GetByte() != 0x46)
                        failure = "No End Handshake ACK.";
                }
                catch { }
            }
            else
                failure = "Some goofy shit happened, sorry.";
            Status.Text = failure ?? "Done";
            try
            {
                await adapter.DisconnectDeviceAsync(tdh3);
            }
            catch { }
            ChannelRead.IsEnabled = true;
            ChannelWrite.IsEnabled = true;
            StartScan.IsEnabled = true;
            Activity.IsRunning = false;
            ChannelView.IsEnabled = true;
            Devices.IsEnabled = true;
        }

        private async void ChannelView_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ChannelEditor());
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
