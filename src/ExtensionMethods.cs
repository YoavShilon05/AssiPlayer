using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    public static class ExtensionMethods
    {
        public static float Bias(float n, float max, float bias)
        {
            return MathF.Pow((n / max), bias);
        }
        
        public static List<T> Sort<T, I>(this Dictionary<T, I> dict)
        {
            Dictionary<I, List<T>> weightedDict = dict.Reverse();
            List<I> keys = weightedDict.Keys.ToList();
            keys.Sort();

            List<T> result = new();

            foreach (var k in keys)
                result.AddRange(weightedDict[k]);
            
            return result;
        }

        public static Dictionary<R, List<T>> Reverse<T, R>(this Dictionary<T, R> dict)
        {
            Dictionary<R, List<T>> result = new();
            foreach(T k in dict.Keys)
            {
                if (result.ContainsKey(dict[k])) result[dict[k]].Add(k);
                else result.Add(dict[k], new List<T>{k});
            }

            return result;
        }

        public static string Join(this string slicer, IEnumerable<string> arr)
        {
            string result = "";
            var i = 0;
            var enumerable = arr as string[] ?? arr.ToArray();
            foreach (var s in enumerable)
            {
                i++;
                result += s;
                if (i < enumerable.Length)
                     result += slicer;
            }

            return result;
        }

        public static T RandomChoice<T>(this IList<T> arr)
        {
            Random r = new();
            var idx = r.Next(arr.Count);
            return arr[idx];
        }

        public static async Task<FullTrack> GetFull(this SimpleTrack track)
        {
            return await SpotifyManager.GlobalClient.Tracks.Get(track.Id);
        }
        
        public static async Task<FullAlbum> GetFull(this SimpleAlbum album)
        {
            return await SpotifyManager.GlobalClient.Albums.Get(album.Id);
        }
        
        public static async Task<FullArtist> GetFull(this SimpleArtist artist)
        {
            return await SpotifyManager.GlobalClient.Artists.Get(artist.Id);
        }

        public static IEnumerable<T> Extend<T>(this IEnumerable<T> arr, IEnumerable<T> other)
        {
            var l = new List<T>(arr);
            l.AddRange(other);
            return l;
        }

        public static IEnumerable<T> Clean<T>(this IEnumerable<T> arr)
        {
            return arr.Where(item => item != null).ToList();
        }

        public static void Remove<T>(this Queue<T> queue, int index)
        {
            var list = new List<T>();
            for (int i = 0; i < index; i++)
                list.Add(queue.Dequeue());
            for (int i = 0; i < index - 1; i++)
                queue.Enqueue(list[i]);
        }

    }
}
