using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using Newtonsoft.Json;
using SpotifyAPI.Web;
using Swan;
using Swan.Formatters;

namespace AssiSharpPlayer
{
    public static class Program
    {
        public static Dictionary<ulong, Player> players = new();
        public static Dictionary<ulong, List<string>> blacklists = new();
        public static Bot Bot { get; private set; }

        public static void SerialzieBlacklists()
        {
            File.WriteAllText("Blacklists.json", blacklists.ToJson());
        }
        
        private static void Main()
        {
            blacklists = JsonConvert.DeserializeObject<Dictionary<ulong, List<string>>>(File.ReadAllText("Blacklists.json"));
            
            Bot = new(new[] {"s."});
            Bot.Run(File.ReadAllText("Token.txt")).GetAwaiter().GetResult();

            SpotifyManager.SaveCache();
        }
    }
}
