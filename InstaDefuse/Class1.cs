using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;

namespace InstaDefuse;

public class InstaDefuse : BasePlugin

{
    public override string ModuleName => "InstaDefuse";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleDescription => "Defuse the bomb instantly.";
    public override string ModuleAuthor => "Yeezy";
    public override void Load(bool hotReload)
    {
       RegisterEventHandler<EventBombBegindefuse>(OnBombBeginDefuse);
    }


    private HookResult OnBombBeginDefuse(EventBombBegindefuse @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
            return HookResult.Continue;

        var bomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
        if (bomb == null || !bomb.IsValid || bomb.BombDefused)
            return HookResult.Continue;

        // Check if all Terrorists are dead
        bool allTerroristsDead = true;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p.Team == CsTeam.Terrorist && p.PawnIsAlive) 
            {
                allTerroristsDead = false;
                break;
            }
        }

        if (allTerroristsDead || @event.Haskit)
        {
            bomb.DefuseCountDown = 0.1f;
            Utilities.SetStateChanged(bomb, "CPlantedC4", "m_bBombDefused");
            Utilities.SetStateChanged(bomb, "CPlantedC4", "m_flDefuseCountDown");

            Server.NextFrame(() =>
            {
                AddTeamScore((int)CsTeam.CounterTerrorist);

                var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                    .FirstOrDefault()?.GameRules;

                if (gameRules != null)
                {
                    gameRules.TerminateRound(0.5f, RoundEndReason.BombDefused);
                }
            });

            return HookResult.Changed;
        }

        return HookResult.Continue;
    }

    private void AddTeamScore(int team)
    {
        var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
        foreach (var teamManager in teamManagers)
        {
            if (teamManager.TeamNum == team)
            {
                teamManager.Score++;
                Utilities.SetStateChanged(teamManager, "CTeam", "m_iScore");
                break; // Exit after finding the correct team
            }
        }
    }
}
