using System;
using System.IO;
using cAlgo.API;
using cAlgo.API.Internals;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Webhook : Robot
{
    private readonly string _url;

    public Webhook(string endpoint)
    {
		 // ensure TLS1.2+
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
                                             | SecurityProtocolType.Tls11
                                             | SecurityProtocolType.Tls;
        // expects "https://discord.com/api/webhooks/{id}/{token}"
		_url = endpoint.TrimEnd('/');
    }

    public Task<WebhookResponse> SendAsync(string jsonPayload)
    {
        return Task.Run(async () =>
		{
			// again ensure TLS in case someone skips ctor
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			var response = new WebhookResponse();
			try
			{
				using (var wc = new WebClient())
				{
					wc.Headers[HttpRequestHeader.ContentType] = "application/json";
					string fullUrl = _url;
					string result = await Task.Run(() => wc.UploadString(_url, "POST", jsonPayload));
					var j = JObject.Parse(result);

					response.Error = 0;
					response.Response = result;
					response.Message = (string)j["id"];
				}
			} catch (WebException wex) {
				response.Error = 1;
				if (wex.Response != null)
				{
					using (var rdr = new StreamReader(wex.Response.GetResponseStream()))
						response.Response = rdr.ReadToEnd();
				}
				else
				{
					response.Response = wex.Message;
				}
			}
			return response;
        });
    }

    public Task<WebhookResponse> EditAsync(string messageId, string jsonPayload)
    {
        return Task.Run(() =>
        {
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                string editUrl = $"{_url}/messages/{messageId}";
                string result = wc.UploadString(editUrl, "PATCH", jsonPayload);
                return new WebhookResponse
                {
                    Error    = 0,
                    Response = result,
                    Message  = messageId
                };
            }
        });
    }

    public class WebhookResponse
    {
        public int    Error    { get; set; }
        public string Response { get; set; }
        public string Message  { get; set; }
    }
}