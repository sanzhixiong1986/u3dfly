#if !BESTHTTP_DISABLE_SIGNALR_CORE
using System;
using System.Collections.Concurrent;
using System.Threading;

using BestHTTP.PlatformSupport.Memory;
using BestHTTP.Core;
using BestHTTP.Extensions;

namespace BestHTTP.SignalRCore.Transports
{
    /// <summary>
    /// LongPolling transport implementation.
    /// https://github.com/aspnet/AspNetCore/blob/master/src/SignalR/docs/specs/TransportProtocols.md#http-post-client-to-server-only
    /// https://github.com/aspnet/AspNetCore/blob/master/src/SignalR/docs/specs/TransportProtocols.md#long-polling-server-to-client-only
    /// </summary>
    internal sealed class LongPollingTransport : TransportBase
    {
        /// <summary>
        /// Maximum retries for a failed request
        /// </summary>
        const int MaxRetries = 6;

        public override TransportTypes TransportType { get { return TransportTypes.LongPolling; } }

        /// <summary>
        /// Polling transport can't send out a new send-messages request until the previous isn't finished, so it must cache new ones.
        /// </summary>
        private ConcurrentQueue<BufferSegment> outgoingMessages = new ConcurrentQueue<BufferSegment>();

        /// <summary>
        /// Flag indicating that a send-request is already sent out. We have to cache messages (<see cref="outgoingMessages"/>) until the request finishes.
        /// </summary>
        private int sendingInProgress;

        /// <summary>
        /// Cached stream instance. By using a <see cref="BufferSegmentStream"/> we can avoid allocating a large byte[] for the cached messages and copy bytes to the new array.
        /// </summary>
        private BufferSegmentStream stream = new BufferSegmentStream();

        internal LongPollingTransport(HubConnection con)
            :base(con)
        {
        }

        #region ITransport methods

        public override void StartConnect()
        {
            if (this.State != TransportStates.Initial)
                return;

            HTTPManager.Logger.Information("LongPollingTransport", "StartConnect");

            this.State = TransportStates.Connecting;

            // https://github.com/aspnet/SignalR/blob/dev/specs/HubProtocol.md#overview
            // When our connection is open, send the 'negotiation' message to the server.
            
            var request = new HTTPRequest(BuildUri(this.connection.Uri), HTTPMethods.Post, OnHandshakeRequestFinished);

            this.stream.Write(JsonProtocol.WithSeparator(string.Format("{{\"protocol\":\"{0}\", \"version\": 1}}", this.connection.Protocol.Encoder.Name)));

            request.UploadStream = this.stream;

            if (this.connection.AuthenticationProvider != null)
                this.connection.AuthenticationProvider.PrepareRequest(request);

            request.Send();
        }

        public override void Send(BufferSegment msg)
        {
            if (this.State != TransportStates.Connected)
                return;

            outgoingMessages.Enqueue(msg);
            
            if (Interlocked.CompareExchange(ref this.sendingInProgress, 1, 0) == 1)
                return;

            SendMessages();
        }

        public override void StartClose()
        {
            if (this.State != TransportStates.Connected)
                return;

            HTTPManager.Logger.Information("LongPollingTransport", "StartClose");

            this.State = TransportStates.Closing;

            SendConnectionCloseRequest();
        }

        #endregion

        #region Private Helper methods

        private void SendMessages()
        {
            if (this.State != TransportStates.Connected || this.outgoingMessages.Count == 0)
                return;

            var request = new HTTPRequest(BuildUri(this.connection.Uri), HTTPMethods.Post, OnSendMessagesFinished);

            BufferSegment buffer;
            while (this.outgoingMessages.TryDequeue(out buffer))
                this.stream.Write(buffer);

            request.UploadStream = this.stream;

            request.Tag = 0;

            if (this.connection.AuthenticationProvider != null)
                this.connection.AuthenticationProvider.PrepareRequest(request);

            request.Send();
        }

        private void DoPoll()
        {
            if (this.State != TransportStates.Connected)
                return;

            HTTPManager.Logger.Information("LongPollingTransport", "Sending Poll request");

            var request = new HTTPRequest(BuildUri(this.connection.Uri), OnPollRequestFinished);

            request.AddHeader("Accept", " application/octet-stream");
            request.Timeout = TimeSpan.FromMinutes(2);

            if (this.connection.AuthenticationProvider != null)
                this.connection.AuthenticationProvider.PrepareRequest(request);

            request.Send();
        }

