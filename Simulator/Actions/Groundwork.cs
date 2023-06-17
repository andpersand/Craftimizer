namespace Craftimizer.Simulator.Actions;

internal class Groundwork : BaseAction
{
    public override ActionCategory Category => ActionCategory.Synthesis;
    public override int Level => 72;
    public override uint ActionId => 100403;

    public override int CPCost => 18;
    // Groundwork Mastery Trait
    public override float Efficiency
    {
        get
        {
            var ret = Simulation.Input.Stats.Level >= 86 ? 3.60f : 3.00f;
            // TODO: does not account for waste not
            return Simulation.Durability < DurabilityCost ? ret / 2 : ret;
        }
    }
    public override bool IncreasesProgress => true;
    public override int DurabilityCost => 20;
}