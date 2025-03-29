using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SpeechHelper;

namespace WebRTCSendAudio;

public class WavFileSender : PCMSender
{
    public WavFileSender(RTPSession rtpSession) : base(rtpSession)
    {

    }

    public async Task SendWavFileAsync(string filePath)
    {
        var speaker = new SpeakerOutput(AudioEncodingFormat.Pcm, SendAudioFormat.ClockRate);

        using (var reader = new WaveFileReader(filePath))
        {
            var waveFormat = reader.WaveFormat;

            var buffer = new byte[waveFormat.AverageBytesPerSecond / 10]; // 100ms buffer
            int bytesRead;

            while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var data = buffer.Take(bytesRead).ToArray();
                SendData(data, waveFormat.SampleRate);
                await Task.Delay(AUDIO_SAMPLE_PERIOD_MILLISECONDS_DEFAULT);
            }
        }
    }
}