        private void SendConnectionCloseRequest(int retryCount = 0)
        {
            if (this.State != TransportStates.Closing)
                return;

            HTTPManager.Logger.Information("LongPollingTransport", "Sending DELETE request");

            var request = new HTTPRequest(BuildUri(this.connection.Uri), HTTPMethods.Delete, OnConnectionCloseRequestFinished);
            request.Tag = retryCount;

            if (this.connection.AuthenticationProvider != null)
                this.connection.AuthenticationProvider.PrepareRequest(request);

            request.Send();
        }

        #endregion

        #region Callbacks

        private void OnHandshakeRequestFinished(HTTPRequest req, HTTPResponse resp)
        {
            switch (req.State)
            {
                // The request finished without any problem.
                case HTTPRequestStates.Finished:
                    if (resp.IsSuccess)
                    {
                        // This will trigger the OnConnected event event of the HubConnection
                        this.State = TransportStates.Connected;

                        // so calling SendMessages() right after it seems to be a very good idea
                        SendMessages();

                        DoPoll();
                    }
                    else
                    {
                        this.ErrorReason = string.Format("Handshake Request finished Successfully, but the server sent an error. Status Code: {0}-{1} Message: {2}",
                                                        resp.StatusCode,
                                                        resp.Message,
                                                        resp.DataAsText);
                    }
                    break;

                // The request finished with an unexpected error. The request's Exception property may contain more info about the error.
                case HTTPRequestStates.Error:
                    this.ErrorReason = "Handshake Request Finished with Error! " + (req.Exception != null ? (req.Exception.Message + "\n" + req.Exception.StackTrace) : "No Exception");
                    break;

                // The request aborted, initiated by the user.
                case HTTPRequestStates.Aborted:
                    this.ErrorReason = "Handshake Request Aborted!";
                    break;

                // Connecting to the server is timed out.
                case HTTPRequestStates.ConnectionTimedOut:
                    this.ErrorReason = "Handshake - Connection Timed Out!";
                    break;

                // The request didn't finished in the given time.
                case HTTPRequestStates.TimedOut:
                    this.ErrorReason = "Handshake - Processing the request Timed Out!";
                    break;
            }

            if (!string.IsNullOrEmpty(this.ErrorReason))
                this.State = TransportStates.Failed;
        }

        private void OnSendMessagesFinished(HTTPRequest req, HTTPResponse resp)
        {
            /*
             * The HTTP POST request is made to the URL [endpoint-base]. The mandatory id query string value is used to identify the connection to send to.
             * If there is no id query string value, a 400 Bad Request response is returned. Upon receipt of the entire payload,
             * the server will process the payload and responds with 200 OK if the payload was successfully processed.
             * If a client makes another request to / while an existing request is outstanding, the new request is immediately terminated by the server with the 409 Conflict status code.
             * 
             * If a client receives a 409 Conflict request, the connection remains open.
             * Any other response indicates that the connection has been terminated due to an error.
             * 
             * If the relevant connection has been terminated, a 404 Not Found status code is returned.
             * If there is an error instantiating an EndPoint or dispatching the message, a 500 Server Error status code is returned.
             * */
            switch (req.State)
            {
                // The request finished without any problem.
                case HTTPRequestStates.Finished:
                    switch(resp.StatusCode)
                    {
                        // Upon receipt of the entire payload, the server will process the payload and responds with 200 OK if the payload was successfully processed.
                        case 200:
                            Interlocked.Exchange(ref this.sendingInProgress, 0);

                            SendMessages();

                            break;

                        // Any other response indicates that the connection has been terminated due to an error.
                        default:
                            this.ErrorReason = string.Format("Send Request finished Successfully, but the server sent an error. Status Code: {0}-{1} Message: {2}",
                                                            resp.StatusCode,
                                                            resp.Message,
                                                            resp.DataAsText);
                            break;
                    }
                    break;

                default:
                    int retryCount = (int)req.Tag;
                    if (retryCount < MaxRetries)
                    {
                        req.Tag = retryCount + 1;
                        RequestEventHelper.EnqueueRequestEvent(new RequestEventInfo(req, RequestEvents.Resend));
                    }
                    else
                    {
                        this.ErrorReason = string.Format("Send message reached max retry count ({0})!", MaxRetries);
                    }
                    break;
            }
        }

