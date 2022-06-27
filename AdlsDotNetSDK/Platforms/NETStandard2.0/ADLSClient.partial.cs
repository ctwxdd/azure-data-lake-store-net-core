using Microsoft.Rest;
using System.Net.Http;
using System.Threading;

namespace Microsoft.Azure.DataLake.Store
{
    public partial class AdlsClient
    {
        /// <summary>
        /// Http Client for making calls
        /// </summary>
        public readonly HttpClient HttpClient;

        internal AdlsClient(string accnt, long clientId, HttpClient httpClient, bool skipAccntValidation = false)
            : this(accnt, clientId, skipAccntValidation)
        {
            HttpClient = httpClient;
        }

        internal AdlsClient(string accnt, long clientId, string token, HttpClient httpClient, bool skipAccntValidation = false)
            : this(accnt, clientId, token, skipAccntValidation)
        {
            HttpClient = httpClient;
        }

        internal AdlsClient(string accnt, long clientId, ServiceClientCredentials creds, HttpClient httpClient, bool skipAccntValidation = false)
            : this(accnt, clientId, creds, skipAccntValidation)
        {
            HttpClient = httpClient;
        }

        /// <summary>
        /// Internal factory method that returns a AdlsClient without Account validation. For testing purposes
        /// </summary>
        /// <param name="accnt">Azure data lake store account name including full domain name (e.g. contoso.azuredatalake.net)</param>
        /// <param name="token">Token</param>
        /// <param name="httpClient">HttpClient</param>
        /// <returns>AdlsClient</returns>
        internal static AdlsClient CreateClientWithoutAccntValidation(string accnt, string token, HttpClient httpClient)
        {
            return new AdlsClient(accnt, Interlocked.Increment(ref _atomicClientId), token, httpClient, true);
        }

        /// <summary>
        /// Factory method that creates an instance AdlsClient using the token key. If an application wants to perform multi-threaded operations using this SDK
        /// it is highly recomended to set ServicePointManager.DefaultConnectionLimit to the number of threads application wants the sdk to use before creating any instance of AdlsClient.
        /// By default ServicePointManager.DefaultConnectionLimit is set to 2.
        /// </summary>
        /// <param name="accountFqdn">Azure data lake store account name including full domain name (e.g. contoso.azuredatalakestore.net)</param>
        /// <param name="token">Full authorization Token e.g. Bearer abcddsfere.....</param>
        /// <param name="httpClient">HttpClient</param>
        /// <returns>AdlsClient</returns>
        public static AdlsClient CreateClient(string accountFqdn, string token, HttpClient httpClient)
        {
            return new AdlsClient(accountFqdn, Interlocked.Increment(ref _atomicClientId), token, httpClient);
        }

        /// <summary>
        /// Factory method that creates an instance of AdlsClient using ServiceClientCredential. If an application wants to perform multi-threaded operations using this SDK
        /// it is highly recomended to set ServicePointManager.DefaultConnectionLimit to the number of threads application wants the sdk to use before creating any instance of AdlsClient.
        /// By default ServicePointManager.DefaultConnectionLimit is set to 2.
        /// </summary>
        /// <param name="accountFqdn">Azure data lake store account name including full domain name  (e.g. contoso.azuredatalakestore.net)</param>
        /// <param name="creds">Credentials that retrieves the Auth token</param>
        /// <param name="httpClient">HttpClient</param>
        /// <returns>AdlsClient</returns>
        public static AdlsClient CreateClient(string accountFqdn, ServiceClientCredentials creds, HttpClient httpClient)
        {
            return new AdlsClient(accountFqdn, Interlocked.Increment(ref _atomicClientId), creds, httpClient);
        }
    }
}
