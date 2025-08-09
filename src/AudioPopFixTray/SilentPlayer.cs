using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioPopFixTray
{
    public class SilentPlayer : IDisposable
    {
        private readonly MMDevice _device;
        private WasapiOut? _out;
        private IWaveProvider? _source;

        public SilentPlayer(MMDevice device)
        {
            _device = device;
        }

        public void Start()
        {
            Stop();

            // 48k stereo silence to match common system rate
            var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            _source = new SilenceProvider(format); // SilenceProvider already implements IWaveProvider

            _out = new WasapiOut(_device, AudioClientShareMode.Shared, true, 20);
            _out.Init(_source);
            _out.Play();
            Debug.WriteLine($"Started silent stream on: {_device.FriendlyName}");
        }

        public void Stop()
        {
            if (_out != null)
            {
                try { _out.Stop(); } catch { }
                _out.Dispose();
                _out = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
