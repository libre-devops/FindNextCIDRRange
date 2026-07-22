// Checks any CIDR against Azure's subnet rules using nothing but arithmetic: no Azure call, so no
// identity and no Reader grant is ever involved. This endpoint postdates the 1.x contract, so it
// carries none of the preserved quirks: responses are application/json and the wire status is
// always truthful, 200 for any parseable CIDR (including one Azure would refuse, which is still a
// successful answer to the question asked) and 400 only for input that cannot be parsed.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace FindNextCIDR
{
    public class CheckCidr
    {
        private readonly ILogger<CheckCidr> _logger;

        public CheckCidr(ILogger<CheckCidr> logger)
        {
            _logger = logger;
        }

        // Azure reserves five addresses in every subnet: the network address, the default
        // gateway, two addresses for Azure DNS mapping, and the broadcast address. That is why
        // the smallest subnet Azure allows is /29, the largest is /2, and GetCidr has always
        // accepted cidr 2 through 29.
        internal const int AzureReserved = 5;

        public class ReservedAddresses
        {
            public string networkAddress { get; set; }
            public string defaultGateway { get; set; }
            public string[] azureDns { get; set; }
            public string broadcast { get; set; }
        }

        public class CheckCidrResponse
        {
            public string cidr { get; set; }
            public string normalized { get; set; }
            public bool validAzureSubnet { get; set; }
            public int prefixLength { get; set; }
            public long totalAddresses { get; set; }
            public int azureReservedAddresses { get; set; }
            public long usableAddresses { get; set; }
            public ReservedAddresses reserved { get; set; }
            public string firstUsable { get; set; }
            public string lastUsable { get; set; }
            public string reason { get; set; }
        }

        public class CheckCidrError
        {
            public string error { get; set; }
        }

        // The route pins its full api/... path like every function here, because host.json
        // empties the global route prefix so the landing page can own "/".
        [Function("CheckCidr")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "api/CheckCidr")] HttpRequestData req)
        {
            _logger.LogInformation("CheckCidr processed a request.");

            if (TryAnalyze(req.Query["cidr"], out CheckCidrResponse result, out string error))
            {
                return await JsonResponse(req, HttpStatusCode.OK, result);
            }

            return await JsonResponse(req, HttpStatusCode.BadRequest, new CheckCidrError { error = error });
        }

        internal static bool TryAnalyze(string cidrInput, out CheckCidrResponse result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrWhiteSpace(cidrInput))
            {
                error = "cidr is null";
                return false;
            }

            // Without an explicit prefix the parser would guess a classful mask, which is never
            // what an Azure question means.
            if (!cidrInput.Contains('/'))
            {
                error = "cidr must include a prefix length, for example 10.0.0.0/24";
                return false;
            }

            if (!IPNetwork2.TryParse(cidrInput, out IPNetwork2 network))
            {
                error = "not a valid CIDR: " + cidrInput;
                return false;
            }

            if (network.AddressFamily != AddressFamily.InterNetwork)
            {
                error = "only IPv4 CIDRs are supported; Azure IPv6 subnets are always /64";
                return false;
            }

            byte prefix = network.Cidr;
            long total = (long)network.Total;

            var response = new CheckCidrResponse
            {
                cidr = cidrInput,
                normalized = network.ToString(),
                prefixLength = prefix,
                totalAddresses = total,
                azureReservedAddresses = AzureReserved,
            };

            if (prefix > 29)
            {
                response.validAzureSubnet = false;
                response.usableAddresses = 0;
                response.reason = "An Azure subnet cannot be smaller than /29: Azure reserves " + AzureReserved
                    + " addresses in every subnet (the network address, the default gateway, two addresses for"
                    + " Azure DNS, and the broadcast address), so a /" + prefix + " with " + total
                    + (total == 1 ? " address" : " addresses") + " cannot fit them.";
            }
            else if (prefix < 2)
            {
                response.validAzureSubnet = false;
                response.usableAddresses = 0;
                response.reason = "An Azure subnet cannot be larger than /2.";
            }
            else
            {
                response.validAzureSubnet = true;
                response.usableAddresses = total - AzureReserved;
                response.reserved = new ReservedAddresses
                {
                    networkAddress = network.Network.ToString(),
                    defaultGateway = Offset(network.Network, 1).ToString(),
                    azureDns = new[] { Offset(network.Network, 2).ToString(), Offset(network.Network, 3).ToString() },
                    broadcast = network.Broadcast.ToString(),
                };
                response.firstUsable = Offset(network.Network, 4).ToString();
                response.lastUsable = Offset(network.Broadcast, -1).ToString();
            }

            result = response;
            return true;
        }

        private static IPAddress Offset(IPAddress address, int offset)
        {
            byte[] bytes = address.GetAddressBytes();
            uint value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            value = (uint)(value + offset);
            return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
        }

        private static async Task<HttpResponseData> JsonResponse<T>(HttpRequestData req, HttpStatusCode status, T body)
        {
            var response = req.CreateResponse(status);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            var options = new JsonSerializerOptions { WriteIndented = true };
            await response.WriteStringAsync(JsonSerializer.Serialize(body, options));
            return response;
        }
    }
}
