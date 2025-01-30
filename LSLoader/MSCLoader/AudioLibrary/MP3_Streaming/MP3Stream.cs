﻿#if !Mini
using NAudio.Wave;
using System;
using System.IO;
using System.Net;
using System.Threading;

namespace AudioLibrary.MP3_Streaming
{
    internal partial class MP3Stream : IDisposable
    {
        public enum StreamingPlaybackState
        {
            Stopped,
            Playing,
            Buffering,
            Paused
        }
        public string buffer_info, song_info;
        public BufferedWaveProvider bufferedWaveProvider;
        public bool decomp = false;
        public AudioSource audioSource;

        public volatile StreamingPlaybackState playbackState;
        private volatile bool fullyDownloaded;
        private HttpWebRequest webRequest;
        private bool subbedToEvent = false;
        delegate void ShowErrorDelegate(string message);
        public void Dispose()
        {
            StopPlayback();
            System.Console.WriteLine("[MP3Stream] Disposed");
        }
        private void StreamMp3(object state)
        {
            fullyDownloaded = false;
            string url = (string)state;
            webRequest = (HttpWebRequest)WebRequest.Create(url);
            int metaInt = 0; // blocksize of mp3 data

            webRequest.Headers.Clear();
            webRequest.Headers.Add("GET", "/ HTTP/1.0");
            webRequest.Headers.Add("Icy-MetaData", "1");
            webRequest.UserAgent = "WinampMPEG/5.09";
            HttpWebResponse resp;
            try
            {
                resp = (HttpWebResponse)webRequest.GetResponse();
            }
            catch (WebException e)
            {
                if (e.Status != WebExceptionStatus.RequestCanceled)
                {
                    System.Console.WriteLine(e.Message);
                }
                return;
            }
            byte[] buffer = new byte[16384 * 4]; // needs to be big enough to hold a decompressed frame
            try
            {
                // read blocksize to find metadata block
                metaInt = Convert.ToInt32(resp.GetResponseHeader("icy-metaint"));

            }
            catch
            {
            }
            IMp3FrameDecompressor decompressor = null;
            try
            {
                using (Stream responseStream = resp.GetResponseStream())
                {
                    ReadFullyStream readFullyStream = new ReadFullyStream(responseStream);
                    subbedToEvent = false;
                    readFullyStream.MetaInt = metaInt;
                    do
                    {
                        if (IsBufferNearlyFull)
                        {
                            System.Console.WriteLine("Buffer getting full, taking a break");
                            Thread.Sleep(1000);
                        }
                        else
                        {
                            Mp3Frame frame;
                            try
                            {
                                frame = Mp3Frame.LoadFromStream(readFullyStream);
                                if (metaInt > 0 && !subbedToEvent)
                                {
                                    subbedToEvent = true;
                                    readFullyStream.StreamTitleChanged += ReadFullyStream_StreamTitleChanged;
                                }
                                else if (metaInt <= 0)
                                    song_info = "none";
                            }
                            catch (EndOfStreamException)
                            {
                                fullyDownloaded = true;
                                // reached the end of the MP3 file / stream
                                break;
                            }
                            catch (WebException)
                            {
                                // probably we have aborted download from the GUI thread
                                break;
                            }
                            if (frame == null) break;
                            if (decompressor == null)
                            {
                                // don't think these details matter too much - just help ACM select the right codec
                                // however, the buffered provider doesn't know what sample rate it is working at
                                // until we have a frame
                                decompressor = CreateFrameDecompressor(frame);
                                bufferedWaveProvider = new BufferedWaveProvider(decompressor.OutputFormat)
                                {
                                    BufferDuration = TimeSpan.FromSeconds(30) // allow us to get well ahead of ourselves
                                };
                                //this.bufferedWaveProvider.BufferedDuration = 250;

                                decomp = true; //tell main Unity Thread to create AudioClip
                            }
                            int decompressed = decompressor.DecompressFrame(frame, buffer, 0);
                            bufferedWaveProvider.AddSamples(buffer, 0, decompressed);
                        }

                    } while (playbackState != StreamingPlaybackState.Stopped);
                    System.Console.WriteLine("Exiting Thread");
                    // was doing this in a finally block, but for some reason
                    // we are hanging on response stream .Dispose so never get there
                    decompressor.Dispose();
                    readFullyStream.Close();
                    readFullyStream.StreamTitleChanged -= ReadFullyStream_StreamTitleChanged; //Unsubscribe title event.
                    readFullyStream.Dispose();
                }
            }
            finally
            {
                if (decompressor != null)
                {
                    decompressor.Dispose();
                }
            }
        }

