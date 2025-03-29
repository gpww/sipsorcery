using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SpeechHelper;

namespace WebRTCSendAudio
{
    public class WavFileSender3
    {
        private readonly AudioExtrasSource _audioExtrasSource;

        public WavFileSender3(AudioExtrasSource audioExtrasSource)
        {
            _audioExtrasSource = audioExtrasSource;
        }

        public async Task SendWavFileAsync(string filePath, int sampleRate = 16000)
        {
            using (var reader = new WaveFileReader(filePath))
            {
                var waveFormat = reader.WaveFormat;
                var buffer = new byte[waveFormat.AverageBytesPerSecond / 10]; // 100ms buffer
                int bytesRead;

                using (var memoryStream = new MemoryStream())
                {
                    while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        if (waveFormat.SampleRate != sampleRate)
                        {
                            var data = buffer.Take(bytesRead).ToArray();
                            var resampleData = AudioFormatHelper.Resample(data, waveFormat.SampleRate, sampleRate);
                            await memoryStream.WriteAsync(resampleData);
                        }
                        else
                        {
                            await memoryStream.WriteAsync(buffer, 0, bytesRead);
                        }
                    }

                    memoryStream.Position = 0;
                    await _audioExtrasSource.SendAudioFromStream(memoryStream, AudioSamplingRatesEnum.Rate16KHz);
                }
            }
        }

        private AudioSamplingRatesEnum ConvertWaveFormatToSamplingRate(WaveFormat waveFormat)
        {
            return waveFormat.SampleRate switch
            {
                8000 => AudioSamplingRatesEnum.Rate8KHz,
                16000 => AudioSamplingRatesEnum.Rate16KHz,
                _ => throw new NotSupportedException($"Unsupported sample rate: {waveFormat.SampleRate}")
            };
        }
    }
}
