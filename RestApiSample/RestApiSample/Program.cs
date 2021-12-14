﻿using System;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace RestApiSample
{
    class Program
    {
        static void Main(string[] args)
        {
            CallRefreshAsync();
            Console.ReadLine();
        }

        private static async void CallRefreshAsync()
        {
            HttpClient client = new HttpClient();
            //AAS template
            //client.BaseAddress = new Uri("https://<rollout>.asazure.windows.net/servers/<serverName>/models/<resource>/");

            //PBI template
            client.BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/groups/<workspaceID>/datasets/<datasetID>/");

            // Send refresh request
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UpdateToken());

            RefreshRequest refreshRequest = new RefreshRequest()
            {
                type = "full",
                maxParallelism = 10
            };

            HttpResponseMessage response = await client.PostAsJsonAsync("refreshes", refreshRequest);
            string content = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            Uri location = response.Headers.Location;
            Console.WriteLine(response.Headers.Location);

            // Check the response
            while (true) // Will exit while loop when exit Main() method (it's running asynchronously)
            {
                string output = "";

                // Refresh token if required
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UpdateToken());

                response = await client.GetAsync(location);
                if (response.IsSuccessStatusCode)
                {
                    output = await response.Content.ReadAsStringAsync();
                }

                Console.Clear();
                Console.WriteLine(output);

                Thread.Sleep(5000);
            }
        }

        private static async Task<string> UpdateToken()
        {

            // AAS REST API Inputs:
            // string resourceURI = "https://*.asazure.windows.net";
            // string authority = "https://login.windows.net/<TenantID>/oauth2/authorize";
            // AuthenticationContext ac = new AuthenticationContext(authority);

            // PBI REST API Inputs:
            string resourceURI = "https://analysis.windows.net/powerbi/api";
            string authority = "https://login.microsoftonline.com/<TenantID>";
            string[] scopes = new string[] { $"{resourceURI}/.default" };



            #region Use Interactive or username/password

            //string clientID = "<App ID>"; // Native app with necessary API permissions

            //Interactive login if not cached:
            //AuthenticationContext ac = new AuthenticationContext(authority);
            //AuthenticationResult ar = await ac.AcquireTokenAsync(resourceURI, clientID, new Uri("urn:ietf:wg:oauth:2.0:oob"), new PlatformParameters(PromptBehavior.SelectAccount));

            // Username/password:
            // AuthenticationContext ac = new AuthenticationContext(authority);
            // UserPasswordCredential cred = new UserPasswordCredential("<User ID (UPN e-mail format)>", "<Password>");
            // AuthenticationResult ar = await ac.AcquireTokenAsync(resourceURI, clientID, cred);

            #endregion

            // AAS Service Principal:
            // ClientCredential cred = new ClientCredential("<App ID>", "<App Key>");
            // AuthenticationResult ar = await ac.AcquireTokenAsync(resourceURI, cred);


            // PBI Service Principal: 
            AuthenticationContext ac = new AuthenticationContext(authority);
            ClientCredential cred = new ClientCredential("<App ID>", "<App Key>");
            AuthenticationResult ar = await ac.AcquireTokenAsync(resourceURI, cred);

            return ar.AccessToken;
        }
    }

    class RefreshRequest
    {
        public string type { get; set; }
        public int maxParallelism { get; set; }
    }
}
