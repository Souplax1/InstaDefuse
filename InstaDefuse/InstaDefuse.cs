using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
 

namespace InstaDefuse;

public partial class InstaDefuse : BasePlugin

{
    public override string ModuleName => "InstaDefuse";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleDescription => "Defuse the bomb instantly or explode it if defuse fails.";
    public override string ModuleAuthor => "Yeezy";

    private InstaDefuseConfig _config = new();
    private float _bombPlantedTime = float.NaN;
    public override void Load(bool hotReload)
    {
       EnsureConfigFile();
       RegisterEventHandler<EventBombBegindefuse>(OnBombBeginDefuse);
       RegisterEventHandler<EventBombPlanted>(OnBombPlanted);
    }

    private static CPlantedC4? FindPlantedBomb()
    {
        var plantedBombList = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").ToList();

        if (plantedBombList.Any())
        {
            return plantedBombList.FirstOrDefault();
        }

        Console.WriteLine($"No planted bomb entities have been found!");
        return null;
    }

    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
      
        _bombPlantedTime = Server.CurrentTime;
       

        return HookResult.Continue;
    }


    private static bool TeamHasAlivePlayers(CsTeam team)
    {
        var players = Utilities.GetPlayers();

        foreach (var player in players)
        {
            if (!player.IsValid) continue;
            if (player.Team != team) continue;
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null) continue;
            if (pawn.Health <= 0) continue;

            return true;
        }

        return false;
    }


    private HookResult OnBombBeginDefuse(EventBombBegindefuse @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player != null && player.IsValid && player.PawnIsAlive)
        {
            AttemptInstadefuse(player);
        }

        return HookResult.Continue;
    }

    private void AttemptInstadefuse(CCSPlayerController defuser)
    {


        var plantedBomb = FindPlantedBomb();
        if (plantedBomb == null)
        {
            Console.WriteLine($"Planted bomb is null!");
            return;
        }

        if (plantedBomb.CannotBeDefused)
        {
            return;
        }

        if (float.IsNaN(_bombPlantedTime))
        {
            Console.WriteLine($"Bomb planted time is not set; skipping insta-defuse.");
            return;
        }


        var bombTimeUntilDetonation = plantedBomb.TimerLength - (Server.CurrentTime - _bombPlantedTime);

        var defuseLength = plantedBomb.DefuseLength;
        if (defuseLength != 5 && defuseLength != 10)
        {
            defuseLength = defuser.PawnHasDefuser ? 5.0f : 10.0f;
        }

        var timeLeftAfterDefuse = bombTimeUntilDetonation - defuseLength;
        var bombCanBeDefusedInTime = timeLeftAfterDefuse >= 0.0f;

        if (bombCanBeDefusedInTime)
        {
            if (TeamHasAlivePlayers(CsTeam.Terrorist))
            {
                Console.WriteLine($"Terrorists are still alive");
                return;
            }
            Server.NextFrame(() =>
            {
                plantedBomb = FindPlantedBomb();

                if (plantedBomb == null)
                {
                    Console.WriteLine($"Planted bomb is null!");
                    return;
                }

                plantedBomb.DefuseCountDown = 0;

                Server.PrintToChatAll($"{BuildPrefix()} {ChatColors.Green}{defuser.PlayerName}{ChatColors.White} Insta defused.");
            });
        }
        else
        {
            if (!_config.InstaExplode)
            {
                return;
            }
            var early = bombTimeUntilDetonation;
            if (early < 0.0f) early = 0.0f;
            Server.NextFrame(() =>
            {
                var bomb = FindPlantedBomb();
                if (bomb != null)
                {
                    bomb.C4Blow = Server.CurrentTime + 0.01f;
                }
                Server.PrintToChatAll($"{BuildPrefix()} Bomb exploded {ChatColors.Green}{early:0.00}s{ChatColors.White} early due to a failed attempt to defuse by {ChatColors.Blue}{defuser.PlayerName}{ChatColors.White}");
            });
        }
    }
 
}

internal sealed record InstaDefuseConfig(
    [property: JsonPropertyName("chat_prefix")] string ChatPrefix = "InstaDefuse",
    [property: JsonPropertyName("prefix_color")] string PrefixColor = "green",
    [property: JsonPropertyName("insta_explode")] bool InstaExplode = true
);

partial class InstaDefuse
{
    private string BuildPrefix()
    {
        var color = ColorFromName(_config.PrefixColor);
        return $"{ChatColors.White}[ {color}{_config.ChatPrefix}{ChatColors.White} ]";
    }

    private string ColorFromName(string? name)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "green": return $"{ChatColors.Green}";
            case "blue": return $"{ChatColors.Blue}";
            case "red": return $"{ChatColors.Red}";
            case "yellow": return $"{ChatColors.Yellow}";
            case "white": return $"{ChatColors.White}";
            case "grey":
            case "gray": return $"{ChatColors.Grey}";
            default: return $"{ChatColors.Green}";
        }
    }
    private string GetConfigPath()
    {
        var pluginName = ModuleName;
        var gameRoot = GetGameRootDirectory();
        var path = Path.Combine(gameRoot, "addons", "counterstrikesharp", "configs", "plugins", pluginName, "config.json");
        return path;
    }

    private string GetGameRootDirectory()
    {
        // Use CounterStrikeSharp's game directory
        var root = Server.GameDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            // Minimal fallback to container standard
            var known = Path.Combine(Path.DirectorySeparatorChar.ToString(), "game", "csgo");
            return Directory.Exists(known) ? known : AppContext.BaseDirectory;
        }

        // Prefer csgo subfolder if present
        var csgo = Path.Combine(root, "csgo");
        return Directory.Exists(csgo) ? csgo : root;
    }

    private void EnsureConfigFile()
    {
        try
        {
            var configPath = GetConfigPath();
            string? dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(configPath))
            {
                var defaultConfig = new InstaDefuseConfig();
                var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }

            LoadConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to ensure config file: {ex.Message}");
        }
    }

    private void LoadConfig()
    {
        try
        {
            var path = GetConfigPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<InstaDefuseConfig>(json);
                if (cfg != null)
                {
                    _config = cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load config file: {ex.Message}");
        }
    }
}
