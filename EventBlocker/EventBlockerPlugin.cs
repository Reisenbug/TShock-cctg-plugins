using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace EventBlocker
{
    [ApiVersion(2, 1)]
    public class EventBlockerPlugin : TerrariaPlugin
    {
        public override string Name => "EventBlocker";
        public override string Author => "stardust";
        public override string Description => "ban blood moons, slime rains, and goblin invasions.";
        public override Version Version => new Version(1, 0, 0);

        public EventBlockerPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            // Register the GameUpdate hook to monitor and block events
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

            TShock.Log.ConsoleInfo("EventBlocker is initialized.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            }
            base.Dispose(disposing);
        }

        private void OnGameUpdate(EventArgs args)
        {
            // ban bloodmoon
            if (Main.bloodMoon)
            {
                Main.bloodMoon = false;
                TShock.Log.ConsoleInfo("[EventBlocker] prevented a blood moon event");
            }

            // 禁止史莱姆雨
            if (Main.slimeRain)
            {
                Main.StopSlimeRain(false);
                TShock.Log.ConsoleInfo("[EventBlocker] prevented a slime rain event");
            }

            // 禁止哥布林军团入侵
            if (Main.invasionType == 1) // 1 = 哥布林军团
            {
                Main.invasionType = 0;
                Main.invasionSize = 0;
                TShock.Log.ConsoleInfo("[EventBlocker] prevented a goblin invasion event");
            }
        }
    }
}
