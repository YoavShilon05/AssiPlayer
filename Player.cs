using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Threading;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using SpotifyAPI.Web;
using DSharpPlus.VoiceNext.Entities;
using DSharpPlus.Lavalink;

namespace AssiSharpPlayer
{
    public enum PlayerStates
    {
        Idle,
        Radio,
        Track,
        Album,
        Playlist
    }
    
    public class Player
    {
        public Queue<TrackRecord> radioQueue = new();
        public Queue<TrackRecord> songQueue = new();
        public List<TrackRecord> history = new();
        private VoiceNextConnection voiceConnection;
        private const int queue_length = 3;

        public bool terminate = false;
        public bool skip = false;

        public bool running = false;
        private bool playingRadio = false;
        
        public Player(DiscordChannel vc)
        {
            Program.players.Add(vc.Guild.Id, this);
            voiceConnection = vc.ConnectAsync().GetAwaiter().GetResult();
        }
        
        private IEnumerable<DiscordMember> GetMembersListening()
        {
            var users = voiceConnection.TargetChannel.Users;
            foreach (var u in users)
                if (!u.VoiceState.IsSelfDeafened)
                    yield return u;
        }

        
        private async Task<FullTrack> Radio() =>
            await RadioSongGenerator.RandomFavorite(GetMembersListening(), history, radioQueue);

        private async Task Download(FullTrack track, Queue<TrackRecord> queue, bool ordered=false)
        {
            TrackRecord hollowRecord = null;
            if (ordered)
            {
                hollowRecord = new TrackRecord(null, null, null, 0, null, false);
                queue.Enqueue(hollowRecord);
            }

            TrackRecord record = await Downloader.SearchAudio(track);
            if (ordered)
                hollowRecord.Set(record);
            else queue.Enqueue(record);
            
            Console.WriteLine($"Downloaded track {track.Name}");
        }

        public async Task AddTrack(FullTrack track)
        {
            await Download(track, songQueue, true);
        }
        
        public async Task PlayAlbum(FullAlbum album)
        {
            List<SimpleTrack> tracks = album.Tracks.Items;
            Thread[] downloaders = new Thread[tracks!.Count];
            await AddTrack(await tracks[0].GetFull());
            
            if (!running) Main();
            
            for (int i = 1; i <= tracks.Count; i++)
            {
                var locali = i;
                downloaders[i] = new Thread(async () => await AddTrack(await tracks[locali].GetFull()));
                downloaders[i].Start();
                await Task.Delay(500);
            }
        }

        public void StartPlayingRadio()
        {
            playingRadio = true;
            
            List<Thread> downloaders = new();
            for (int i = 0; i < queue_length- 1; i++)
            {
                Thread th = new(async () => await Download(await Radio(), radioQueue));
                downloaders.Add(th);
                th.Start();
            }
        }
        
        public async Task Main()
        {
            running = true;

            while (true)
            {
                Thread? downloader = null;
                if (songQueue.Count == 0 && playingRadio)
                {
                    FullTrack nextSong = await Radio();
                    downloader = new(async () => await Download(nextSong, radioQueue));
                    if (radioQueue.Count == 0) await Download(nextSong, radioQueue);
                    else downloader.Start();
                }


                TrackRecord song;
                if (songQueue.Count > 0)
                {
                    while (!songQueue.Peek().Downloaded) await Task.Delay(1);
                    song = songQueue.Dequeue();
                }
                else if (playingRadio)
                {
                    while (radioQueue.Count == 0) await Task.Delay(1);
                    song = radioQueue.Dequeue();
                }
                else break;
                
                await Play(song.Path);
                //delete last song
                if (history.Count > 0) File.Delete(history[^1].Path);
                
                history.Add(song);
                skip = false;
                
                
                if (downloader != null && downloader.IsAlive) downloader.Join();

                if (terminate)
                {
                    voiceConnection.Disconnect();
                    break;
                }
            }
            
            running = false;
        }
        
        private async Task Play(string file)
        {
            if (voiceConnection == null)
                throw new InvalidOperationException("Not connected in this guild.");

            if (!File.Exists(file))
                throw new FileNotFoundException("File was not found.");

            await voiceConnection.SendSpeakingAsync(true); // send a speaking indicator

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $@"-i ""{file}"" -ac 2 -f s16le -ar 48000 pipe:1",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var ffmpeg = Process.Start(psi);
            var ffout = ffmpeg.StandardOutput.BaseStream;

            var buff = new byte[3840];
            var br = 0;
            while ((br = ffout.Read(buff, 0, buff.Length)) > 0)
            {
                
                if (skip || terminate) break;
                
                if (br < buff.Length) // not a full sample, mute the rest
                    for (var i = br; i < buff.Length; i++)
                        buff[i] = 0;
                
                await voiceConnection.GetTransmitSink().WriteAsync(buff);
            }
            
            await voiceConnection.SendSpeakingAsync(false); // we're not speaking anymore
            await voiceConnection.WaitForPlaybackFinishAsync();
        }
        
        /*
        self.songqueue : Queue[MusicFunctions.SongInfo] = Queue()
        self.history = set()
        self.voice_client = voice_client
            self.voice_channel = voice_channel
            self.skip = False
            self.terminate = False
            self.playing_song : MusicFunctions.SongInfo = None
            self.text_channel = text_channel

            self.queueLength = 3 #num of songs to download mid-queue
        */
    }
}