using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
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
        public FullTrack track;
        public FullArtist artist;
        public HashSet<ulong> owners = new();
        public Dictionary<ulong, double> userWeights = new();
        
        private const float LikedArtistWeight = 55;
        private const float TopSongWeight = 100;
        private const float LikedSongWeight = 70;
        private const float GenreWeight = 85;
        
        public Song(FullTrack track, ulong owner)
        {
            owners.Add(owner);
            this.track = track;
            ArtistBatcher.Artist(track.Artists[0].Id, new Ref<FullArtist>(() => artist,val => artist = val));
        }

        public void CalculateWeightForListener(Listener l)
        {

            IEnumerable<string> genres = l.genreWeights.Keys.Intersect(artist.Genres);
            float genrevalue = genres.Sum(g => l.genreWeights[g]) / 25;

            float likedvalue = 0;
            List<string> likedTrackIds = l.likedTracks.Select(t => t.Id).ToList();
            if (likedTrackIds.Contains(track.Id))
            {
                //likedvalue = (float)likedTrackIds.IndexOf(track.Id) / (likedTrackIds.Count) + 0.5f;
                likedvalue = 1;
            }
            
            float topvalue = 0;
            List<string> topTrackIds = l.topTracks.Select(t => t.Id).ToList();
            if (topTrackIds.Contains(track.Id))
            {
                //topvalue = (float)topTrackIds.IndexOf(track.Id) / (Listener.TopSongsLimit) + 0.5f;
                topvalue = 1;
            }
            
            float artistvalue = 0;
            List<string> topArtistIds = l.favoriteArtists.Select(a => a.Id).ToList();
            if (topArtistIds.Contains(track.Artists[0].Id))
            {
                //artistvalue = (float)topArtistIds.IndexOf(artist.Id) / (topArtistIds.Count) + 0.5f;
                artistvalue = 1;
            }

            double value = genrevalue * GenreWeight + likedvalue * LikedSongWeight + topvalue * TopSongWeight + artistvalue *
                          LikedArtistWeight;

            userWeights[l.id] = value;
        }
    
        public void RemoveUser(ulong id)
        {
            if (userWeights.ContainsKey(id)) userWeights.Remove(id);
        }
        
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
        public ulong id;
        public const int TopSongsLimit = 50;
        private const int TopArtistsLimit = 30;
        private const int LikedSongsLimit = 1000;
        private const int LikeArtistBySongsInTopThresh = 3;


        public Listener(ulong id)
        {
            this.id = id;
            client = SpotifyManager.GetClient(id).GetAwaiter().GetResult();
        }

        public async Task Set()
        {

            await TopTracks();
            await TopArtists();
            await LikedTracks();
        }
        
        private async Task LikedTracks()
        {
            IEnumerable<SavedTrack> savedTracks = await Requestor.MakeRequest(client.Library.GetTracks, LikedSongsLimit);
            Dictionary<FullTrack, long> trackweights = savedTracks.ToDictionary(t => t.Track, t => t.AddedAt.Ticks);
            likedTracks = trackweights.Sort();
        }

        private async Task TopTracks()
        {
            topTracks = (await Requestor.MakeRequest(client.Personalization.GetTopTracks, TopSongsLimit,
                PersonalizationTopRequest.TimeRange.ShortTerm)).ToList();
            topTracks.Reverse();
        }


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

            
            IEnumerable<string> artists = artistAmounts
                .Where(pair => pair.Value > LikeArtistBySongsInTopThresh).ToDictionary(
                    x => x.Key, x => x.Value).Sort().ToList();
            
            Ref<IEnumerable<FullArtist>> r = new(() => favoriteArtists, a => favoriteArtists.AddRange(a));
            
            if (artists.Any())
                ArtistBatcher.Batch(artists, r);

            favoriteArtists.Reverse();
        }


        public void GenreWeights(IEnumerable<Song> mysongs)
        {
            void SetGenres(IEnumerable<string> genres)
            {
                foreach (var g in genres)
                {
                    if (genreWeights.ContainsKey(g)) genreWeights[g] += 1;
                    else genreWeights.Add(g, 1);
                }
            }
            

            foreach (var s in mysongs)
            {
                SetGenres(s.artist.Genres);
            }


            float sum = genreWeights.Values.Sum();
            foreach (var g in genreWeights.Keys)
                genreWeights[g] = 100 * genreWeights[g] / sum;
            
        }

        public List<FullArtist> favoriteArtists = new();
        public List<FullTrack> topTracks;
        public List<FullTrack> likedTracks = new();
        public Dictionary<string, float> genreWeights = new();

        public IEnumerable<Song> GetSongs()
        {
            HashSet<string> songsCreated = new();
            List<Song> result = new();

            foreach (var t in likedTracks)
            {
                // if for some reason you liked the same song two times, please continue.
                if (songsCreated.Contains(t.Id)) continue;
                
                Song s = new(t, id);
                songsCreated.Add(t.Id);
                result.Add(s);
            }

            foreach (var t in topTracks)
            {
                // same story for the top tracks.
                if (songsCreated.Contains(t.Id)) continue;
                Song s = new(t, id);
                songsCreated.Add(t.Id);
                result.Add(s);
            }

            return result;
        }
    }

    public static class ArtistBatcher
    {
        private static List<(string, Ref<FullArtist>)> requests = new();
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
                    Console.WriteLine("processing batch...");
                    List<(string, Ref<FullArtist>)> batch;
                    if (requests.Count > batchsize) batch = requests.GetRange(0, batchsize);
                    else batch = requests;
                    
                    List<string> ids = batch.Select(i => i.Item1).ToList();
                    List<FullArtist> result = (await SpotifyManager.GlobalClient.Artists.GetSeveral(new(ids))).Artists;

                    for (int i = 0; i < result.Count; i++)
                    {
                        batch[i].Item2.Value = result[i];
                    }

                    requests.RemoveRange(0, batch.Count);
                    if (requests.Count == 0) join = false;
                }
            }
        }
        
        public static void Artist(string artist,  Ref<FullArtist> reference)
        {
            requests.Add((artist, reference));
        }

        public static void Batch(IEnumerable<string> batch, Ref<IEnumerable<FullArtist>> reference)
        {
            
            List<FullArtist> result = new();
            foreach (var artist in batch)
            {
                Artist(artist, new Ref<FullArtist>(() => null, val => result.Add(val)));
            }

            Join();
            reference.Value = result;
        }
        
        public static void Join()
        {
            if (requests.Count == 0) return;
            join = true;
            while (join)
                Task.Delay(1000);
            
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
        public bool set = false;
        private List<ulong> users;
        private List<Listener> listeners = new();
        private Dictionary<ulong, float> userWeights = new();
        private HashSet<string> history = new();
        //private Dictionary<ulong, Dictionary<Song, Dictionary<ulong, double>>> songWeights = new();
        private Dictionary<ulong, List<Song>> songs = new();
        
        public RadioPlayer(List<ulong> users)
        {
            this.users = users;
            foreach (var u in users)
            {
                userWeights.Add(u, 1);
            }
        }

        public async Task Set()
        {
            await SetListeners();
            foreach (var l in listeners)
            {
                AddSongsOfListener(l);
            }
            //CleanSongs();
            ArtistBatcher.Join();

            // you can only set the genres after the artists have been set.
            foreach (var l in listeners)
            {
                l.GenreWeights(songs[l.id]);
            }
            
            foreach (var u in users)
            {
                SetSongWeightsForUser(u);
            }

            set = true;
        }
        
        private async Task SetListeners()
        {
            
            //Thread[] threads = new Thread[users.Count];
            //for (var i = 0; i < users.Count; i++)
            //{
            //    ulong id = users[i];
            //    Thread th = new Thread(async () =>
            //    {
            //        Listener l = new(id);
            //        await l.Set();
            //        listeners.Add(l);
            //    });
            //    th.Start();

            //    threads[i] = th;
            //}

            //return new Task(() =>
            //{
            //    foreach (Thread th in threads)
            //        th.Join();
            //});
            foreach (var i in users)
            {
                await AddListener(i, false);
            }
        }
        
        private void AddSongsOfListener(Listener l)
        {
            songs.Add(l.id, l.GetSongs().ToList());
        }

        public async Task AddUser(ulong id)
        {
            if (users.Contains(id)) return;
            users.Add(id);
            userWeights.Add(id, 1);
            await AddListener(id);
        }

        private async Task AddListener(ulong id , bool calculateSongs=true)
        {
            Listener l = new(id);
            await l.Set();
            listeners.Add(l);
             if (calculateSongs)
             {
                 AddSongsOfListener(l);
                 ArtistBatcher.Join();
                 SetSongWeightsForUser(id);
             }
        }

        public void RemoveUser(ulong id)
        {
            if (!users.Contains(id)) return;
            users.Remove(id);
            userWeights.Remove(id);
            RemoveListener(id);
        }

        private void RemoveListener(ulong id)
        {
            foreach (var l in listeners)
            {
                if (l.id == id)
                {
                    listeners.Remove(l);
                    if (songs.ContainsKey(l.id)) songs.Remove(l.id);
                    return;
                }
            }
        }
        
        private void CleanSongs()
        {
            Dictionary<string, Song> songSet = new();
            foreach (var user in songs.Keys)
            {
                foreach (var s in songs[user])
                {
                    if (songSet.ContainsKey(s.track.Id))
                    {
                        songs[user].Remove(s);
                        songSet[s.track.Id].owners.Add(user);
                    }
                    else songSet.Add(s.track.Id, s);
                }
            }
        }
        
        private void SetSongWeightsForUser(ulong u)
        {
            foreach (var s in songs[u])
            {
                foreach (var l in listeners)
                    s.CalculateWeightForListener(l);
            }
        }

        private static T WeightedRandom<T>(Dictionary<T, double> dict)
        {
            double length = dict.Values.Sum();
            var random = new Random();
            double d = random.NextDouble() * length;
            double p = 0;
            foreach (var item in dict.Keys)
            {
                if (p + dict[item] < d) p += dict[item];
                else return item;
            }
            throw new Exception("p overexceeded dict weights");
        }
        
        private Func<Song, double> GetBiasByAlgorithm(RadioAlgorithms algorithm)
        {
            static double UnchartedFunc(double n, float max)
            {
                return (-Math.Cos(n * 2 * Math.PI / max / 2) + 0.5) * max;
            }
            
            switch (algorithm)
            {
                case RadioAlgorithms.radio:
                    return song =>
                    {
                        return song.userWeights.Keys.Sum(user => song.userWeights[user] * userWeights[user]);
                    };

                case RadioAlgorithms.uncharted:
                    return song =>
                    {
                        return song.userWeights.Keys.Sum(user =>
                            UnchartedFunc(song.userWeights[user], 250) * userWeights[user]);
                    };
                
                case RadioAlgorithms.explore:
                    return song =>
                    {
                        return song.userWeights.Keys.Sum(
                            user => song.userWeights[user] * Math.Pow(userWeights[user], 5));
                    };
            }

            throw new Exception("Algorithm provided does not have a function (tf yoav)");
        }
        
        
        public FullTrack Next(RadioAlgorithms algorithm=RadioAlgorithms.radio)
        {
            Dictionary<Song, double> songRatings = new();
            Func<Song, double> function = GetBiasByAlgorithm(algorithm);
            
            foreach (var u in songs.Keys)
            {
                foreach (var s in songs[u])
                {
                    songRatings.Add(s, function(s));
                }
            }

            Song result = WeightedRandom(songRatings);
            return result.track;
        }
        
        
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
        
        public static void AAMain(string[] args)
        {
            var t = DateTime.Now;
            RadioPlayer r = new(new(){329960504376426496});
            r.Set().GetAwaiter().GetResult();
            Console.WriteLine((DateTime.Now - t).Seconds);
            
            t = DateTime.Now;
            r = new(new(){329960504376426496, 417610386871812098});
            r.Set().GetAwaiter().GetResult();
            Console.WriteLine((DateTime.Now - t).Seconds);
            
            t = DateTime.Now;
            r = new(new(){329960504376426496, 417610386871812098, 315068042910498816, 368039129348440085});
            r.Set().GetAwaiter().GetResult();
            Console.WriteLine((DateTime.Now - t).Seconds);
        }  
    }
    
    
}
