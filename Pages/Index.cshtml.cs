using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WebsiteChecker.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IHttpClientFactory _clientFactory;

        public IndexModel(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
            UrlToCheck = string.Empty;
            CheckedUrl = string.Empty;
            ErrorMessage = string.Empty;
            CheckResults = new Dictionary<string, bool>();
            RedirectedUrl = string.Empty;
            RedirectsToDifferentDomain = false;
        }

        [BindProperty]
        [Required(ErrorMessage = "Please enter a URL.")]
        [Url(ErrorMessage = "Please enter a valid URL.")]
        public string UrlToCheck { get; set; }

        public bool IsUp { get; set; }
        public string CheckedUrl { get; set; }
        public string ErrorMessage { get; set; }
        public bool CheckPerformed { get; set; } = false;

        public Dictionary<string, bool> CheckResults { get; set; }

        // New properties for redirect detection
        public string RedirectedUrl { get; set; }
        public bool RedirectsToDifferentDomain { get; set; }

        public void OnGet()
        {
            // Initial state
        }

        public async Task<IActionResult> OnPostAsync()
        {
            CheckedUrl = UrlToCheck;
            CheckPerformed = true;

            if (!ModelState.IsValid)
            {
                IsUp = false;
                return Page();
            }

            string urlToProcess = UrlToCheck;
            if (!urlToProcess.StartsWith("http://") && !urlToProcess.StartsWith("https://"))
            {
                urlToProcess = "http://" + urlToProcess;
            }

            Uri uri;
            try
            {
                uri = new Uri(urlToProcess);
            }
            catch
            {
                IsUp = false;
                ErrorMessage = "Invalid URL format.";
                return Page();
            }

            string host = uri.Host;

            try
            {
                // DNS Resolution
                try
                {
                    var dns = await Dns.GetHostAddressesAsync(host);
                    CheckResults["DNS"] = dns.Length > 0;
                }
                catch
                {
                    CheckResults["DNS"] = false;
                }

                // TCP Connection
                try
                {
                    using var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(host, uri.Port > 0 ? uri.Port : 80);
                    CheckResults["TCP"] = true;
                }
                catch
                {
                    CheckResults["TCP"] = false;
                }

                // HTTP Response with redirect detection
                bool httpSuccess = false;
                HttpResponseMessage? response = null;

                // Use HttpClientHandler to disable automatic redirect following
                var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var redirectClient = new HttpClient(handler);
                redirectClient.Timeout = TimeSpan.FromSeconds(10);

                try
                {
                    response = await redirectClient.GetAsync(urlToProcess);

                    httpSuccess = response.IsSuccessStatusCode || 
                                  (int)response.StatusCode >= 300 && (int)response.StatusCode < 400;

                    CheckResults["HTTP"] = httpSuccess;

                    // Check for redirect status code (3xx)
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    {
                        var locationHeader = response.Headers.Location;
                        if (locationHeader != null)
                        {
                            string redirectedTo = locationHeader.IsAbsoluteUri
                                ? locationHeader.ToString()
                                : new Uri(uri, locationHeader).ToString();

                            RedirectedUrl = redirectedTo;

                            var originalHost = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
                            var redirectedHost = new Uri(redirectedTo).Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);

                            RedirectsToDifferentDomain = !originalHost.Equals(redirectedHost, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                catch
                {
                    CheckResults["HTTP"] = false;
                }

                // SSL Certificate check (simple)
                CheckResults["SSL"] = urlToProcess.StartsWith("https://");

                // Load Time
                try
                {
                    var client = _clientFactory.CreateClient();
                    var sw = Stopwatch.StartNew();
                    await client.GetAsync(urlToProcess);
                    sw.Stop();
                    CheckResults["LoadTime"] = sw.ElapsedMilliseconds < 3000; // Arbitrary 3 sec limit
                }
                catch
                {
                    CheckResults["LoadTime"] = false;
                }

                // HTTP Status Code
                CheckResults["StatusCode"] = response != null && (int)response.StatusCode < 400;

                // Content Available
                try
                {
                    var client = _clientFactory.CreateClient();
                    var content = await client.GetStringAsync(urlToProcess);
                    CheckResults["Content"] = !string.IsNullOrEmpty(content);
                }
                catch
                {
                    CheckResults["Content"] = false;
                }

                // Firewall Blocking (approximation)
                CheckResults["Firewall"] = CheckResults["TCP"];

                // Ping Reachability (approximation)
                try
                {
                    var pingTest = await Dns.GetHostEntryAsync(host);
                    CheckResults["Ping"] = pingTest.AddressList.Length > 0;
                }
                catch
                {
                    CheckResults["Ping"] = false;
                }

                // Overall status
                IsUp = CheckResults["DNS"] && CheckResults["TCP"] && CheckResults["HTTP"];
            }
            catch (Exception ex)
            {
                IsUp = false;
                ErrorMessage = $"An unexpected error occurred: {ex.Message}";
            }

            return Page();
        }
    }
}
