// Serves the API's own documentation: the OpenAPI description (the repo-root openapi.yaml,
// embedded into the assembly at build time so the two can never drift) at /api/openapi.yaml, and
// an interactive Swagger UI page at /api/swagger. Both are 2.x additions; the GetCidr contract
// itself is untouched.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Reflection;

namespace FindNextCIDR
{
    public class Docs
    {
        [Function("OpenApiSpec")]
        public async Task<HttpResponseData> Spec(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi.yaml")] HttpRequestData req)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("openapi.yaml");
            using var reader = new StreamReader(stream);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/yaml; charset=utf-8");
            await response.WriteStringAsync(await reader.ReadToEndAsync());
            return response;
        }

        [Function("SwaggerUi")]
        public async Task<HttpResponseData> Ui(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await response.WriteStringAsync(SwaggerHtml);
            return response;
        }

        private const string SwaggerHtml = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>FindNextCIDRRange API</title>
              <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css">
            </head>
            <body>
              <div id="swagger-ui"></div>
              <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
              <script>
                window.onload = () => {
                  SwaggerUIBundle({
                    url: "openapi.yaml",
                    dom_id: "#swagger-ui",
                    deepLinking: true,
                    tryItOutEnabled: true
                  });
                };
              </script>
            </body>
            </html>
            """;
    }
}
