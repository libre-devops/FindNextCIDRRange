using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Collections.Specialized;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Web;

namespace FindNextCIDRRange.Tests;

// Minimal hand-rolled stand-ins for the isolated worker's HTTP abstractions, just enough for the
// function to run outside a Functions host. Only the members the function actually touches do
// anything; the rest return inert defaults.

internal sealed class FakeFunctionContext : FunctionContext
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object GetService(Type serviceType) => null;
    }

    public override string InvocationId => "00000000-0000-0000-0000-000000000000";
    public override string FunctionId => "GetCidr";
    public override TraceContext TraceContext => null;
    public override BindingContext BindingContext => null;
    public override RetryContext RetryContext => null;
    public override IServiceProvider InstanceServices { get; set; } = EmptyServiceProvider.Instance;
    public override FunctionDefinition FunctionDefinition => null;
    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
    public override IInvocationFeatures Features => null;
}

internal sealed class FakeHttpRequestData : HttpRequestData
{
    private readonly Uri _url;
    private readonly string _method;
    private readonly MemoryStream _body;

    public FakeHttpRequestData(FunctionContext context, Uri url, string method = "GET", string body = null)
        : base(context)
    {
        _url = url;
        _method = method;
        _body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
    }

    public override Stream Body => _body;
    public override HttpHeadersCollection Headers { get; } = new();
    public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
    public override Uri Url => _url;
    public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
    public override string Method => _method;
    public override NameValueCollection Query => HttpUtility.ParseQueryString(_url.Query);

    public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);
}

internal sealed class FakeHttpResponseData : HttpResponseData
{
    public FakeHttpResponseData(FunctionContext context) : base(context)
    {
    }

    public override HttpStatusCode StatusCode { get; set; }
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();
    public override HttpCookies Cookies => null;

    public string ReadBody()
    {
        Body.Position = 0;
        using var reader = new StreamReader(Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
