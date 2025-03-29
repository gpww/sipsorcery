using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SpeechHelper;

namespace WebRTCSendAudio;

public class PCMSender
{
    public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_DEFAULT = 20;
    public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_MIN = 20;
    public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_MAX = 500;

    protected readonly RTPSession _rtpSession;
    public AudioFormat SendAudioFormat { get; set; }
    protected readonly AudioEncoder _audioEncoder;

    public PCMSender(RTPSession rtpSession) 
    {
        _rtpSession = rtpSession;
        _audioEncoder = new AudioEncoder(includeOpus: true);
    }
    protected uint CalculateRtpTimestampIncrement(int averageBytesPerSecond, int bytesRead)
    {
        // 根据编码格式计算 RTP 时间戳增量
        if (SendAudioFormat.Codec == AudioCodecsEnum.OPUS)
        {
            // Opus 使用 48kHz 采样率
            return (uint)(48000 * bytesRead / averageBytesPerSecond);
        }
        else if (SendAudioFormat.Codec == AudioCodecsEnum.G722)
        {
            // G722 使用 16kHz 采样率
            return (uint)(16000 * bytesRead / averageBytesPerSecond);
        }
        else if (SendAudioFormat.Codec == AudioCodecsEnum.G729)
        {
            // G729 使用 8kHz 采样率
            return (uint)(8000 * bytesRead / averageBytesPerSecond);
        }
        else
        {
            // 默认使用原始采样率
            return (uint)(waveFormat.SampleRate * bytesRead / averageBytesPerSecond);
        }
    }

    public void SendData(byte[] data, int sampleRate)
    {
        var resampleData = AudioFormatHelper.Resample(data, sampleRate, SendAudioFormat.ClockRate);

        var shortBuffer = AudioEncoder.BytesToShorts(resampleData);
        var encodedBuffer = _audioEncoder.EncodeAudio(shortBuffer, SendAudioFormat);

        // 计算 RTP 时间戳
        var averageBytesPerSecond = CalculateAverageBytesPerSecond(sampleRate);
        uint durationRtpUnits = CalculateRtpTimestampIncrement(averageBytesPerSecond, data.Length);
        // 发送音频数据
        _rtpSession.SendAudio(durationRtpUnits, encodedBuffer);
    }
    public static int CalculateAverageBytesPerSecond(int sampleRate, int channels = 1, int bitsPerSample = 16)
    {
        return sampleRate * channels * (bitsPerSample / 8);
    }
}
