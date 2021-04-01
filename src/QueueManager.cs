using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    public enum QueueType
    {
        Radio,
        Track
    }

    public enum RadioGetters
    {
        Radio,
        Playlist_Shuffle,
        Playlist_Walkthrough,
        Artist,
        Loop
    }
    
    public class QueueManager
    {
        public Queue<TrackRecord> radioQueue { get; private set; } = new();
        public Queue<TrackRecord> trackQueue { get; private set; } = new();
        public Queue<(FullTrack, QueueType)> downloading { get; private set; } = new();
        public RadioPlayer radio;
        private RadioAlgorithms algorithm = RadioAlgorithms.radio;
    
        public List<TrackRecord> history = new();

        public const int RadioQueueAmount = 5;

        private Thread downloader;
        private bool running = true;

        public FullTrack LoopTrack;
        
        private async Task<FullTrack> RadioGetter()
        {
            if (radio.set)
                return radio.Next(algorithm);
            return await radio.RandomFavorite(history.Select(r => r.Track));
        }

        public void RemoveItemFromDownloadingQueue(FullTrack track)
        {
            downloading = new Queue<(FullTrack, QueueType)>(downloading.Where(t => t.Item1.Id != track.Id));
        }
        
        private Task<FullTrack> PlaylistShuffleGetter()
        {
            throw new NotImplementedException();
        }
        
        private Task<FullTrack> PlaylistWalkthroughGetter()
        {
            throw new NotImplementedException();
        }
        
        private Task<FullTrack> ArtistGetter()
        {
            throw new NotImplementedException();
        }
        
        
        private Task<FullTrack> LoopGetter() =>
            Task.FromResult(LoopTrack);
        

        private Dictionary<RadioGetters, Func<Task<FullTrack>>> getters;
        
        public QueueManager(IEnumerable<DiscordMember> users)
        {
            radio = new RadioPlayer(users.Select(u => u.Id).ToList());
            radio.Set().ConfigureAwait(false);
            
            getters = new()
            {
                {RadioGetters.Radio, RadioGetter},
                {RadioGetters.Artist, ArtistGetter},
                {RadioGetters.Playlist_Shuffle, PlaylistShuffleGetter},
                {RadioGetters.Playlist_Walkthrough, PlaylistWalkthroughGetter},
                {RadioGetters.Loop, LoopGetter}
            };
            
            downloader = new(async () =>
            {
                while (running)
                {
                    while (downloading.Count == 0) { }

                    var (track, type) = downloading.Peek();
                    var downloaded = await Download(track);
                    
                    // check if track not removed
                    if (downloading.Count > 0 && downloading.Peek().Item1.Id == track.Id)
                    {
                        AddToQueue(downloaded, type);
                        downloading.Dequeue();
                    }
                }
            });
            downloader.Start();
        }

        private Queue<TrackRecord> GetQueue(QueueType queue) =>
            queue switch
            {
                QueueType.Radio => radioQueue,
                QueueType.Track => trackQueue,
                _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
            };

        private void AddToQueue(TrackRecord track, QueueType type)
        {
            Queue<TrackRecord> queue = GetQueue(type);
            queue.Enqueue(track);
        }
        
        private static async Task<TrackRecord> Download(FullTrack track)
        {
            TrackRecord record = await Downloader.SearchAudio(track);
            Console.WriteLine($"Downloaded track {track.Name}");
            return record;
        }

        public async Task QueueNextRadio(RadioGetters getter)
        {
            FullTrack r = await getters[getter]();
            downloading.Enqueue((r, QueueType.Radio));
        }

        public async Task QueueAlbum(FullAlbum album)
        {
            foreach (var song in album.Tracks.Items!)
                await QueueTrack(await song.GetFull());
        }

        public Task QueueTrack(FullTrack track)
        {
            downloading.Enqueue((track, QueueType.Track));
            return Task.CompletedTask;
        }

        public async Task<TrackRecord> NextTrack(bool download_next=true, RadioGetters getter=RadioGetters.Radio)
        {
            while (trackQueue.Count == 0 && radioQueue.Count == 0) {}
            
            // checks if no other player is gonna play the song you are about to delete
            if (history.Count > 0 && !OtherPlayerUsingTrack(history[^1].Track))
                File.Delete(history[^1].Path);
            
            if (trackQueue.Count == 0)
            {
                if (download_next) await QueueNextRadio(getter).ConfigureAwait(false);
                var track = radioQueue.Dequeue();
                history.Add(track);
                return track;
            }
            return trackQueue.Dequeue();
        }

        public void Terminate()
        {
            running = false;
            downloader.Join();
            Clear();
        }

        private static bool OtherPlayerUsingTrack(FullTrack track) =>
            Program.players.Values.Any(p => p.GetQueue().Select(t => t.Id).Contains(track.Id));
        
        
        public void Clear()
        {
            downloading.Clear();

            List<TrackRecord> downloadedTracks = radioQueue.ToList();
            downloadedTracks.AddRange(trackQueue);

            foreach (var t in downloadedTracks)
                if (!OtherPlayerUsingTrack(t.Track)) File.Delete(history[^1].Path);
            
            radioQueue.Clear();
            trackQueue.Clear();
        }
    }
}