        private void OnPollRequestFinished(HTTPRequest req, HTTPResponse resp)
        {
            /*
             * When data is available, the server responds with a body in one of the two formats below (depending upon the value of the Accept header).
             * The response may be chunked, as per the chunked encoding part of the HTTP spec.
             * 
             * If the id parameter is missing, a 400 Bad Request response is returned.
             * If there is no connection with the ID specified in id, a 404 Not Found response is returned.
             *
             * When the client has finished with the connection, it can issue a DELETE request to [endpoint-base] (with the id in the query string) to gracefully terminate the connection.
             * The server will complete the latest poll with 204 to indicate that it has shut down.
             * */
            switch (req.State)
            {
                // The request finished without any problem.
                case HTTPRequestStates.Finished:
                    switch(resp.StatusCode)
                    {
                        case 200:
                            // Parse and dispatch messages only if the transport is still in connected state
                            if (this.State == TransportStates.Connected)
                            {
                                this.messages.Clear();
                                try
                                {
                                    this.connection.Protocol.ParseMessages(resp.Data, ref this.messages);

                                    this.connection.OnMessages(this.messages);
                                }
                                catch (Exception ex)
                                {
                                    HTTPManager.Logger.Exception("LongPollingTransport", "OnMessage(byte[])", ex);
                                }
                                finally
                                {
                                    this.messages.Clear();

                                    DoPoll();
                                }
                            }
                            else if (this.State == TransportStates.Closing)
                            {
                                // DELETE message sent out while we received the poll result. We can close the transport at this point as we don't want to send out a new poll request.
                                this.State = TransportStates.Closed;
                            }
                            break;

                        case 204:
                            this.State = TransportStates.Closed;
                            break;

                        case 400:
                        case 404:
                            if (this.State == TransportStates.Closing)
                            {
                                this.State = TransportStates.Closed;
                            }
                            else if (this.State != TransportStates.Closed)
                            {
                                this.ErrorReason = resp.DataAsText;
                                this.State = TransportStates.Failed;
                            }
                            break;

                        default:
                            this.ErrorReason = string.Format("Poll Request finished Successfully, but the server sent an error. Status Code: {0}-{1} Message: {2}",
                                                            resp.StatusCode,
                                                            resp.Message,
                                                            resp.DataAsText);
                            break;
                    }
                    break;

                default:
                    if (this.State == TransportStates.Closing)
                        this.State = TransportStates.Closed;
                    else if (this.State != TransportStates.Closed)
                        DoPoll();
                    break;
            }
        }

        private void OnConnectionCloseRequestFinished(HTTPRequest req, HTTPResponse resp)
        {
            switch (req.State)
            {
                // The request finished without any problem.
                case HTTPRequestStates.Finished:
                    if (resp.IsSuccess)
                    {
                        return;
                    }
                    else
                    {
                        HTTPManager.Logger.Warning("LongPollingTransport", string.Format("Connection Close Request finished Successfully, but the server sent an error. Status Code: {0}-{1} Message: {2}",
                                                        resp.StatusCode,
                                                        resp.Message,
                                                        resp.DataAsText));
                    }
                    break;

                // The request finished with an unexpected error. The request's Exception property may contain more info about the error.
                case HTTPRequestStates.Error:
                    HTTPManager.Logger.Warning("LongPollingTransport", "Connection Close Request Finished with Error! " + (req.Exception != null ? (req.Exception.Message + "\n" + req.Exception.StackTrace) : "No Exception"));
                    break;

                // The request aborted, initiated by the user.
                case HTTPRequestStates.Aborted:
                    HTTPManager.Logger.Warning("LongPollingTransport", "Connection Close Request Aborted!");
                    break;

                // Connecting to the server is timed out.
                case HTTPRequestStates.ConnectionTimedOut:
                    HTTPManager.Logger.Warning("LongPollingTransport", "Connection Close - Connection Timed Out!");
                    break;

                // The request didn't finished in the given time.
                case HTTPRequestStates.TimedOut:
                    HTTPManager.Logger.Warning("LongPollingTransport", "Connection Close - Processing the request Timed Out!");
                    break;
            }

            int retryCount = (int)req.Tag;
            if (retryCount <= MaxRetries)
            {
                // Try again if there were an error
                SendConnectionCloseRequest(retryCount + 1);
            }
            else
            {
                this.State = TransportStates.Closed;
            }
        }

        #endregion
    }
}
#endif
