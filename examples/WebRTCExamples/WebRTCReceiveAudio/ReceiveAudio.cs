//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that can receive the audio stream
// from a WebRTC peer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;
using Silver;
using SIPSorceryMedia.Abstractions;
using SpeechHelper;
using Microsoft.CognitiveServices.Speech;

namespace demo
{
    class ReceiveAudio
    {
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static WebSocketServer _webSocketServer;

        static void Main()
        {
            Console.WriteLine("WebRTC Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, false);
            _webSocketServer.AddWebSocketService<SDPExchange>("/", (sdpExchanger) =>
            {
                sdpExchanger.WebSocketOpened += SendSDPOffer;
                sdpExchanger.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static async Task<RTCPeerConnection> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            var peerConnection = new RTCPeerConnection(null);

            var audioEncoder = new AudioEncoder(includeOpus: true);
            // Sink (speaker) only audio end point.
            WindowsAudioEndPoint windowsAudioEP = new(audioEncoder, -1, -1, true, false);
            //windowsAudioEP.RestrictFormats(x => x.FormatName == "OPUS");
            windowsAudioEP.OnAudioSinkError += err => logger.LogWarning($"Audio sink error. {err}.");

            Console.WriteLine(StringHelper.LineSeparator);
            Console.WriteLine("WindowsAudioEP中支持的格式：");
            foreach (var format in windowsAudioEP.GetAudioSourceFormats())
            {
                Console.WriteLine(format.FormatName);
            }
            Console.WriteLine(StringHelper.LineSeparator);

            MediaStreamTrack audioTrack = new MediaStreamTrack(windowsAudioEP.GetAudioSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(audioTrack);

            SpeakerOutput speakerOutput = new SpeakerOutput(SpeechSynthesisOutputFormat.Raw8Khz16BitMonoPcm);
            AudioFormat audioFormatNegotiated = new AudioFormat();
            peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
            {
                audioFormatNegotiated = audioFormats.First();
                windowsAudioEP.SetAudioSinkFormat(audioFormatNegotiated);
            };
            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection connected changed to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    await windowsAudioEP.StartAudioSink();
                }
                else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                {
                    peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    peerConnection.OnSendReport -= RtpSession_OnSendReport;

                    await windowsAudioEP.CloseAudio();
                }
            };

            peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");

                if (media == SDPMediaTypesEnum.audio)
                {
                    var data = audioEncoder.DecodeAudio(rtpPkt.Payload, audioFormatNegotiated);
                    var bytes = AudioEncoder.ShortsToBytes(data);
                    speakerOutput.EnqueueForPlayback(bytes);
                    //windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
                }
            };

            var offerSdp = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");
            logger.LogDebug(offerSdp.sdp);

            context.WebSocket.Send(offerSdp.sdp);

            return peerConnection;
        }

        private static void WebSocketMessageReceived(RTCPeerConnection peerConnection, string message)
        {
            try
            {
                if (peerConnection.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    peerConnection.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });

                    // 打印最终协商的音频格式
                    //var negotiatedAudioFormat = GetNegotiatedAudioFormat(peerConnection);
                    //Console.WriteLine($"最终协商的音频格式：{negotiatedAudioFormat}");

                    // 解析并打印浏览器发送过来的支持音频格式
                    var remoteAudioFormats = GetRemoteAudioFormats(peerConnection);
                    Console.WriteLine("浏览器支持的音频格式：");
                    foreach (var format in remoteAudioFormats)
                    {
                        Console.WriteLine(format);
                    }
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);
                    peerConnection.addIceCandidate(new RTCIceCandidateInit { candidate = message });
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
        }

        // 扩展 RTCPeerConnection 类以获取协商的音频格式
        private static string GetNegotiatedAudioFormat(RTCPeerConnection peerConnection)
        {
            var audioMedia = peerConnection.RemoteDescription?.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.audio);
            if (audioMedia != null)
            {
                SDPAudioVideoMediaFormat? format = audioMedia.MediaFormats.Values.FirstOrDefault();
                return format?.ToAudioFormat().FormatName;
            }
            return null;
        }

        // 新增方法，用于获取浏览器发送过来的支持音频格式
        private static List<string> GetRemoteAudioFormats(RTCPeerConnection peerConnection)
        {
            var audioFormats = new List<string>();
            var remoteDescription = peerConnection.remoteDescription;
            if (remoteDescription != null)
            {
                var sdp = remoteDescription.sdp;
                foreach (var format in sdp.Media[0].MediaFormats.Values)
                {
                    Console.WriteLine(format.Rtpmap);
                    audioFormats.Add(format.Rtpmap);
                }
                //var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                //foreach (var line in lines)
                //{
                //    if (line.StartsWith("m=audio"))
                //    {
                //        var parts = line.Split(' ');
                //        for (int i = 3; i < parts.Length; i++)
                //        {
                //            audioFormats.Add(parts[i]);
                //        }
                //        break;
                //    }
                //}
            }
            return audioFormats;
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP sent BYE {mediaType}.");
            }
            else if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                logger.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                if (sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    logger.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    logger.LogDebug($"RTCP sent RR {mediaType}, no packets sent or received.");
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            if (recvRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP recv BYE {mediaType}.");
            }
            else
            {
                var rr = recvRtcpReport.ReceiverReport?.ReceptionReports?.FirstOrDefault();
                if (rr != null)
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<ReceiveAudio>();
        }
    }
}
