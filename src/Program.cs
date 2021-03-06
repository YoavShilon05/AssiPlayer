using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;

namespace AssiSharpPlayer
{
    public static class Program
    {
        public static Dictionary<ulong, Player> players = new();
        public static Bot Bot { get; private set; }

        private static void NOTMain()
        {
            Bot = new(new[] {"s."});
            Bot.Run(File.ReadAllText("Token.txt")).GetAwaiter().GetResult();
        }
    }
}
