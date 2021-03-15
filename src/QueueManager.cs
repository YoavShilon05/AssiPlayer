using System;
using System.Collections.Generic;
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

        public const int RadioQueueAmount = 5;

        private Thread downloader;

        private Queue<(FullTrack, QueueType)> downloading = new();

        public QueueManager(IEnumerable<DiscordMember> users)
        {
            radio = new RadioPlayer(users.Select(u => u.Id));
            downloader = new(async () =>
            {
                while (true)
                {
                    while (downloading.Count == 0) { }

                    var (track, type) = downloading.Dequeue();
                    AddToQueue(await Download(track), type);
                }
                // ReSharper disable once FunctionNeverReturns
            });
        }

        private Queue<TrackRecord> GetQueue(QueueType queue) =>
            queue switch
            {
                QueueType.Radio => radioQueue,
                QueueType.Track => trackQueue,
                _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
            };

        public void AddToQueue(TrackRecord track, QueueType type)
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

        public Task QueueNextRadio()
        {
            FullTrack r = radio.BigDic();
            downloading.Enqueue((r, QueueType.Radio));
            return Task.CompletedTask;
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

        public async Task<TrackRecord> NextTrack()
        {
            if (trackQueue.Count == 0)
            {
                await QueueNextRadio();
                return radioQueue.Dequeue();
            }

            return trackQueue.Dequeue();
        }
    }
}
