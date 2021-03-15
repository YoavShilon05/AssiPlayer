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

    public class QueueManager
    {
        private Queue<TrackRecord> radioQueue = new();
        private Queue<TrackRecord> trackQueue = new();
        private RadioPlayer radio;

        public List<TrackRecord> history = new();

        public const int RadioQueueAmount = 5;

        private Thread downloader;
        private bool running = true;

        private Queue<(FullTrack, QueueType)> downloading = new();

        public QueueManager(IEnumerable<DiscordMember> users)
        {
            radio = new RadioPlayer(users.Select(u => u.Id).ToList());
            downloader = new(async () =>
            {
                while (running || downloading.Count > 0)
                {
                    while (downloading.Count == 0) { }

                    var (track, type) = downloading.Dequeue();
                    AddToQueue(await Download(track), type);
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

        public async Task QueueNextRadio()
        {
            FullTrack r = await radio.RandomFavorite(history.Select(t => t.Track));
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

        public async Task<TrackRecord> NextTrack(bool download_next=true)
        {
            while (trackQueue.Count == 0 && radioQueue.Count == 0) {}
            if (trackQueue.Count == 0)
            {
                if (history.Count > 0) File.Delete(history[^1].Path);
                if (download_next) await QueueNextRadio().ConfigureAwait(false);
                var track = radioQueue.Dequeue();
                history.Add(track);
                return track;
            }
            return trackQueue.Dequeue();
        }

        public async Task Terminate()
        {
            running = false;
            downloader.Join();

            for (int i = 0; i < RadioQueueAmount; i++)
                File.Delete((await NextTrack(false)).Path);
            
        }
    }
}
