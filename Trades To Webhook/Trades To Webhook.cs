/*  CTRADER GURU --> Template 1.0.8

    Homepage    : https://ctrader.guru/
    Telegram    : https://t.me/ctraderguru
    Twitter     : https://twitter.com/cTraderGURU/
    Facebook    : https://www.facebook.com/ctrader.guru/
    YouTube     : https://www.youtube.com/channel/UCKkgbw09Fifj65W5t5lHeCQ
    GitHub      : https://github.com/ctrader-guru

*/

using System.Linq;
using System;
using cAlgo.API;
using System.Windows.Forms;
using cAlgo.API.Internals;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace cAlgo
{

    #region Extensions

    public static class SymbolExtensions
    {

        public static double DigitsToPips(this Symbol MySymbol, double Pips)
        {

            return Math.Round(Pips / MySymbol.PipSize, 2);

        }

        public static double PipsToDigits(this Symbol MySymbol, double Pips)
        {

            return Math.Round(Pips * MySymbol.PipSize, MySymbol.Digits);

        }

    }

    public static class BarsExtensions
    {

        public static int GetIndexByDate(this Bars MyBars, DateTime MyTime)
        {

            for (int i = MyBars.ClosePrices.Count - 1; i >= 0; i--)
            {

                if (MyTime == MyBars.OpenTimes[i])
                    return i;

            }

            return -1;

        }

    }

    #endregion

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class TradesToWebhook : Robot
    {

        #region Identity

        public const string NAME = "Trades To Webhook";

        public const string VERSION = "1.0.0";

        #endregion

        #region Params

        [Parameter(NAME + " " + VERSION, Group = "Identity", DefaultValue = "https://www.google.com/search?q=ctrader+guru+trades+to+webhook")]
        public string ProductInfo { get; set; }

        [Parameter("Only This ?", Group = "Symbols", DefaultValue = false)]
        public bool OnlyThis { get; set; }

        [Parameter("empty = all | label1,label2,label3", Group = "Labels", DefaultValue = "")]
        public string Labels { get; set; }
        public List<string> ListLabels = new List<string>();

        [Parameter("EndPoint", Group = "Webhook", DefaultValue = "https://api.telegram.org/bot[ YOUR TOKEN ]/sendMessage")]
        public string EndPoint { get; set; }
        public Webhook MyWebook;

        [Parameter("POST", Group = "Webhook", DefaultValue = "chat_id=[ @CHATID ]&text={0}")]
        public string PostParams { get; set; }

        [Parameter("Opened (empty = disabled)", Group = "Messages", DefaultValue = "#{0} opened {1} position at {2} for {3} lots, stoploss {4} takeprofit {5} label '{6}'")]
        public string MessageOpen { get; set; }

        [Parameter("Modified (empty = disabled)", Group = "Messages", DefaultValue = "#{0} modified {1} position at {2} for {3} lots, stoploss {4} takeprofit {5} label '{6}'")]
        public string MessageModify { get; set; }

        [Parameter("Closed (empty = disabled)", Group = "Messages", DefaultValue = "#{0} closed {1} position at {2} for {3} lots, stoploss {4} takeprofit {5} label '{6}'")]
        public string MessageClose { get; set; }

        #endregion

        #region cBot Events

        protected override void OnStart()
        {

            Print("{0} : {1}", NAME, VERSION);

            EndPoint = EndPoint.Trim();
            if (EndPoint.Length < 1)
            {

                MessageBox.Show("Wrong 'EndPoint', es. 'https://api.telegram.org/bot[ YOUR TOKEN ]/sendMessage'", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Stop();

            }

            PostParams = PostParams.Trim();
            if (PostParams.IndexOf("{0}") < 0)
            {

                MessageBox.Show("Wrong 'POST params', es. 'chat_id=[ @CHATID ]&text={0}'", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Stop();

            }

            Labels = Labels.Trim();
            if (Labels.Length > 0)
                ListLabels = Labels.Split(',').ToList();

            MyWebook = new Webhook(EndPoint);

            Positions.Opened += OnPositionOpened;
            Positions.Modified += OnPositionModified;
            Positions.Closed += OnPositionClosed;

        }

        protected override void OnStop()
        {

            Positions.Opened -= OnPositionOpened;
            Positions.Modified -= OnPositionModified;
            Positions.Closed -= OnPositionClosed;

        }

        #endregion

        #region Methods

        private string FormatMessage(Position position, string message)
        {

            int digit = Symbols.GetSymbol(position.SymbolName).Digits;

            string sl = (position.StopLoss == null) ? "0" : ((double)position.StopLoss).ToString("N" + digit);
            string tp = (position.TakeProfit == null) ? "0" : ((double)position.TakeProfit).ToString("N" + digit);

            string label = (position.Label == null || position.Label.Length == 0) ? "" : position.Label;

            return string.Format(message, position.SymbolName, position.TradeType, position.EntryPrice, position.Quantity, sl, tp, label);


        }

        private void ToWebhook(Position position, string message)
        {

            if (message.Trim().Length == 0)
                return;

            if (OnlyThis && position.SymbolName != SymbolName)
                return;

            bool nolabel = position.Label == null || position.Label.Length == 0;

            if (ListLabels.Count > 0 && (nolabel || ListLabels.IndexOf(position.Label) < 0))
                return;

            message = message.Trim();

            string message_to_send = FormatMessage(position, message);

            Task<Webhook.WebhookResponse> webhook_result = Task.Run(async() => await MyWebook.SendAsync(string.Format(PostParams, message_to_send)));

            Print(webhook_result.Result.Response);

        }

        public void OnPositionOpened(PositionOpenedEventArgs args)
        {

            ToWebhook(args.Position, MessageOpen);

        }

        public void OnPositionModified(PositionModifiedEventArgs args)
        {

            ToWebhook(args.Position, MessageModify);

        }

        public void OnPositionClosed(PositionClosedEventArgs args)
        {

            ToWebhook(args.Position, MessageClose);

        }

        #endregion

    }

}
