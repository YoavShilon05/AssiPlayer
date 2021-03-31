using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    
    public class Ref<T>
    {
        public T Value
        {
            get => getter();
            set => setter(value);
        }
  

        Func<T> getter;
        Action<T> setter;
  
        public Ref(Func<T> getter, Action<T> setter)
        {
            this.getter = getter;
            this.setter = setter;
        }
    }
    
    public class Song
    {
        
    }

    public static class Requestor
    {
        private static async Task<IEnumerable<T>> GenerateRequest<T, T2>(
            Func<T2, Task<Paging<T>>> getter,
            int limit, Func<int, T2> generator, Func<T2> remainder) where T2 : RequestParams
        {
            var result = new T[limit];

            for (int i = 0; i < MathF.Floor(limit / 50f); i++)
            {
                Paging<T> request = await getter(generator(i));
                request.Items!.CopyTo(result, i * 50);
                if (request.Items.Count < 50) return result.Clean();
            }

            if (limit % 50 == 0) return result.Clean();
            
            var newrequest = await getter(remainder());
            newrequest.Items!.CopyTo(result, limit - limit % 50);

            return result.Clean();
        }

        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<PersonalizationTopRequest, Task<Paging<T>>> getter,
            int limit, PersonalizationTopRequest.TimeRange timeRange)
        {
            return await GenerateRequest(getter, limit,
                i => new PersonalizationTopRequest
                    {Limit = 50, Offset = i * 50, TimeRangeParam = timeRange},
                () => new PersonalizationTopRequest
                    {Limit = limit % 50, Offset = limit - limit % 50, TimeRangeParam = timeRange});
        }

        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<LibraryTracksRequest, Task<Paging<T>>> getter,
            int limit)
        {
            return await GenerateRequest(getter, limit,
                i => new LibraryTracksRequest {Limit = 50, Offset = i * 50},
                 () => new LibraryTracksRequest {Limit = limit % 50, Offset = limit - limit % 50}
            );
        }
        
        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<LibraryAlbumsRequest, Task<Paging<T>>> getter,
            int limit)
        {
            return await GenerateRequest(getter, limit,
                i => new LibraryAlbumsRequest {Limit = 50, Offset = i * 50},
                () => new LibraryAlbumsRequest {Limit = limit % 50, Offset = limit - limit % 50}
            );
        }
    }
    
    public class Listener
    {
        public SpotifyClient client;
        public const int TopSongsLimit = 70;
        private const int TopArtistsLimit = 30;
        private const int LikedSongsLimit = 1000;
        private const int LikeArtistBySongsInTopThresh = 3;


        public Listener(ulong id)
        {
            client = SpotifyManager.GetClient(id).GetAwaiter().GetResult();
        }

        public async Task Set()
        {

            await TopTracks();
            await TopArtists();
            await LikedTracks();
            GenreWeights();

        }
        
        private async Task LikedTracks()
        {
            IEnumerable<SavedTrack> savedTracks = await Requestor.MakeRequest(client.Library.GetTracks, LikedSongsLimit);
            Dictionary<FullTrack, long> trackweights = savedTracks.ToDictionary(t => t.Track, t => t.AddedAt.Ticks);
            likedTracks = trackweights.Sort();
        }

        private async Task TopTracks() =>
            topTracks = (await Requestor.MakeRequest(client.Personalization.GetTopTracks, TopSongsLimit,
                                                PersonalizationTopRequest.TimeRange.ShortTerm)).ToList();


        private async Task TopArtists()
        {
            favoriteArtists = (await Requestor.MakeRequest(client.Personalization.GetTopArtists, TopArtistsLimit,
                                                PersonalizationTopRequest.TimeRange.ShortTerm)).ToList();

            Dictionary<string, float> artistAmounts = new();
            
            foreach (var track in topTracks)
            {
                //if (favoriteArtists.Select(a => a.Id).Contains(track.Artists[0].Id)) continue;
                
                if (artistAmounts.ContainsKey(track.Artists[0].Id)) 
                    artistAmounts[track.Artists[0].Id] += 1;
                else artistAmounts.Add(track.Artists[0].Id, 1);
            }


            FullArtist result = new();
            ArtistBatcher.Artist(favoriteArtists[0].Id,
                new Ref<FullArtist>(() => result, artist => result = artist));
            
            Console.WriteLine(result);
            await ArtistBatcher.Join();
            Console.WriteLine(result);
            
            IEnumerable<string> artistWeights = artistAmounts
                .Where(pair => pair.Value > LikeArtistBySongsInTopThresh).ToDictionary(
                    x => x.Key, x => x.Value).Sort().ToList();
            
            Ref<IEnumerable<FullArtist>> r = new(() => favoriteArtists, a => favoriteArtists.AddRange(a));
            
            if (artistWeights.Any())
                await ArtistBatcher.Batch(artistWeights, r);

            Console.WriteLine("aa");
            
        }


        public void GenreWeights()
        {
            void SetGenres(IEnumerable<string> genres)
            {
                foreach (var g in genres)
                {
                    if (genreWeights.ContainsKey(g)) genreWeights[g] += 1;
                    else genreWeights.Add(g, 1);
                }
            }

            foreach (var artist in favoriteArtists)
                SetGenres(artist.Genres);

            float sum = genreWeights.Values.Sum();
            foreach (var g in genreWeights.Keys)
                genreWeights[g] = genreWeights[g] / sum;
            
        }

        public List<FullArtist> favoriteArtists = new();
        public List<FullTrack> topTracks;
        public List<FullTrack> likedTracks = new();
        public Dictionary<string, float> genreWeights = new();
    }

    public static class ArtistBatcher
    {
        private static List<(string, Ref<FullArtist>, Ref<Task>)> requests = new();
        private static bool join;
        private static bool running;
        private static Thread listener; 
        public const int batchsize = 20;
        
        static ArtistBatcher()
        {
            running = true;
            listener = new(async () => await Listen());
            listener.Start();
        }

        private static async Task Listen()
        {
            while (running)
            {
                if (requests.Count >= batchsize || join)
                {
                    List<(string, Ref<FullArtist>, Ref<Task>)> batch;
                    if (requests.Count > batchsize) batch = requests.GetRange(0, batchsize);
                    else batch = requests;
                    
                    List<string> ids = batch.Select(i => i.Item1).ToList();
                    List<FullArtist> result = (await SpotifyManager.GlobalClient.Artists.GetSeveral(new(ids))).Artists;

                    for (int i = 0; i < result.Count; i++)
                    {
                        batch[i].Item2.Value = result[i];
                        batch[i].Item3.Value = Task.CompletedTask;
                    }

                    requests.RemoveRange(0, batch.Count);
                    if (requests.Count == 0) join = false;
                }
            }
        }
        
        public static Ref<Task> Artist(string artist,  Ref<FullArtist> reference)
        {
            Task task = new(() => { });
            Ref<Task> Treference = new(() => task, newtask => task = newtask);
            requests.Add((artist, reference, Treference));

            return Treference;
        }

        public static async Task Batch(IEnumerable<string> batch, Ref<IEnumerable<FullArtist>> reference)
        {
            
            List<FullArtist> result = new();
            foreach (var artist in batch)
            {
                await Artist(artist, new Ref<FullArtist>(() => null, val => result.Add(val))).Value;
            }

            await Join();
            reference.Value = result;
        }
        
        public static Task Join()
        {
            join = true;
            return requests[^1].Item3.Value;
        }

        public static void Terminate()
        {
            Join();
            running = false;
        }
    }


    public enum RadioAlgorithms
    {
        radio,
        explore,
        uncharted
    }
    
    public class RadioPlayer
    {
        private List<ulong> users;
        private List<Listener> listeners = new();
        private HashSet<string> history = new();
        
        public RadioPlayer(List<ulong> users)
        {
            this.users = users;
        }
        
        public async Task Start()
        {
            /*
            Thread[] threads = new Thread[users.Count];
            for (var i = 0; i < users.Count; i++)
            {
                ulong id = users[i];
                Thread th = new Thread(async () =>
                {
                    Listener l = new(id);
                    await l.Set();
                    listeners.Add(l);
                });
                th.Start();

                threads[i] = th;
            }

            return new Task(() =>
            {
                foreach (Thread th in threads)
                    th.Join();
            });
            foreach (var i in users)
            {
                Listener l = new(i);
                await l.Set();
                listeners.Add(l);
            }*/

        }

        
        //public FullTrack Next(RadioAlgorithms algorithm)
        //{
            
        //}
        
        
        private bool TrackInBlacklist(FullTrack track)
        {
            return users.Where(u => Program.blacklists.ContainsKey(u)).Any(u => Program.blacklists[u].Contains(track.Id));
        }
        
        public async Task<FullTrack> RandomFavorite(IEnumerable<FullTrack> history)
        {
            Random r = new();
            var ui = r.Next(users.Count);
            var u = users[ui];

            var client = await SpotifyManager.GetClient(u);

            List<FullTrack> tracks = (await client.Personalization.GetTopTracks
                (new(){Limit = 50, TimeRangeParam = PersonalizationTopRequest.TimeRange.ShortTerm})).Items;

            int tracki = r.Next(tracks!.Count);
            while (history.Contains(tracks[tracki]) || TrackInBlacklist(tracks[tracki])) tracki = r.Next(tracks!.Count); 
            
            return tracks[tracki];
        }
        
        public static void Main(string[] args)
        {
            RadioPlayer r = new(new(){329960504376426496});
            r.Start().GetAwaiter().GetResult();
            Console.WriteLine(r.listeners);
        }  
    }
    
    
}
