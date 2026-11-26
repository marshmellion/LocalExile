using GameServer.Modding;

namespace GameServer.Modules.PhreciaAscendancies;

/// <summary>Optional admission and node-stat sources for the implemented Phrecia classes.</summary>
public sealed class PhreciaAscendanciesModule : IModule
{
    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        // The family's node stats and buffs (PhreciaNodeStats, its three aura templates) are the Game module's, for every
        // league: a jewel can allocate one of these nodes on any character.
        AscendancyPolicies.Register(module.Lifetime, new PhreciaFamilyPolicy());
        var gameplay = new Classes.Harbinger.HarbingerGameplay();
        GameEvents.CompanionTick.Add(module.Manifest.Id, gameplay.Tick);
        GameEvents.SkillCastAt.Add(module.Manifest.Id, gameplay.Cast);
        GameEvents.PlayerStatSources.Add(module.Manifest.Id, gameplay.StatsFor);
        GameEvents.GrantedSkillExecutable.Add(module.Manifest.Id, gameplay.Executable);
        GameEvents.TransientClosing.Add(module.Manifest.Id, gameplay.Close);
        GameEvents.PauseChanged.Add(module.Manifest.Id, gameplay.Pause);
    }
}
