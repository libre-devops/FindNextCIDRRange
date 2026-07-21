// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

/*
 MIT License
Copyright (c) 2021 Gary L. Mullen-Schultz
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// .NET 10 isolated worker port of Gary L. Mullen-Schultz's original in-process function. The HTTP
// contract is preserved exactly, warts deliberately included: the same route, methods, and query
// parameters; the same indented-JSON string bodies with the same field names; text/plain content
// type on responses; and every error returned with HTTP 400 on the wire while the intended status
// lives in the body's code field, because existing consumers parse the body, not the status line.
// One behaviour change only: the original kept the working status code in a static field shared
// across invocations, which could bleed one request's error code into another's error body under
// concurrency; it is a local here. Per-request behaviour is identical.

using Azure;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace FindNextCIDR
{
    public class GetCidr
    {
        private readonly ILogger<GetCidr> _logger;

        public GetCidr(ILogger<GetCidr> logger)
        {
            _logger = logger;
        }

        public class ProposedSubnetResponse
        {
            public string name { get; set; }
            public string id { get; set; }
            public string type { get; set; }
            public string location { get; set; }
            public string addressSpace { get; set; }
            public string proposedCIDR { get; set; }
        }

        public class CustomError
        {
            public string code { get; set; }
            public string message { get; set; }
        }

        // The route is pinned to the full historical path because host.json empties the global
        // route prefix (so the landing page can own "/"); the wire URL is unchanged.
        [Function("GetCidr")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "api/GetCidr")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            string subscriptionId = req.Query["subscriptionId"];
            string virtualNetworkName = req.Query["virtualNetworkName"];
            string resourceGroupName = req.Query["resourceGroupName"];
            string cidrString = req.Query["cidr"];
            string desiredAddressSpace = req.Query["addressSpace"];
            Exception error = null;
            string errorMessage = null;
            bool success = false;
            string foundSubnet = null;
            string foundAddressSpace = null;
            byte cidr;
            VirtualNetworkResource vNet = null;
            HttpStatusCode httpStatusCode = HttpStatusCode.OK;

            try
            {
                // Validate the input params
                errorMessage = ValidateInput(subscriptionId, virtualNetworkName, resourceGroupName, cidrString, desiredAddressSpace);
                if (null == errorMessage)
                {
                    // Make sure the CIDR is valid
                    if (ValidateCIDR(cidrString))
                    {
                        cidr = Byte.Parse(cidrString);

                        // Get a client for the SDK calls. The resource group id is constructed
                        // directly rather than via GetDefaultSubscriptionAsync: the original's
                        // subscription lookup demanded subscriptions/read at subscription scope,
                        // which made narrowly scoped Reader grants (a resource group or a single
                        // vnet) fail with a 403 dressed as a 500. Reading only what is queried
                        // means Reader on the vnet's scope is genuinely enough.
                        var armClient = new ArmClient(new DefaultAzureCredential(), subscriptionId);

                        ResourceGroupResource rg = armClient.GetResourceGroupResource(
                            ResourceGroupResource.CreateResourceIdentifier(subscriptionId, resourceGroupName));

                        vNet = await rg.GetVirtualNetworkAsync(virtualNetworkName);

                        foreach (string ip in vNet.Data.AddressPrefixes)
                        {
                            IPNetwork2 vNetCIDR = IPNetwork2.Parse(ip);
                            if (cidr >= vNetCIDR.Cidr && (null == desiredAddressSpace || vNetCIDR.ToString().Equals(desiredAddressSpace)))
                            {
                                _logger.LogInformation("In: Candidate = " + vNetCIDR.ToString() + ", desired = " + desiredAddressSpace);
                                foundSubnet = GetValidSubnetIfExists(vNet, vNetCIDR, cidr);
                                foundAddressSpace = vNetCIDR.ToString();

                                if (null != foundSubnet)
                                {
                                    _logger.LogInformation("Valid subnet is found: " + foundSubnet);
                                    success = true;
                                    break;
                                }
                            }
                        }

                        if (!success)
                        {
                            httpStatusCode = HttpStatusCode.NotFound;
                            if (null == desiredAddressSpace)
                                errorMessage = "VNet " + resourceGroupName + "/" + virtualNetworkName + " cannot accept a subnet of size " + cidr;
                            else
                                errorMessage = "Requested address space (" + desiredAddressSpace + ") not found in VNet " + resourceGroupName + "/" + virtualNetworkName;
                        }
                    }
                    else
                    {
                        httpStatusCode = HttpStatusCode.BadRequest;
                        errorMessage = "Invalid CIDR size requested: " + cidrString;
                    }
                }
                else
                {
                    httpStatusCode = HttpStatusCode.BadRequest;
                    errorMessage = "Invalid input: " + errorMessage;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404) // case the resource group or vnet doesn't exist
            {
                httpStatusCode = HttpStatusCode.NotFound;
                error = ex;
            }
            catch (Exception e)
            {
                httpStatusCode = HttpStatusCode.InternalServerError;
                error = e;
                // empty code var will signal error
            }

            if (null == errorMessage && success)
            {
                ProposedSubnetResponse proposedSubnetResponse = new ProposedSubnetResponse()
                {
                    name = virtualNetworkName,
                    id = vNet.Id,
                    type = vNet.Id.ResourceType,
                    location = vNet.Data.Location.ToString(),
                    proposedCIDR = foundSubnet,
                    addressSpace = foundAddressSpace
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(proposedSubnetResponse, options);

                return await PlainTextResponse(req, HttpStatusCode.OK, jsonString);
            }
            else
            {
                if (null != error)
                {
                    errorMessage = error.Message;
                }
                var customError = new CustomError
                {
                    code = "" + ((int)httpStatusCode),
                    message = httpStatusCode.ToString() + ", " + errorMessage
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(customError, options);

                // The original returned BadRequestObjectResult for every error, so the wire status
                // is always 400 and the intended status lives in the body. Preserved on purpose.
                return await PlainTextResponse(req, HttpStatusCode.BadRequest, jsonString);
            }
        }

        private static async Task<HttpResponseData> PlainTextResponse(HttpRequestData req, HttpStatusCode status, string body)
        {
            var response = req.CreateResponse(status);
            // ObjectResult over a string serialized as text/plain in the in-process model; kept for
            // byte-compatibility with existing consumers.
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync(body);
            return response;
        }

        private static string ValidateInput(string subscriptionId, string virtualNetworkName, string resourceGroupName, string cidrString, string desiredAddressSpace)
        {
            string errorMessage = null;

            if (null == subscriptionId)
            {
                errorMessage = "subscriptionId is null";
            }
            else if (null == virtualNetworkName)
            {
                errorMessage = "virtualNetworkName is null";
            }
            else if (null == resourceGroupName)
            {
                errorMessage = "resourceGroupName is null";
            }
            else if (null == cidrString)
            {
                errorMessage = "cidr is null";
            }
            else if (!ValidateCIDRBlock(desiredAddressSpace))
            {
                errorMessage = "desiredAddressSpace is invalid";
            }

            return errorMessage;
        }

        private static bool ValidateCIDRBlock(string inCIDRBlock)
        {
            bool isGood = false;

            if (null == inCIDRBlock)
            {
                isGood = true;
            }
            else
            {
                try
                {
                    IPNetwork2.Parse(inCIDRBlock);
                    isGood = true;
                }
                catch
                {
                    isGood = false;
                }
            }

            return isGood;
        }

        private static bool ValidateCIDR(string inCIDR)
        {
            bool isGood = false;

            byte cidr;

            if (Byte.TryParse(inCIDR, out cidr))
            {
                isGood = (2 <= cidr && 29 >= cidr);
            }

            return isGood;
        }

        private static string GetValidSubnetIfExists(VirtualNetworkResource vNet, IPNetwork2 vNetCIDR, Byte cidr)
        {
            var usedSubnets = new List<IPNetwork2>();

            // Get every Azure subnet in the VNet
            SubnetCollection usedSubnetsAzure = vNet.GetSubnets();

            // Get a list of all CIDRs that could possibly fit into the given address space with the CIDR range requested
            IPNetworkCollection candidateSubnets = vNetCIDR.Subnet(cidr);

            // Convert into IPNetwork object list
            foreach (SubnetResource usedSubnet in usedSubnetsAzure)
            {
                var prefixes = new List<string>();

                prefixes.AddRange(usedSubnet.Data.AddressPrefixes);

                if (null != usedSubnet.Data.AddressPrefix && !prefixes.Contains(usedSubnet.Data.AddressPrefix))
                    prefixes.Add(usedSubnet.Data.AddressPrefix);

                foreach (var prefix in prefixes)
                    usedSubnets.Add(IPNetwork2.Parse(prefix));
            }

            foreach (IPNetwork2 candidateSubnet in candidateSubnets)
            {
                bool subnetIsValid = true;
                // Go through each Azure subnet in VNet, check against candidate
                foreach (IPNetwork2 usedSubnet in usedSubnets)
                {
                    if (usedSubnet.Overlap(candidateSubnet))
                    {
                        subnetIsValid = false;
                        break; // stop the loop as the candidate is not valid (overlapping with existing subnets)
                    }
                }
                if (subnetIsValid)
                {
                    return candidateSubnet.ToString();
                }
            }
            // no valid subnet found
            return null;
        }
    }
}
