using System.Collections.Generic;
using System.Net.Http;

namespace Microsoft.Azure.DataLake.Store
{
    /// <summary>
    /// Lists a dictionary of all available Operations for a Http request
    /// </summary>
    internal sealed class Operation
    {
        /// <summary>
        /// HTTP method the operation uses like GET/PUT/POST
        /// </summary>
        internal HttpMethod Method { get; }
        /// <summary>
        /// Whether the http request for this operation requires request body (data)
        /// </summary>
        internal bool RequiresBody { get; }
        /// <summary>
        /// Whether the http response for this operation returns response body (data)
        /// </summary>
        internal bool ReturnsBody { get; }
        /// <summary>
        /// What handler it uses WebHdfs or WebHdfsExt
        /// </summary>
        internal string Namespace { get; }

        internal Operation(HttpMethod mthd, bool reqBody, bool retBody, string nmSpc)
        {
            Method = mthd;
            RequiresBody = reqBody;
            ReturnsBody = retBody;
            Namespace = nmSpc;
        }

        /// <summary>
        /// Dictionary containing the Operations
        /// </summary>
        internal static Dictionary<string, Operation> Operations = new Dictionary<string, Operation>()
        {
            {"OPEN",new Operation(HttpMethod.Get, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"CREATE",new Operation(HttpMethod.Put, Constants.RequiresBodyTrue, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"MKDIRS",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"APPEND",new Operation(HttpMethod.Post,Constants.RequiresBodyTrue, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"CONCURRENTAPPEND",new Operation(HttpMethod.Post, Constants.RequiresBodyTrue, Constants.ReturnsBodyTrue, Constants.WebHdfsExt)},
            {"DELETE",new Operation(HttpMethod.Delete, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"RENAME",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"SETEXPIRY",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfsExt)},
            {"MSCONCAT",new Operation(HttpMethod.Post, Constants.RequiresBodyTrue, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"GETFILESTATUS",new Operation(HttpMethod.Get, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"LISTSTATUS",new Operation(HttpMethod.Get, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"ENUMERATEDELETEDITEMS",new Operation(HttpMethod.Get, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"RESTOREDELETEDITEMS",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"SETTIMES",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"CHECKACCESS",new Operation(HttpMethod.Get, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"SETPERMISSION",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"MODIFYACLENTRIES",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"SETACL",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"SETOWNER",new Operation(HttpMethod.Put,Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"REMOVEACLENTRIES",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"REMOVEACL",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"REMOVEDEFAULTACL",new Operation(HttpMethod.Put, Constants.RequiresBodyFalse, Constants.ReturnsBodyFalse, Constants.WebHdfs)},
            {"GETACLSTATUS",new Operation(HttpMethod.Get, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
            {"GETCONTENTSUMMARY",new Operation(HttpMethod.Get, Constants.RequiresBodyFalse, Constants.ReturnsBodyTrue, Constants.WebHdfs)},
        };
    }
    
    /// <summary>
    /// Constants that describes properties of HttpWeb request
    /// </summary>
    internal static class Constants
    {
        public static bool RequiresBodyTrue = true;
        public static bool RequiresBodyFalse = false;
        public static bool ReturnsBodyTrue = true;
        public static bool ReturnsBodyFalse = false;
        public static string WebHdfs = "/webhdfs/v1";
        public static string WebHdfsExt = "/WebHdfsExt";
    }

}
