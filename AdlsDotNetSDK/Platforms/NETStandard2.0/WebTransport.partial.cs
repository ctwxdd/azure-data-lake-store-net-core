using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.DataLake.Store
{
    internal partial class WebTransport
    {
        private static void PostPowershellLogDetails(HttpRequestMessage request, HttpResponseMessage response)
        {
            if (WebTransportPowershellLog.IsDebugEnabled)
            {
                string message = $"VERB: {request.Method}{Environment.NewLine}{Environment.NewLine}RequestHeaders:{Environment.NewLine}";
                bool firstHeader = true;
                foreach (var requestHeader in request.Headers)
                {
                    if (requestHeader.Key.Equals("Authorization"))
                    {
                        message += (!firstHeader ? Environment.NewLine : "") +
                                   $"[AuthorizationHeaderLength:{requestHeader.Value.FirstOrDefault().Length}]";
                    }
                    else
                    {
                        message += (!firstHeader ? Environment.NewLine : "") +
                                   $"[{requestHeader.Key}:{requestHeader.Value}]";
                    }

                    firstHeader = false;
                }
                message += $"{Environment.NewLine}{Environment.NewLine}";
                message += $"ResponseStatus:{response.StatusCode}{Environment.NewLine}{Environment.NewLine}ResponseHeaders:{Environment.NewLine}";
                firstHeader = true;
                foreach (var responseHeader in response.Headers)
                {
                    message += (!firstHeader ? Environment.NewLine : "") + $"[{responseHeader.Key}:{responseHeader.Value.FirstOrDefault()}]";
                    firstHeader = false;
                }

                message += $"{Environment.NewLine}{Environment.NewLine}";
                WebTransportPowershellLog.Debug(message);
            }
        }

        /// <summary>
        /// Sets the WebRequest headers
        /// </summary>
        /// <param name="webReq">HttpWebRequest</param>
        /// <param name="client">AdlsClient</param>
        /// <param name="req">RequestOptions</param>
        /// <param name="token">Auth token</param>
        /// <param name="opMethod">Operation method (e.g. POST/GET)</param>
        /// <param name="customHeaders">Custom headers</param>
        /// <param name="postRequestLength">Request Length</param>
        private static void AssignCommonHttpHeaders(HttpRequestMessage webReq, AdlsClient client, RequestOptions req, string token, HttpMethod opMethod, IDictionary<string, string> customHeaders, int postRequestLength)
        {

            webReq.Headers.Add("Authorization", token);
            string latencyHeader = LatencyTracker.GetLatency();
            if (!string.IsNullOrEmpty(latencyHeader))
            {
                webReq.Headers.Add("x-ms-adl-client-latency", latencyHeader);
            }

            if (client.ContentEncoding != null && postRequestLength > MinDataSizeForCompression)
            {
                webReq.Content.Headers.ContentEncoding.Add(client.ContentEncoding);
            }

            if (client.DipIp != null && !req.IgnoreDip)
            {
                webReq.Headers.Host = client.AccountFQDN;
            }

            if (!req.KeepAlive)
            {
                webReq.Headers.ConnectionClose = true;
            }

            if (customHeaders != null)
            {
                string contentType;
                if (customHeaders.TryGetValue("Content-Type", out contentType))
                {
                    webReq.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                }
                foreach (var key in customHeaders.Keys)
                {
                    if (!HeadersNotToBeCopied.Contains(key))
                    {
                        webReq.Headers.Add(key, customHeaders[key]);
                    }

                }
            }

            webReq.Headers.TryAddWithoutValidation("User-Agent", client.GetUserAgent());
            webReq.Headers.Add("x-ms-client-request-id", req.RequestId);
            webReq.Method = opMethod;
        }

        private static void HandleUnsuccessfullCall(HttpRequestMessage webReq, HttpResponseMessage webResponse, RequestOptions req, OperationResponse resp, string path, string token)
        {
            try
            {
                PostPowershellLogDetails(webReq, webResponse);
                resp.HttpStatus = webResponse.StatusCode;
                resp.RequestId = webResponse.Headers.GetValues("x-ms-request-id").FirstOrDefault();

                if (resp.HttpStatus == HttpStatusCode.Unauthorized && TokenLog.IsDebugEnabled)
                {
                    string tokenLogLine =
                        $"HTTPRequest,HTTP401,cReqId:{req.RequestId},sReqId:{resp.RequestId},path:{path},token:{token}";
                    TokenLog.Debug(tokenLogLine);
                }
                resp.HttpMessage = webResponse.ReasonPhrase;
                ByteBuffer errorResponseData = default(ByteBuffer);

                using (Stream errorStream = webResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                {
                    // Reading the data from the error response into a byte array is necessary to show the actual error data as a part of the 
                    // error message in case JSON parsing does not work. We read the bytes and then pass it back to JsonTextReader using a memorystream
                    int noBytes;
                    int totalLengthToRead = errorResponseData.Count;
                    do
                    {
                        noBytes = errorStream.Read(errorResponseData.Data, errorResponseData.Offset, totalLengthToRead);
                        errorResponseData.Offset += noBytes;
                        totalLengthToRead -= noBytes;

                    } while (noBytes > 0 && totalLengthToRead > 0);
                    // Pass errorResponseData.Offset instead of errorResponseData.Count because errorResponseData.Offset can be less than errorResponseData.Count 
                    //This will be the case mostly for chunked error response where we initialize the byte with 1000 bytes
                    ParseRemoteError(errorResponseData.Data, errorResponseData.Offset, resp, webResponse.Content.Headers.ContentType.ToString());
                }
            }
            catch (Exception ex)
            {
                resp.Ex = ex;
            }
        }

        private static void HandleTaskCancelException(TaskCanceledException e, OperationResponse resp, CancellationToken timeoutCancelToken, CancellationToken actualCancelToken = default(CancellationToken))
        {
            // The status property will be set to RequestCanceled after Abort.
            if (timeoutCancelToken.IsCancellationRequested)
            {
                // Type should not be of operationcancelledexception otherwise this wont be retried
                resp.Ex = new Exception("Operation timed out");
            }
            else if (actualCancelToken.IsCancellationRequested)
            {
                resp.Ex = new OperationCanceledException(actualCancelToken);
            }
            else
            {
                resp.Ex = e;
            }
        }

        /// <summary>
        /// Handles WebException. Determines whether it is due to cancelled operation, remoteexception from server or some other webexception
        /// </summary>
        /// <param name="e">WebException instance</param>
        /// <param name="resp">OperationResponse</param>
        private static void HandleHttpRequestException(HttpRequestException e, OperationResponse resp)
        {
            if(e.InnerException is WebException)
            { 
                var webEx = (WebException)e.InnerException;
                switch (webEx.Status)
                {
                    case WebExceptionStatus.NameResolutionFailure:
                    case WebExceptionStatus.ServerProtocolViolation:
                    case WebExceptionStatus.ConnectFailure:
                    case WebExceptionStatus.ConnectionClosed:
                    case WebExceptionStatus.KeepAliveFailure:
                    case WebExceptionStatus.ReceiveFailure:
                    case WebExceptionStatus.SendFailure:
                    case WebExceptionStatus.Timeout:
                    case WebExceptionStatus.UnknownError:
                        resp.ConnectionFailure = true;
                        break;
                }
                resp.Ex = webEx;
            }
            else
            {
                resp.Ex = e;
            }
        }

        /// <summary>
        /// Verifies the responseData for the operation and initializes it if the encoding is chunked
        /// </summary>
        /// <param name="webResponse">HttpWebResponse</param>
        /// <param name="responseData">ResponseData structure</param>
        /// <param name="isResponseError">True when we are initializing error response stream else false</param>
        /// <returns>False if the response is not chunked but the content length is 0 else true</returns>
        private static bool InitializeResponseData(HttpResponseMessage webResponse, ref ByteBuffer responseData, bool isResponseError = false)
        {
            string encoding = webResponse.Headers.TransferEncoding.ToString();
            if (!string.IsNullOrEmpty(encoding) && encoding.Equals("chunked"))
            {
                // If the error response is from our FE, then it wont be chunked. If the error is from IIS
                // then it may be chunked. So assign a default size of the error response. Even if the remote error 
                // is not contained in that buffer size, its fine.
                if (isResponseError)
                {
                    responseData.Data = new byte[ErrorResponseDefaultLength];
                    responseData.Count = ErrorResponseDefaultLength;
                    responseData.Offset = 0;
                }
                //If it is chunked responseData should be instantiated and responseDataLength should be greater than 0, because we dont know the content length
                if (responseData.Data == null)
                {
                    throw new ArgumentNullException(nameof(responseData.Data));
                }
                if (responseData.Offset >= responseData.Data.Length || responseData.Offset < 0 ||
                    responseData.Count + responseData.Offset > responseData.Data.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(responseData.Offset));
                }
            }
            else
            {
                //Initialize the response based on content length property
                if (responseData.Data == null) //For OPEN operation the data might not be chunked
                {
                    if (webResponse.Content.Headers.ContentLength > 0)
                    {
                        responseData.Data = new byte[(int)webResponse.Content.Headers.ContentLength];
                        responseData.Offset = 0;
                        responseData.Count = (int)webResponse.Content.Headers.ContentLength;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        #region Async
        /// <summary>
        /// Makes a single Http call to the server, sends the request and obtains the response. This is a asynchronous call.
        /// </summary>
        /// <param name="opCode">Operation Code</param>
        /// <param name="path">Path of the file or directory</param>
        /// <param name="requestData">byte array, offset and length of the data of http request</param>
        /// <param name="responseData">byte array, offset and length of the data of http response. byte array should be initialized for chunked response</param>
        /// <param name="qp">Headers for request</param>
        /// <param name="client">ADLS Store CLient</param>
        /// <param name="req">Request options containing RetryOption, timout and requestid </param>
        /// <param name="resp">Contains the response message </param>
        /// <param name="cancelToken">CancellationToken to cancel the operation</param>
        /// <param name="customHeaders">Dictionary containing the custom header that Core wants to pass</param>
        /// <returns>Tuple of Byte array containing the bytes returned from the server and number of bytes read from server</returns>
        private static async Task<Tuple<byte[], int>> MakeSingleCallAsync(string opCode, string path,
            ByteBuffer requestData, ByteBuffer responseData, QueryParams qp, AdlsClient client, RequestOptions req, OperationResponse resp, CancellationToken cancelToken, IDictionary<string, string> customHeaders)
        {
            string token = null;
            Operation op = Operation.Operations[opCode];
            string urlString = CreateHttpRequestUrl(op, path, client, resp, qp.Serialize(opCode), req);
            if (string.IsNullOrEmpty(urlString))
            {
                return null;
            }

            if (client.WrapperStream != null && string.IsNullOrEmpty(client.ContentEncoding))
            {
                resp.Error = "WrapperStream is set, but Encoding string is not set";
                return null;
            }

            if (client.WrapperStream == null && !string.IsNullOrEmpty(client.ContentEncoding))
            {
                resp.Error = "Encoding string is set, but WrapperStream is not set";
                return null;
            }

            try
            {
                // Create does not throw WebException
                //HttpWebRequest webReq = (HttpWebRequest)WebRequest.Create(urlString);
                var webReq = new HttpRequestMessage() { RequestUri = new Uri(urlString) };

                //If operation is cancelled then stop
                cancelToken.ThrowIfCancellationRequested();
                // If security certificate is used then no need to pass token
                if (req.ClientCert != null)
                {
                    throw new NotImplementedException("Cert auth is not implemented. Add it to HttpClient");
                }

                Stopwatch watch = Stopwatch.StartNew();
                token = await client.GetTokenAsync(cancelToken).ConfigureAwait(false);
                watch.Stop();
                resp.TokenAcquisitionLatency = watch.ElapsedMilliseconds;
                if (string.IsNullOrEmpty(token))
                {
                    resp.Ex = new ArgumentException($"Token is null or empty.");
                    return null;
                }

                if (token.Length <= AuthorizationHeaderLengthThreshold)
                {
                    resp.Ex = new ArgumentException($"Token Length is {token.Length}. Token is most probably malformed.");
                    return null;
                }

                resp.AuthorizationHeaderLength = token.Length;
                AssignCommonHttpHeaders(webReq, client, req, token, op.Method, customHeaders, requestData.Count);
                using (var timeoutCancellationTokenSource = GetCancellationTokenSourceForTimeout(req))
                {
                    using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken, timeoutCancellationTokenSource.Token))
                    {
                        try
                        {
                            //This point onwards if operation is cancelled http request is aborted
                            linkedCts.Token.Register(OnCancel, webReq);

                            if (!op.Method.Equals(HttpMethod.Get))
                            {
                                if (op.RequiresBody && requestData.Data != null)
                                {
                                    HttpContent metaDataContent = new ByteArrayContent(requestData.Data, requestData.Offset, requestData.Count);
                                    webReq.Content = metaDataContent;
                                }
                            }

                            var webResponse = await client.HttpClient.SendAsync(webReq, linkedCts.Token).ConfigureAwait(false);

                            if (!webResponse.IsSuccessStatusCode)
                            {
                                HandleUnsuccessfullCall(webReq, webResponse, req, resp, path, token);
                            }

                            resp.HttpStatus = webResponse.StatusCode;
                            resp.HttpMessage = webResponse.ReasonPhrase;
                            resp.RequestId = webResponse.Headers.GetValues("x-ms-request-id").FirstOrDefault();
                            PostPowershellLogDetails(webReq, webResponse);
                            if (op.ReturnsBody)
                            {
                                if (!InitializeResponseData(webResponse, ref responseData))
                                {
                                    return null;
                                }

                                int totalBytes = 0;
                                using (Stream opStream = await webResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                {

                                    int noBytes;
                                    int totalLengthToRead = responseData.Count;
                                    //Read the required amount of data. In case of chunked it is what users requested, else it is amount of data sent
                                    do
                                    {
                                        noBytes = await opStream.ReadAsync(responseData.Data, responseData.Offset,
                                            totalLengthToRead, linkedCts.Token).ConfigureAwait(false);
                                        totalBytes += noBytes;
                                        responseData.Offset += noBytes;
                                        totalLengthToRead -= noBytes;

                                    } while (noBytes > 0 && totalLengthToRead > 0);
                                }

                                return
                                    Tuple.Create(responseData.Data,
                                        totalBytes); //Return the total bytes read also since in case of chunked amount of data returned can be less than data returned
                            }
                        }
                        catch(TaskCanceledException e)
                        {
                            HandleTaskCancelException(e, resp, timeoutCancellationTokenSource.Token);
                        }
                        catch (HttpRequestException e)
                        {
                            HandleHttpRequestException(e, resp);
                        }
                    }
                }

            }// Any unhandled exception is caught here
            catch (Exception e)
            {
                resp.Ex = cancelToken.IsCancellationRequested ? new OperationCanceledException(cancelToken) : e;
            }

            return null;
        }
        #endregion

        #region SYNC
        /// <summary>
        /// Makes a single Http call to the server, sends the request and obtains the response. This is a synchronous call.
        /// </summary>
        /// <param name="opCode">Operation Code</param>
        /// <param name="path">Path of the file or directory</param>
        /// <param name="requestData">byte array, offset and length of the data of http request</param>
        /// <param name="responseData">byte array, offset and length of the data of http response. byte array should be initialized for chunked response</param>
        /// <param name="qp">Headers for request</param>
        /// <param name="client">ADLS Store CLient</param>
        /// <param name="req">Request options containing RetryOption, timout and requestid </param>
        /// <param name="resp">Contains the response message </param>
        /// <param name="customHeaders">Dictionary containing the custom header that Core wants to pass</param>
        /// <returns>Tuple of Byte array containing the bytes returned from the server and number of bytes read from server</returns>
        private static Tuple<byte[], int> MakeSingleCall(string opCode, string path, ByteBuffer requestData, ByteBuffer responseData, QueryParams qp, AdlsClient client, RequestOptions req, OperationResponse resp, IDictionary<string, string> customHeaders)
        {
            string token = null;
            Operation op = Operation.Operations[opCode];
            string urlString = CreateHttpRequestUrl(op, path, client, resp, qp.Serialize(opCode), req);
            if (string.IsNullOrEmpty(urlString))
            {
                return null;
            }

            try
            {
                // Create does not throw WebException
                //HttpWebRequest webReq = (HttpWebRequest)WebRequest.Create(urlString

                var webReq = new HttpRequestMessage() { RequestUri = new Uri(urlString) };

                // If security certificate is used then no need to pass token
                if (req.ClientCert != null)
                {
                    throw new NotImplementedException("Cert auth is not implemented. Add it to HttpClient");
                }

                Stopwatch watch = Stopwatch.StartNew();
                token = client.GetTokenAsync().GetAwaiter().GetResult();
                watch.Stop();
                resp.TokenAcquisitionLatency = watch.ElapsedMilliseconds;
                if (string.IsNullOrEmpty(token))
                {
                    resp.Ex = new ArgumentException($"Token is null or empty.");
                    return null;
                }

                if (token.Length <= AuthorizationHeaderLengthThreshold)
                {
                    resp.Ex = new ArgumentException($"Token Length is {token.Length}. Token is most probably malformed.");
                    return null;
                }

                resp.AuthorizationHeaderLength = token.Length;
                AssignCommonHttpHeaders(webReq, client, req, token, op.Method, customHeaders, requestData.Count);
                using (CancellationTokenSource timeoutCancellationTokenSource = GetCancellationTokenSourceForTimeout(req))
                {
                    try
                    {
                        //This point onwards if operation is cancelled http request is aborted
                        timeoutCancellationTokenSource.Token.Register(OnCancel, webReq);
                        if (!op.Method.Equals(HttpMethod.Get))
                        {
                            if (op.RequiresBody && requestData.Data != null)
                            {
                                HttpContent metaDataContent = new ByteArrayContent(requestData.Data, requestData.Offset, requestData.Count);
                                webReq.Content = metaDataContent;
                            }
                        }

                        var webResponse = client.HttpClient.SendAsync(webReq, timeoutCancellationTokenSource.Token).GetAwaiter().GetResult();

                        if (!webResponse.IsSuccessStatusCode)
                        {
                            HandleUnsuccessfullCall(webReq, webResponse, req, resp, path, token);
                        }

                        resp.HttpStatus = webResponse.StatusCode;
                        resp.HttpMessage = webResponse.ReasonPhrase;
                        resp.RequestId = webResponse.Headers.GetValues("x-ms-request-id").FirstOrDefault();
                        PostPowershellLogDetails(webReq, webResponse);
                        if (op.ReturnsBody)
                        {
                            if (!InitializeResponseData(webResponse, ref responseData))
                            {
                                return null;
                            }

                            int totalBytes = 0;
                            using (Stream opStream = webResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                            {

                                int noBytes;
                                int totalLengthToRead = responseData.Count;
                                //Read the required amount of data. In case of chunked it is what users requested, else it is amount of data sent
                                do
                                {
                                    noBytes = opStream.Read(responseData.Data, responseData.Offset, totalLengthToRead);
                                    totalBytes += noBytes;
                                    responseData.Offset += noBytes;
                                    totalLengthToRead -= noBytes;

                                } while (noBytes > 0 && totalLengthToRead > 0);
                            }

                            return Tuple.Create(responseData.Data, totalBytes);
                            //Return the total bytes read also since in case of chunked amount of data returned can be less than data returned
                        }
                    }
                    catch (TaskCanceledException e)
                    {
                        HandleTaskCancelException(e, resp, timeoutCancellationTokenSource.Token);
                    }
                    catch (HttpRequestException e)
                    {
                        HandleHttpRequestException(e, resp);
                    }
                }
            }// Any unhandled exception is caught here
            catch (Exception e)
            {
                resp.Ex = e;

            }

            return null;
        }
        #endregion
    }
}
