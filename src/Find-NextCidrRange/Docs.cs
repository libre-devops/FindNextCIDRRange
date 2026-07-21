// Serves everything around the API rather than the API itself: a landing page at the root, the
// OpenAPI description (the repo-root openapi.yaml, embedded into the assembly at build time so
// the two can never drift) at /api/openapi.yaml, and an interactive Swagger UI page at
// /api/swagger. host.json empties the global route prefix so the root route is reachable, and
// every route here and in GetCidr.cs pins its full historical path, so the wire contract is
// unchanged. The landing page is a single self-contained document (inline CSS, system fonts, no
// external assets); only the Swagger UI page pulls swagger-ui-dist from a CDN.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Reflection;

namespace FindNextCIDR
{
    public class Docs
    {
        [Function("Home")]
        public async Task<HttpResponseData> Home(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "/")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await response.WriteStringAsync(HomeHtml);
            return response;
        }

        [Function("OpenApiSpec")]
        public async Task<HttpResponseData> Spec(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/openapi.yaml")] HttpRequestData req)
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/swagger")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await response.WriteStringAsync(SwaggerHtml);
            return response;
        }

        private const string HomeHtml = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>FindNextCIDRRange</title>
              <style>
                :root {
                  --bg: #fafafa; --fg: #1a1a1a; --muted: #6b6b6b;
                  --card: #ffffff; --border: #e4e4e4; --accent: #0a7d62; --code-bg: #f0f0f0;
                }
                @media (prefers-color-scheme: dark) {
                  :root {
                    --bg: #111214; --fg: #ececec; --muted: #9a9a9a;
                    --card: #1a1c1f; --border: #2c2f33; --accent: #2fbf9a; --code-bg: #24272b;
                  }
                }
                * { box-sizing: border-box; margin: 0; }
                body {
                  background: var(--bg); color: var(--fg);
                  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Ubuntu, sans-serif;
                  min-height: 100vh; display: flex; align-items: center; justify-content: center;
                  padding: 2rem 1rem; line-height: 1.6;
                }
                main {
                  max-width: 44rem; width: 100%; background: var(--card);
                  border: 1px solid var(--border); border-radius: 12px; padding: 2.5rem;
                }
                .kicker {
                  color: var(--accent); font-size: 0.8rem; font-weight: 600;
                  letter-spacing: 0.12em; text-transform: uppercase; margin-bottom: 0.75rem;
                }
                h1 { font-size: 1.9rem; letter-spacing: -0.02em; margin-bottom: 0.5rem; }
                .tagline { color: var(--muted); margin-bottom: 1.75rem; }
                pre {
                  background: var(--code-bg); border: 1px solid var(--border); border-radius: 8px;
                  padding: 1rem; overflow-x: auto; font-size: 0.82rem; margin-bottom: 1.75rem;
                }
                code { font-family: ui-monospace, "Cascadia Code", "Fira Code", Menlo, Consolas, monospace; }
                .param { color: var(--accent); }
                .links { display: flex; flex-wrap: wrap; gap: 0.6rem; margin-bottom: 1.75rem; }
                .links a {
                  display: inline-block; padding: 0.5rem 1rem; border-radius: 8px;
                  border: 1px solid var(--border); color: var(--fg);
                  text-decoration: none; font-size: 0.88rem; font-weight: 500;
                }
                .links a:hover { border-color: var(--accent); color: var(--accent); }
                .links a.primary { background: var(--accent); border-color: var(--accent); color: #fff; }
                .links a.primary:hover { filter: brightness(1.08); }
                footer {
                  border-top: 1px solid var(--border); padding-top: 1.25rem;
                  color: var(--muted); font-size: 0.8rem;
                }
                footer a { color: var(--muted); }
                footer a:hover { color: var(--accent); }
              </style>
            </head>
            <body>
              <main>
                <div class="kicker">Libre DevOps</div>
                <h1>FindNextCIDRRange</h1>
                <p class="tagline">
                  An HTTP API that answers one question well: what is the next free CIDR of a
                  given size in an Azure virtual network?
                </p>
                <pre><code>GET /api/GetCidr?subscriptionId=<span class="param">&lt;sub&gt;</span>
               &amp;resourceGroupName=<span class="param">&lt;rg&gt;</span>
               &amp;virtualNetworkName=<span class="param">&lt;vnet&gt;</span>
               &amp;cidr=<span class="param">&lt;2..29&gt;</span>
               [&amp;addressSpace=<span class="param">&lt;cidr&gt;</span>]</code></pre>
                <div class="links">
                  <a class="primary" href="/api/swagger">Try it in Swagger UI</a>
                  <a href="/api/openapi.yaml">OpenAPI spec</a>
                  <a href="https://github.com/libre-devops/FindNextCIDRRange">GitHub</a>
                  <a href="https://libredevops.org">libredevops.org</a>
                </div>
                <footer>
                  MIT licensed. A <a href="https://libredevops.org">Libre DevOps</a> project,
                  originally by Gary L. Mullen-Schultz at Microsoft. The identity behind this app
                  needs Reader on any virtual network it is asked about.
                </footer>
              </main>
            </body>
            </html>
            """;

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
