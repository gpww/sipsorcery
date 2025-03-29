using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace WebRTCSendAudio
{
    public class WavFileSender1
    {
        private readonly AudioExtrasSource _audioExtrasSource;

        public WavFileSender1(AudioExtrasSource audioExtrasSource)
        {
            _audioExtrasSource = audioExtrasSource;
        }

        public async Task SendWavFileAsync(string filePath, int targetSampleRate = 16000)
        {
            try
            {
                using (var reader = new WaveFileReader(filePath))
                {
                    // 将音频转换为目标格式：PCM 16 位、单声道、目标采样率
                    var targetFormat = new WaveFormat(targetSampleRate, 16, 1);

                    using (var conversionStream = new WaveFormatConversionStream(targetFormat, reader))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            await conversionStream.CopyToAsync(memoryStream);

                            memoryStream.Position = 0;
                            var audioSamplingRate = ConvertSampleRateToEnum(targetSampleRate);
                            await _audioExtrasSource.SendAudioFromStream(memoryStream, audioSamplingRate);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送 WAV 文件时出错: {ex.Message}");
                throw;
            }
        }

        private AudioSamplingRatesEnum ConvertSampleRateToEnum(int sampleRate)
        {
            return sampleRate switch
            {
                8000 => AudioSamplingRatesEnum.Rate8KHz,
                16000 => AudioSamplingRatesEnum.Rate16KHz,
                _ => throw new NotSupportedException($"不支持的采样率: {sampleRate}")
            };
        }
    }
}
