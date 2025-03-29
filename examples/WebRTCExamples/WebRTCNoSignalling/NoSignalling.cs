//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: This example is the same as the WebRTCTestPatternServer example
// except that it can be used without requiring a web socket signalling channel.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Concentus;
using Concentus.Structs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using Newtonsoft.Json;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.Windows;

namespace WebRTCServer
{
    class NoSignalling
    {
        //stun:stun.miwifi.com
        //stun:stun.qq.com
        private const string STUN_URL = "stun:stun.qq.com";
        //private const string STUN_URL = "stun:stun.sipsorcery.com";
        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main()
        {
            await Wait4Answer();
            //await CreateOffer();
        }
        static async Task CreateOffer()
        {
            Console.WriteLine("WebRTC No Signalling Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            logger = AddConsoleLogger();

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            var pc = CreatePeerConnection();

            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
            Console.WriteLine("THE SDP OFFER BELOW NEEDS TO BE PASTED INTO YOUR BROWSER");
            Console.WriteLine();

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            var offerSerialised = Newtonsoft.Json.JsonConvert.SerializeObject(offerSdp,
                 new Newtonsoft.Json.Converters.StringEnumConverter());
            var offerBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(offerSerialised));

            Console.WriteLine(offerBase64);

            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
            Console.WriteLine("THE SDP ANSWER FROM THE BROWSER NEEDS TO PASTED BELOW");
            Console.WriteLine();

            string remoteAnswerOrOfferB64 = null;
            while (string.IsNullOrWhiteSpace(remoteAnswerOrOfferB64))
            {
                Console.Write("=> ");
                remoteAnswerOrOfferB64 = Console.ReadLine();
            }

            if (remoteAnswerOrOfferB64 == "q")
            {
                Console.WriteLine("Quitting.");
            }
            else
            {
                var remoteSdp = Encoding.UTF8.GetString(Convert.FromBase64String(remoteAnswerOrOfferB64));
                var remoteSdpInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(remoteSdp);

                HandleAnswer(exitMre, pc, remoteAnswerOrOfferB64);
            }
        }
        static async Task Wait4Answer()
        {
            Console.WriteLine("WebRTC No Signalling Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            logger = AddConsoleLogger();

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            var pc = CreatePeerConnection();

            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
            Console.WriteLine("THE SDP Offer FROM THE BROWSER NEEDS TO PASTED BELOW");
            Console.WriteLine();

            string remoteOfferB64 = null;
            while (string.IsNullOrWhiteSpace(remoteOfferB64))
            {
                Console.Write("=> ");
                remoteOfferB64 = Console.ReadLine();
            }

            if (remoteOfferB64 == "q")
            {
                Console.WriteLine("Quitting.");
            }
            else
            {
                var remoteSdp = Encoding.UTF8.GetString(Convert.FromBase64String(remoteOfferB64));
                var remoteSdpInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(remoteSdp);

                HandleOffer(exitMre, pc, remoteOfferB64);
            }
        }
        private static void HandleAnswer(ManualResetEvent exitMre, RTCPeerConnection pc, string remoteAnswerB64)
        {
            string remoteAnswer = Encoding.UTF8.GetString(Convert.FromBase64String(remoteAnswerB64));
            Console.WriteLine($"Remote answer: {remoteAnswer}");

            RTCSessionDescriptionInit answerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(remoteAnswer);
            pc.setRemoteDescription(answerInit);

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            Console.WriteLine("Closing.");
            pc.Close("normal");

            Task.Delay(1000).Wait();
        }
        private static void HandleOffer(ManualResetEvent exitMre, RTCPeerConnection pc, string remoteOfferB64)
        {
            string remoteOffer = Encoding.UTF8.GetString(Convert.FromBase64String(remoteOfferB64));
            Console.WriteLine($"Remote offer: {remoteOffer}");

            RTCSessionDescriptionInit offerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(remoteOffer);
            pc.setRemoteDescription(offerInit);

            // Create an answer to the remote offer
            var answerSdp = pc.createAnswer();
            pc.setLocalDescription(answerSdp);

            var answerSerialised = JsonConvert.SerializeObject(answerSdp, new Newtonsoft.Json.Converters.StringEnumConverter());
            var answerBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(answerSerialised));

            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
            Console.WriteLine("THE SDP ANSWER BELOW NEEDS TO BE PASTED INTO YOUR BROWSER");
            Console.WriteLine();
            Console.WriteLine(answerBase64);
            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            Console.WriteLine("Closing.");
            pc.Close("normal");

            Task.Delay(1000).Wait();
        }

        private static RTCPeerConnection CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var peerConnection = new RTCPeerConnection(config, 0, new PortRange(61000, 65000));

            //var testPatternSource = new VideoTestPatternSource();
            //WindowsVideoEndPoint windowsVideoEndPoint = new WindowsVideoEndPoint(new VpxVideoEncoder());
            //MediaStreamTrack track = new MediaStreamTrack(windowsVideoEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            //peerConnection.addTrack(track);
            //testPatternSource.OnVideoSourceRawSample += windowsVideoEndPoint.ExternalVideoSourceRawSample;
            //windowsVideoEndPoint.OnVideoSourceEncodedSample += peerConnection.SendVideo;

            // Sink (speaker) only audio end point.
            WindowsAudioEndPoint windowsAudioEP = new(new AudioEncoder(includeOpus: true), -1, -1, true, false);
            //windowsAudioEP.RestrictFormats(x => x.FormatName == "OPUS");
            windowsAudioEP.OnAudioSinkError += err => logger.LogWarning($"Audio sink error. {err}.");

            MediaStreamTrack audioTrack = new(windowsAudioEP.GetAudioSinkFormats(), MediaStreamStatusEnum.SendRecv);
            peerConnection.addTrack(audioTrack);

            peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
            {
                var format = audioFormats.First();
                windowsAudioEP.SetAudioSinkFormat(format);
                Console.WriteLine("OnAudioFormatsNegotiated: {0}", format.FormatName);
            };
            peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");

                if (media == SDPMediaTypesEnum.audio)
                {
                    windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
                }
            };

            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change {state}.");
            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.OnTimeout += (mediaType) => logger.LogWarning($"Timeout on {mediaType}.");
            peerConnection.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                {
                    peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    peerConnection.OnSendReport -= RtpSession_OnSendReport;

                    //await windowsVideoEndPoint.CloseVideo();
                    //await testPatternSource.CloseVideo();
                    await windowsAudioEP.CloseAudio();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    //await testPatternSource.StartVideo();
                    //await windowsVideoEndPoint.StartVideo();
                    Console.WriteLine();
                    await windowsAudioEP.StartAudioSink();
                }
            };

            return peerConnection;
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
                var rr = recvRtcpReport.ReceiverReport?.ReceptionReports.FirstOrDefault();
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
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
            return factory.CreateLogger<NoSignalling>();
        }
    }
}
