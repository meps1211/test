using System;
using System.Windows.Forms;
using Ozeki.Media;
using Ozeki.VoIP;

namespace _14_Call_Assistant
{
    /// <summary>
    /// 
    /// </summary>
    public partial class FormCallAssistant : Form
    {
        ISoftPhone _softPhone;
        IPhoneLine _phoneLine;
        RegState _phoneLineState;
        IPhoneCall _call;

        Microphone _microphone;
        Speaker _speaker;
        MediaConnector _connector;
        PhoneCallAudioSender _mediaSender;
        PhoneCallAudioReceiver _mediaReceiver;

        DatabaseManager _databaseManager;

        UserInfo _otherParty;

        bool _incomingCall;

        public FormCallAssistant()
        {
            InitializeComponent();
        }

        void form_CallAssistant_Load(object sender, EventArgs e)
        {
            _microphone = Microphone.GetDefaultDevice();
            _speaker = Speaker.GetDefaultDevice();
            _connector = new MediaConnector();
            _mediaSender = new PhoneCallAudioSender();
            _mediaReceiver = new PhoneCallAudioReceiver();

            _databaseManager = new DatabaseManager();

            InitializeSoftphone();
        }

        void InitializeSoftphone()
        {
            try
            {
                _softPhone = SoftPhoneFactory.CreateSoftPhone(SoftPhoneFactory.GetLocalIP(), 5700, 5750);
                SIPAccount sa = new SIPAccount(true, "1000", "1000", "1000", "1000", "192.168.115.100", 5060);

                _phoneLine = _softPhone.CreatePhoneLine(sa);
                _phoneLine.RegistrationStateChanged += _phoneLine_RegistrationStateChanged;

                _softPhone.IncomingCall += _softPhone_IncomingCall;

                _softPhone.RegisterPhoneLine(_phoneLine);

                _incomingCall = false;

                ConnectMedia();
            }
            catch (Exception ex)
            {
                InvokeGUIThread(() => { tb_Display.Text = ex.Message; });
            }
        }

        void _phoneLine_RegistrationStateChanged(object sender, RegistrationStateChangedArgs e)
        {
            _phoneLineState = e.State;

            if (_phoneLineState == RegState.RegistrationSucceeded)
            {
                InvokeGUIThread(() => { lbl_UserName.Text = _phoneLine.SIPAccount.UserName; });
                InvokeGUIThread(() => { lbl_DomainHost.Text = _phoneLine.SIPAccount.DomainServerHost; });
            }
        }

        void _softPhone_IncomingCall(object sender, VoIPEventArgs<IPhoneCall> e)
        {
            var userName = e.Item.DialInfo.Dialed;
            InvokeGUIThread(() => { tb_Display.Text = "Ringing (" + userName + ")"; });

            _call = e.Item;
            WireUpCallEvents();
            _incomingCall = true;

            _otherParty = _databaseManager.GetOtherPartyInfos(userName);
            ShowUserInfos(_otherParty);
        }

        void call_CallStateChanged(object sender, CallStateChangedArgs e)
        {
            InvokeGUIThread(() => { lbl_CallState.Text = e.State.ToString(); });

            if (e.State == CallState.Answered)
            {
                StartDevices();
                _mediaSender.AttachToCall(_call);
                _mediaReceiver.AttachToCall(_call);

                InvokeGUIThread(() => { tb_Display.Text = "In call with: " + ((IPhoneCall)sender).DialInfo.Dialed; });
            }
            else if (e.State == CallState.InCall)
            {
                StartDevices();
            }

            if (e.State == CallState.LocalHeld || e.State == CallState.InactiveHeld)
            {
                StopDevices();
                InvokeGUIThread(() => { btn_Hold.Text = "Unhold"; });
            }
            else
            {
                InvokeGUIThread(() => { btn_Hold.Text = "Hold"; });
            }

            if (e.State.IsCallEnded())
            {
                StopDevices();

                _mediaSender.Detach();
                _mediaReceiver.Detach();

                WireDownCallEvents();

                _call = null;

                InvokeGUIThread(() => { tb_Display.Text = String.Empty; });
                ClearUserInfos();
            }
        }

        void ShowUserInfos(UserInfo otherParty)
        {
            InvokeGUIThread(() =>
                {
                    tb_OtherPartyUserName.Text = otherParty.UserName;
                    tb_OtherPartyRealName.Text = otherParty.RealName;
                    tb_OtherPartyCountry.Text = otherParty.Country;
                    tb_OtherPartyNote.Text = otherParty.Note;
                });
        }

        void ClearUserInfos()
        {
            InvokeGUIThread(() =>
            {
                tb_OtherPartyUserName.Text = String.Empty;
                tb_OtherPartyRealName.Text = String.Empty;
                tb_OtherPartyCountry.Text = String.Empty;
                tb_OtherPartyNote.Text = String.Empty;
            });
        }

        void buttonKeyPadButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;

            if (_call != null)
                return;

            if (btn == null)
                return;

            tb_Display.Text += btn.Text.Trim();
        }

        void StartDevices()
        {
            if (_microphone != null)
                _microphone.Start();

            if (_speaker != null)
                _speaker.Start();
        }

        void StopDevices()
        {
            if (_microphone != null)
                _microphone.Stop();

            if (_speaker != null)
                _speaker.Stop();
        }

        void ConnectMedia()
        {
            if (_microphone != null)
                _connector.Connect(_microphone, _mediaSender);

            if (_speaker != null)
                _connector.Connect(_mediaReceiver, _speaker);
        }

        void WireUpCallEvents()
        {
            _call.CallStateChanged += (call_CallStateChanged);
        }

        void WireDownCallEvents()
        {
            _call.CallStateChanged -= (call_CallStateChanged);
        }

        void InvokeGUIThread(Action action)
        {
            Invoke(action);
        }

        void btn_PickUp_Click(object sender, EventArgs e)
        {
            if (_incomingCall)
            {
                _incomingCall = false;
                _call.Answer();

                return;
            }

            if (_call != null)
                return;

            if (_phoneLineState != RegState.RegistrationSucceeded)
            {
                InvokeGUIThread(() => { tb_Display.Text = "OFFLINE! Please register."; });
                return;
            }

            if (!String.IsNullOrEmpty(tb_Display.Text))
            {
                var userName = tb_Display.Text;

                _call = _softPhone.CreateCallObject(_phoneLine, userName);
                WireUpCallEvents();
                _call.Start();

                _otherParty = _databaseManager.GetOtherPartyInfos(userName);
                ShowUserInfos(_otherParty);
            }
        }

        void btn_HangUp_Click(object sender, EventArgs e)
        {
            if (_call != null)
            {
                if (_incomingCall && _call.CallState == CallState.Ringing)
                {
                    _call.Reject();
                }
                else
                {
                    _call.HangUp();
                }
                _incomingCall = false;
                _call = null;
            }
            tb_Display.Text = string.Empty;
        }

        void btn_Transfer_Click(object sender, EventArgs e)
        {
            string transferTo = "1001";

            if (_call == null)
                return;

            if (string.IsNullOrEmpty(transferTo))
                return;

            if (_call.CallState != CallState.InCall)
                return;

            _call.BlindTransfer(transferTo);
            InvokeGUIThread(() => { tb_Display.Text = "Transfering to:" + transferTo; });
        }

        void btn_Hold_Click(object sender, EventArgs e)
        {
            if (_call != null)
                _call.ToggleHold();
        }

    }
}