        private void ReadFullyStream_StreamTitleChanged(object sender, StreamTitleChangedEventArgs e)
        {
            song_info = e.Title;
            System.Console.WriteLine(e.Title);
        }

        public void ReadData(float[] data)
        {
            if (bufferedWaveProvider != null)
            {
                bufferedWaveProvider.ToSampleProvider().Read(data, 0, data.Length);
            }
        }
        private static IMp3FrameDecompressor CreateFrameDecompressor(Mp3Frame frame)
        {
            WaveFormat waveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(waveFormat);
        }

        public bool IsBufferNearlyFull => bufferedWaveProvider != null &&
                       bufferedWaveProvider.BufferLength - bufferedWaveProvider.BufferedBytes
                       < bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;

        //Everything starts here.
        public void PlayStream(string streamUrl)
        {
            if (playbackState == StreamingPlaybackState.Stopped)
            {
                playbackState = StreamingPlaybackState.Buffering;
                bufferedWaveProvider = null;
                ThreadPool.QueueUserWorkItem(StreamMp3, streamUrl);
            }
            else if (playbackState == StreamingPlaybackState.Paused)
            {
                playbackState = StreamingPlaybackState.Buffering;
            }
        }

        private void StopPlayback()
        {
            if (playbackState != StreamingPlaybackState.Stopped)
            {
                if (!fullyDownloaded)
                {
                    webRequest.Abort();
                }

                decomp = false;
                if (audioSource != null)
                {
                    audioSource.Stop();
                    audioSource = null;
                }
                song_info = null;
                subbedToEvent = false;
                playbackState = StreamingPlaybackState.Stopped;
                ShowBufferState(0, 0);
            }
        }

        private void ShowBufferState(double buffered, double total)
        {
            buffer_info = $"{buffered:0.0}s/{total:0.0}s";
        }

        public void UpdateLoop()
        {
            if (playbackState != StreamingPlaybackState.Stopped)
            {
                if (bufferedWaveProvider != null)
                {
                    double bufferedSeconds = bufferedWaveProvider.BufferedDuration.TotalSeconds;
                    ShowBufferState(bufferedSeconds, bufferedWaveProvider.BufferDuration.TotalSeconds);
                    // make it stutter less if we buffer up a decent amount before playing
                    if (bufferedSeconds < 0.5 && playbackState == StreamingPlaybackState.Playing && !fullyDownloaded)
                    {
                        Pause();
                    }
                    else if (bufferedSeconds > 3 && playbackState == StreamingPlaybackState.Buffering)
                    {
                        Play();
                    }
                    else if (fullyDownloaded && bufferedSeconds < 0.5)
                    {
                        System.Console.WriteLine("Reached end of stream");
                        StopPlayback();
                    }
                }

            }
        }

        private void Play()
        {
            audioSource.Play();
            playbackState = StreamingPlaybackState.Playing;
        }

        private void Pause()
        {
            playbackState = StreamingPlaybackState.Buffering;
            audioSource.Pause();
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            System.Console.WriteLine("Playback Stopped");
            if (e.Exception != null)
            {
                System.Console.WriteLine(string.Format("Playback Error {0}", e.Exception.Message));
            }
        }
    }

}
#endif