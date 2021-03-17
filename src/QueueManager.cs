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
        Artist
    }
    
    public class QueueManager
    {
        public Queue<TrackRecord> radioQueue { get; private set; } = new();
        public Queue<TrackRecord> trackQueue { get; private set; } = new();
        public Queue<(FullTrack, QueueType)> downloading { get; private set; } = new();
        private RadioPlayer radio;

        public List<TrackRecord> history = new();

        public const int RadioQueueAmount = 5;

        private Thread downloader;
        private bool running = true;


        private async Task<FullTrack> RadioGetter() =>
            await radio.RandomFavorite(history.Select(t => t.Track));

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

        private Dictionary<RadioGetters, Func<Task<FullTrack>>> getters;
        
        public QueueManager(IEnumerable<DiscordMember> users)
        {
            radio = new RadioPlayer(users.Select(u => u.Id).ToList());
            
            getters = new()
            {
                {RadioGetters.Radio, RadioGetter},
                {RadioGetters.Artist, ArtistGetter},
                {RadioGetters.Playlist_Shuffle, PlaylistShuffleGetter},
                {RadioGetters.Playlist_Walkthrough, PlaylistWalkthroughGetter}
                
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
            if (trackQueue.Count == 0)
            {
                if (history.Count > 0) File.Delete(history[^1].Path);
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

        public void Clear()
        {
            downloading.Clear();
            radioQueue.Clear();
            trackQueue.Clear();
        }
    }
}
