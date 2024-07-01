using Craftimizer.Simulator;
using Craftimizer.Simulator.Actions;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Craftimizer.Solver;

internal sealed class Simulator : SimulatorNoRandom
{
    private readonly (BaseAction Data, ActionType Action)[] actionPoolObjects;
    private readonly int maxStepCount;

    public override CompletionState CompletionState
    {
        get
        {
            var b = base.CompletionState;
            if (b == CompletionState.Incomplete && (ActionCount + 1) >= maxStepCount)
                return CompletionState.MaxActionCountReached;
            return b;
        }
    }

    public Simulator(ActionType[] actionPool, int maxStepCount, SimulationState? filteringState = null)
    {
        var pool = actionPool.Select(x => (x.Base(), x));
        if (filteringState is { } state)
        {
            State = state;
            pool = pool.Where(x => x.Item1.IsPossible(this));
        }
        actionPoolObjects = [.. pool.OrderBy(x => x.x)];
        this.maxStepCount = maxStepCount;
    }

    // https://github.com/alostsock/crafty/blob/cffbd0cad8bab3cef9f52a3e3d5da4f5e3781842/crafty/src/craft_state.rs#L146
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CouldUseAction(BaseAction baseAction)
    {
        if (CalculateSuccessRate(baseAction.SuccessRate(this)) != 100)
            return false;

        // don't allow quality moves at max quality
        if (Quality >= Input.Recipe.MaxQuality && baseAction.IncreasesQuality)
            return false;

        return baseAction.CouldUse(this);
    }

    // https://github.com/alostsock/crafty/blob/cffbd0cad8bab3cef9f52a3e3d5da4f5e3781842/crafty/src/craft_state.rs#L146
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // It's just a bunch of if statements, I would assume this is actually quite simple to follow
#pragma warning disable MA0051 // Method is too long
    private bool ShouldUseAction(ActionType action, BaseAction baseAction)
#pragma warning restore MA0051 // Method is too long
    {
        if (CalculateSuccessRate(baseAction.SuccessRate(this)) != 100)
            return false;

        // don't allow quality moves at max quality
        if (Quality >= Input.Recipe.MaxQuality && baseAction.IncreasesQuality)
            return false;

        // always use Trained Eye if it's available
        if (action == ActionType.TrainedEye)
            return baseAction.CouldUse(this);

        var isDifficult = Input.Recipe.ClassJobLevel >= Input.Stats.Level;

        // don't allow quality moves under Muscle Memory for difficult crafts
        if (isDifficult &&
            HasEffect(EffectType.MuscleMemory) &&
            baseAction.IncreasesQuality)
            return false;

        // use First Turn actions if it's available and the craft is difficult
        if (IsFirstStep &&
            Input.Stats.Level >= 69 &&
            isDifficult &&
            baseAction.Category != ActionCategory.FirstTurn &&
            CP >= 6)
            return false;

        // don't allow combo actions if the combo is already in progress
        if (ActionStates.TouchComboIdx != 0 &&
            (action is ActionType.StandardTouchCombo or ActionType.AdvancedTouchCombo or ActionType.RefinedTouchCombo))
            return false;

        // don't allow observe actions if the combo (or observe itself) is already in progress
        if (ActionStates.ObserveCombo && action is ActionType.ObservedAdvancedTouchCombo)
            return false;

        if (action is ActionType.Observe && CP < 25)
            return false;

        // don't allow pure quality moves under Veneration
        if (HasEffect(EffectType.Veneration) &&
            !baseAction.IncreasesProgress &&
            baseAction.IncreasesQuality)
            return false;

        var durability = CalculateDurabilityCost(baseAction.DurabilityCost);

        // don't allow pure quality moves when it won't be able to finish the craft
        if (!baseAction.IncreasesProgress && durability >= Durability)
            return false;

        // don't allow 0 durability moves under Trained Perfection
        if (HasEffect(EffectType.TrainedPerfection) && durability < 10)
            return false;

        if (baseAction.IncreasesProgress)
        {
            var progressIncrease = CalculateProgressGain(baseAction.Efficiency(this));
            var wouldFinish = Progress + progressIncrease >= Input.Recipe.MaxProgress;

            if (wouldFinish)
            {
                // don't allow finishing the craft if there is significant quality remaining
                if (Quality < Input.Recipe.MaxQuality / 5)
                    return false;
            }
            else
            {
                // don't allow pure progress moves under Innovation, if it wouldn't finish the craft
                if (HasEffect(EffectType.Innovation) &&
                    !baseAction.IncreasesQuality &&
                    baseAction.IncreasesProgress)
                    return false;
            }
        }

        if (action == ActionType.ByregotsBlessing &&
            GetEffectStrength(EffectType.InnerQuiet) <= 1)
            return false;

        if ((action == ActionType.WasteNot || action == ActionType.WasteNot2) &&
            (HasEffect(EffectType.WasteNot) || HasEffect(EffectType.WasteNot2)))
            return false;

        if (action == ActionType.MastersMend &&
            Input.Recipe.MaxDurability - durability < 25)
            return false;

        if (action == ActionType.ImmaculateMend &&
            Input.Recipe.MaxDurability - durability < 45)
            return false;

        if (action == ActionType.RefinedTouch &&
            ActionStates.TouchComboIdx != 1)
            return false;

        if (action == ActionType.QuickInnovation &&
            (Quality - Input.StartingQuality) < Input.Recipe.MaxQuality / 2)
            return false;

        if (action == ActionType.Manipulation &&
            HasEffect(EffectType.Manipulation))
            return false;

        if (action == ActionType.GreatStrides &&
            HasEffect(EffectType.GreatStrides))
            return false;

        if ((action is ActionType.Veneration or ActionType.Innovation) &&
            (GetEffectDuration(EffectType.Veneration) > 1 || GetEffectDuration(EffectType.Innovation) > 1))
            return false;

        return baseAction.CouldUse(this);
    }

    // https://github.com/alostsock/crafty/blob/cffbd0cad8bab3cef9f52a3e3d5da4f5e3781842/crafty/src/craft_state.rs#L137
    public ActionSet AvailableActionsHeuristic(bool strict)
    {
        if (IsComplete)
            return new();

        var ret = new ActionSet();
        if (strict)
        {
            foreach (var (data, action) in actionPoolObjects)
            {
                if (ShouldUseAction(action, data))
                    ret.AddAction(action);
            }

            // If Trained Eye is possible, *always* use Trained Eye
            if (ret.HasAction(ActionType.TrainedEye))
            {
                ret = new();
                ret.AddAction(ActionType.TrainedEye);
            }
        }
        else
        {
            foreach (var (data, action) in actionPoolObjects)
                if (CouldUseAction(data))
                    ret.AddAction(action);
        }

        return ret;
    }
}
