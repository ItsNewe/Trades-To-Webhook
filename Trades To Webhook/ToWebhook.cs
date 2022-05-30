using System;
using System.Net;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace cAlgo
{
    
    public class Webhook
    {
        public class WebhookResponse
        {

            public int Error { get; set; }
            public string Response { get; set; }

        }

        private readonly string EndPoint = "";

        public Webhook(string NewEndPoint)
        {

            if (NewEndPoint.Trim().Length < 1) throw new ArgumentException("Parameter cannot be null", "NewEndPoint");

            EndPoint = NewEndPoint.Trim();

        }

        public async Task<WebhookResponse> SendAsync(string post_params)
        {

            WebhookResponse response = new WebhookResponse();

            try
            {

                Uri myuri = new Uri(EndPoint);

                string pattern = string.Format("{0}://{1}/.*", myuri.Scheme, myuri.Host);

                Regex urlRegEx = new Regex(pattern);
                WebPermission p = new WebPermission(NetworkAccess.Connect, urlRegEx);
                p.Assert();

                ServicePointManager.SecurityProtocol = (SecurityProtocolType)192 | (SecurityProtocolType)768 | (SecurityProtocolType)3072;

                using (WebClient wc = new WebClient())
                {

                    wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    string json_result = await Task.Run( () => wc.UploadString( myuri, post_params ) );
                                        
                    response.Response = json_result;
                    response.Error = 0;
                    return response;
                }

            }
            catch (Exception exc)
            {

                response.Response = exc.Message;
                response.Error = 1;
                return response;

            }

        }

    }

}
