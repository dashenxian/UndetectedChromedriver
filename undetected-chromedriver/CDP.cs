using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

public class CDPObject : Dictionary<string, object>
{
    public CDPObject() : base() { }

    public CDPObject(IDictionary<string, object> dictionary) : base(dictionary)
    {
        foreach (var key in Keys)
        {
            if (this[key] is JObject jObject)
            {
                this[key] = new CDPObject(jObject.ToObject<Dictionary<string, object>>());
            }
            else if (this[key] is JArray jArray)
            {
                for (int i = 0; i < jArray.Count; i++)
                {
                    if (jArray[i] is JObject jObj)
                    {
                        jArray[i] = JToken.FromObject(new CDPObject(jObj.ToObject<Dictionary<string, object>>()));
                    }
                }
            }
        }
    }

    public override string ToString()
    {
        var tpl = $"{GetType().Name}(\n\t{{}}\n\t)";
        var items = string.Join("\n  ", this.Select(kv => $"{kv.Key} = {kv.Value}"));
        return string.Format(tpl, items);
    }
}

public class PageElement : CDPObject
{
    public PageElement(IDictionary<string, object> dict) : base(dict) { }
}

public class CDP
{
    private static readonly ILogger<CDP> _log = new LoggerFactory().CreateLogger<CDP>();

    private static readonly CDPObject endpoints = new CDPObject(new Dictionary<string, object>
    {
        { "json", "/json" },
        { "protocol", "/json/protocol" },
        { "list", "/json/list" },
        { "new", "/json/new?{url}" },
        { "activate", "/json/activate/{id}" },
        { "close", "/json/close/{id}" }
    });

    private readonly HttpClient _httpClient;
    private readonly string _serverAddr;
    private int _reqId;
    private HttpResponseMessage _lastResp;
    private JObject _lastJson;
    private string _wsurl;
    public string SessionId { get; private set; }

    public CDP(ChromeOptions options)
    {
        _httpClient = new HttpClient();
        _serverAddr = $"http://{options.DebuggerAddress.Split(':')[0]}:{options.DebuggerAddress.Split(':')[1]}";
        var resp = Get(endpoints["json"].ToString());
        SessionId = resp[0]["id"].ToString();
        _wsurl = resp[0]["webSocketDebuggerUrl"].ToString();
    }

    public void TabActivate(string id = null)
    {
        if (string.IsNullOrEmpty(id))
        {
            var activeTab = TabList()[0];
            id = activeTab["id"].ToString();
            _wsurl = activeTab["webSocketDebuggerUrl"].ToString();
        }
        Post(endpoints["activate"].ToString().Replace("{id}", id));
    }

    public List<PageElement> TabList()
    {
        var retval = Get(endpoints["list"].ToString());
        return retval.Select(o => new PageElement(o.ToObject<Dictionary<string, object>>())).ToList();
    }

    public void TabNew(string url)
    {
        Post(endpoints["new"].ToString().Replace("{url}", url));
    }

    public void TabCloseLastOpened()
    {
        var sessions = TabList();
        var openTabs = sessions.Where(s => s["type"].ToString() == "page").ToList();
        Post(endpoints["close"].ToString().Replace("{id}", openTabs.Last()["id"].ToString()));
    }

    public async Task Send(string method, Dictionary<string, object> parameters)
    {
        _reqId++;
        using (var clientWebSocket = new ClientWebSocket())
        {
            await clientWebSocket.ConnectAsync(new Uri(_wsurl), CancellationToken.None);
            var request = JsonConvert.SerializeObject(new
            {
                method = method,
                @params = parameters,
                id = _reqId
            });
            var bytesToSend = Encoding.UTF8.GetBytes(request);
            await clientWebSocket.SendAsync(new ArraySegment<byte>(bytesToSend), WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[1024];
            var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);

            _lastJson = JObject.Parse(response);
            _log.LogInformation(_lastJson.ToString());
        }
    }

    private JArray Get(string uri)
    {
        var response = _httpClient.GetStringAsync(_serverAddr + uri).Result;
        try
        {
            _lastResp = new HttpResponseMessage();
            _lastJson = JObject.Parse(response);
            return JArray.Parse(response);
        }
        catch
        {
            return null;
        }
    }

    private HttpResponseMessage Post(string uri, Dictionary<string, object> data = null)
    {
        var content = new StringContent(JsonConvert.SerializeObject(data ?? new Dictionary<string, object>()), Encoding.UTF8, "application/json");
        var response = _httpClient.PostAsync(_serverAddr + uri, content).Result;
        try
        {
            _lastResp = response;
            _lastJson = JObject.Parse(response.Content.ReadAsStringAsync().Result);
            return _lastResp;
        }
        catch
        {
            return _lastResp;
        }
    }

    public JObject LastJson => _lastJson;
}
