namespace NicFwBtCore;

public partial class ChannelEditor : ContentPage
{
    private static int currentChannel = 0;
	public ChannelEditor()
	{
		InitializeComponent();
        for (int i = 1; i < 199; i++)
            ChannelNum.Items.Add(i.ToString("000"));
        ChannelNum.SelectedIndex = currentChannel;
        RxCTS.SelectedIndex = 
            TxCTS.SelectedIndex =
            RxDCS.SelectedIndex =
            TxDCS.SelectedIndex = 0;
        foreach(string cts in SubTones.CTS)
        {
            RxCTS.Items.Add(cts);
            TxCTS.Items.Add(cts);
        }
        foreach(string dcs in SubTones.DCS)
        {
            RxDCS.Items.Add($"{dcs}N");
            TxDCS.Items.Add($"{dcs}N");
        }
        foreach (string dcs in SubTones.DCS)
        {
            RxDCS.Items.Add($"{dcs}I");
            TxDCS.Items.Add($"{dcs}I");
        }
    }

    private async void Button_Clicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void ChannelNum_SelectedIndexChanged(object sender, EventArgs e)
    {
        currentChannel = ChannelNum.SelectedIndex;
        RefreshFields();
    }

    private void RefreshFields()
    {
        propagation = false;
        Channel chan = Channel.Get(currentChannel + 1);
        double rx = chan.RxFrequency;
        Name.Text = chan.Name;
        RXFreq.Text = rx.ToString("F5");
        TXFreq.Text = chan.TxFrequency.ToString("F5");
        RXTone.Text = chan.RxTone;
        TXTone.Text = chan.TxTone;
        TXPower.Text = chan.TxPower.ToString();
        Groups.Text = chan.Groups;
        Modulation.SelectedItem = chan.Modulation;
        Bandwidth.SelectedItem = chan.Bandwidth;
        Active.IsChecked = chan.Active;
        propagation = true;        
    }

    private bool propagation = true;
    private void UpdateChannel(bool rx)
    {
        Channel chan = Channel.Get(currentChannel + 1);
        if (Active.IsChecked)
        {
            if (!chan.Active)
            {
                chan.Active = true;
                chan.TxPower = 254;
                chan.Name = $"Channel {currentChannel + 1}";
                chan.RxTone = chan.TxTone = "Off";
                chan.Groups = string.Empty;
                chan.Modulation = "Auto";
                chan.Bandwidth = "Wide";
            }
            else
            {
                chan.Name = Name.Text;
                double offset = chan.TxFrequency - chan.RxFrequency;
                chan.RxFrequency = (double.TryParse(RXFreq.Text, out double d) ? d : chan.RxFrequency).Clamp(18, 1300);
                if (rx)
                    chan.TxFrequency = chan.RxFrequency + offset;
                else
                    chan.TxFrequency = (double.TryParse(TXFreq.Text, out d) ? d : chan.TxFrequency).Clamp(18, 1300);
                chan.RxTone = RXTone.Text;
                chan.TxTone = TXTone.Text;
                int txpwr = (int.TryParse(TXPower.Text, out int i) ? i : chan.TxPower).Clamp(0, 254);
                chan.TxPower = txpwr;
                chan.Groups = Groups.Text;
                chan.Modulation = Modulation.SelectedItem.ToString() ?? "Auto";
                chan.Bandwidth = Bandwidth.SelectedItem.ToString() ?? "Wide";
            }
        }
        else
        {
            chan.Active = false;
        }
    }

    private void Value_Changed(object sender, EventArgs e)
    {
        if (propagation)
        {
            UpdateChannel(sender==RXFreq);
            RefreshFields();
        }
    }

    private void ChannelDown_Clicked(object sender, EventArgs e)
    {
        int i = ChannelNum.SelectedIndex - 1;
        ChannelNum.SelectedIndex = i.Clamp(0, 197);
    }

    private void ChannelUp_Clicked(object sender, EventArgs e)
    {
        int i = ChannelNum.SelectedIndex + 1;
        ChannelNum.SelectedIndex = i.Clamp(0, 197);
    }

    private void Subtone_SelectedIndexChanged(object sender, EventArgs e)
    {
        if(sender is Picker picker)
        {
            if (picker.SelectedIndex > 0)
            {
                if (sender == RxCTS || sender == RxDCS)
                    RXTone.Text = picker.SelectedItem.ToString();
                if (sender == TxCTS || sender == TxDCS)
                    TXTone.Text = picker.SelectedItem.ToString();
                picker.SelectedIndex = 0;
                UpdateChannel(false);
            }
        }

    }
}