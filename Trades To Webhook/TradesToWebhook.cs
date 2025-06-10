using System.Linq;
using System;
using cAlgo.API;
using System.Windows.Forms;
using cAlgo.API.Internals;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

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

		public const string VERSION = "1.1.1";

		#endregion

		#region Params

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

		[Parameter("Opened (empty = disabled)", Group = "Messages", DefaultValue = "#{0} opened {1} position at {2} for {3} lots; stoploss {4}; takeprofit {5}; label '{6}'")]
		public string MessageOpen { get; set; }

		[Parameter("Modified (empty = disabled)", Group = "Messages", DefaultValue = "#{0} modified {1} position at {2} for {3} lots; stoploss {4}; takeprofit {5}; label '{6}'")]
		public string MessageModify { get; set; }

		[Parameter("Closed (empty = disabled)", Group = "Messages", DefaultValue = "#{0} closed {1} position at {2} for {3} lots; stoploss {4}; takeprofit {5}; label '{6}'")]
		public string MessageClose { get; set; }

		[Parameter("Discord Embed Mode", Group = "Discord", DefaultValue = false)]
		public bool EmbedMode { get; set; }


		[Parameter("Enable Debug Logging", Group = "Logging", DefaultValue = false)]
		public bool DebugMode { get; set; }

		private readonly Dictionary<long, string> _positionMsgMap = new Dictionary<long, string>();

		// … inside TradesToWebhook, under your existing maps:

		private readonly Dictionary<long, PositionState> _positionStateMap = new Dictionary<long, PositionState>();

		private class PositionState
		{
			public double Entry { get; set; }
			public double Lots { get; set; }
			public double TP { get; set; }
			public double SL { get; set; }
			public List<string> History { get; set; }

		}

		#endregion

		#region cBot Events

		protected override void OnStart()
		{
			Print("{0} : {1}", NAME, VERSION);

			if (DebugMode)
			{
				Print("DEBUG: DebugMode enabled");
				Print("DEBUG: EndPoint = {0}", EndPoint);
				Print("DEBUG: PostParams = {0}", PostParams);
				Print("DEBUG: Labels = {0}", Labels);
			}

			EndPoint = EndPoint.Trim();
			if(DiscordMode){
				EndPoint += "?wait=true";
			}
			
			if (EndPoint.Length < 1)
			{

				MessageBox.Show("Invalid endpoint URL'", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Stop();

			}

			PostParams = PostParams.Trim();
			if (PostParams.IndexOf("{0}") < 0 && !DiscordMode)
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

			if (DebugMode)
			{
				Print("Debug mode enabled.");
			}

		}

		protected override void OnStop()
		{

			Positions.Opened -= OnPositionOpened;
			Positions.Modified -= OnPositionModified;
			Positions.Closed -= OnPositionClosed;

			if (DebugMode)
			{
				Print("Debug mode disabled.");
			}

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

			Task<Webhook.WebhookResponse> webhook_result = Task.Run(async () => await MyWebook.SendAsync(string.Format(PostParams, message_to_send)));

			// --> We don't know which webhook the client is using, probably a json response
			// --> var Telegram = JObject.Parse(webhook_result.Result.Response);
			// --> Print(Telegram["ok"]);

			if (DebugMode)
			{
				Print("Webhook message sent: {0}", message_to_send);
			}

		}

		private async Task ToWebhookEmbedAsync(Position pos, string messageTemplate, bool _isEdit = false)
		{
			if (string.IsNullOrWhiteSpace(messageTemplate)) return;

			// figure out if we really have an existing message to edit
			bool hasPrev = _positionMsgMap.TryGetValue(pos.Id, out var prevMsgId);
			bool isEdit = hasPrev;

			if (_isEdit && !hasPrev && DebugMode)
				Print("DEBUG: asked to Edit but no previous msgId for Position {0}, will Send instead", pos.Id);

			if (DebugMode)
				Print("DEBUG: ToWebhookEmbedAsync called. PositionId: {0}, isEdit: {1}", pos.Id, isEdit);

			string eventTitle = messageTemplate.Replace("{0}", pos.SymbolName).Split(' ')[1];
			var payload = BuildEmbedPayload(pos, eventTitle, isEdit);
			string json = JsonConvert.SerializeObject(payload);

			if (DebugMode)
				Print("Sending embed (isEdit={0}): {1}", isEdit, json);

			Webhook.WebhookResponse resp;
			try
			{
				resp = isEdit
					? await MyWebook.EditAsync(prevMsgId, json)
					: await MyWebook.SendAsync(json);
			}
			catch (Exception ex)
			{
				// log the real error
				Print("Exception sending embed: {0}", ex.Message);
				if (DebugMode)
					Print("STACKTRACE: {0}", ex.ToString());
				return;
			}

			if (resp.Error == 0)
			{
				// build or update state + history
				if (!_positionStateMap.TryGetValue(pos.Id, out var state))
				{
					state = new PositionState
					{
						Entry = pos.EntryPrice,
						Lots = pos.Quantity,
						TP = pos.TakeProfit ?? 0,
						SL = pos.StopLoss ?? 0,
						History = new List<string>()
					};
				}
				else
				{
					state.Entry = pos.EntryPrice;
					state.Lots = pos.Quantity;
					state.TP = pos.TakeProfit ?? 0;
					state.SL = pos.StopLoss ?? 0;
				}

				// add this event + timestamp
				state.History.Add($"{eventTitle}: {DateTime.UtcNow:HH:mm:ss}");

				// store back
				_positionStateMap[pos.Id] = state;
				var j = JObject.Parse(resp.Response);
				_positionMsgMap[pos.Id] = (string)j["id"];

				if (DebugMode)
				{
					Print("Embed sent successfully. Id: {0}", resp.Message);
				}
			}
			else
			{
				Print("Embed webhook error: {0}, Response: {1}", resp.Error, resp.Response);
				return;
			}
		}

		public void OnPositionOpened(PositionOpenedEventArgs args)
		{
			if (DebugMode)
				Print("DEBUG: OnPositionOpened for PositionId {0}", args.Position.Id);

			BeginInvokeOnMainThread(() => _ = ToWebhookEmbedAsync(args.Position, MessageOpen));

			if (DebugMode)
			{
				Print("Position opened: {0}", args.Position.Id);
			}
		}

		public void OnPositionModified(PositionModifiedEventArgs args)
		{
			if (DebugMode)
				Print("DEBUG: OnPositionModified for PositionId {0}", args.Position.Id);

			BeginInvokeOnMainThread(() => _ = ToWebhookEmbedAsync(args.Position, MessageModify, true));

			if (DebugMode)
			{
				Print("Position modified: {0}", args.Position.Id);
			}
		}

		public void OnPositionClosed(PositionClosedEventArgs args)
		{
			if (DebugMode)
				Print("DEBUG: OnPositionClosed for PositionId {0}", args.Position.Id);

			// fire off the embed–send/edit on main thread
			BeginInvokeOnMainThread(() =>
			{
				// kick off the async call
				_ = ToWebhookEmbedAsync(args.Position, MessageClose)
					.ContinueWith(t =>
					{
						// when done, schedule removal back on main thread
						BeginInvokeOnMainThread(() =>
						{
							_positionMsgMap.Remove(args.Position.Id);

							if (DebugMode)
							{
								Print("Position closed: {0}", args.Position.Id);
							}
						});
					});
			});
		}

		private object BuildEmbedPayload(Position pos, string eventTitle, bool isEdit)
		{
			int digits = Symbols.GetSymbol(pos.SymbolName).Digits;

			// pull old state if editing
			_positionStateMap.TryGetValue(pos.Id, out var old);

			// format current values
			string curEntry = pos.EntryPrice.ToString($"F{digits}");
			string curLots = pos.Quantity.ToString("F2");
			string curTP = pos.TakeProfit.HasValue
								? pos.TakeProfit.Value.ToString($"F{digits}")
								: "N/A";
			string curSL = (pos.StopLoss ?? 0).ToString($"F{digits}");


			// actually build each display string
			string format = $"F{digits}";
			string entryField = old != null && old.Entry != pos.EntryPrice
				? $"~~{old.Entry.ToString(format)}~~ {curEntry}"
				: curEntry;

			string lotsField = old != null && old.Lots != pos.Quantity
				? $"~~{old.Lots:F2}~~ {curLots}"
				: curLots;

			string tpField = old != null && old.TP != (pos.TakeProfit ?? 0)
				? $"~~{old.TP.ToString(format)}~~ {curTP}"
				: curTP;

			string slField = old != null && old.SL != (pos.StopLoss ?? 0)
				? $"~~{old.SL.ToString(format)}~~ {curSL}"
				: curSL;

			_positionStateMap.TryGetValue(pos.Id, out var _old);
			string footerText = _old?.History != null && _old.History.Count > 0
				? string.Join(" | ", _old.History)
				: "";
			return new
			{
				username = NAME,
				embeds = new[] {
				new {
					title = $"{pos.SymbolName} {eventTitle}",
					color = pos.TradeType == TradeType.Buy ? 0x00FF00 : 0xFF0000,
					fields = new[] {
						new { name = "Entry", value = entryField, inline = true },
						new { name = "Lots",  value = lotsField,  inline = true },
						new { name = "TP",    value = tpField,     inline = true },
						new { name = "SL",    value = slField,     inline = false }
					},
					timestamp = DateTime.UtcNow.ToString("o"),
					footer = new {
						text = footerText
					}
				}
			}
			};
		}

		#endregion

	}

}
