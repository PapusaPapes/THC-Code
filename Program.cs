using System;
using System.Collections.Generic;
using TeamHeroCoderLibrary;

namespace PlayerCoder
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Connecting...");

            var connectionManager = new GameClientConnectionManager();
            connectionManager.SetExchangePath(MyAI.FolderExchangePath);
            connectionManager.onHeroHasInitiative = MyAI.ProcessAI;
            connectionManager.StartListeningToGameClientForHeroPlayRequests();
        }
    }

    // Team: Monk / Wizard / Alchemist
    // Items: 2 Ether, 42 Essence
    // Strategy:
    // Wizard opens with PoisonNova to poison all enemies.
    // Monk follows with FlurryOfBlows, Bitter Bloom amplifies damage on poisoned targets.
    // Alchemist slows enemies, hastes Monk, and crafts sustain reactively.
    // Wizard uses Doom as a fallback win condition when burst isn't enough.

    public static class MyAI
    {
        public static string FolderExchangePath =
            "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // Health thresholds
        private const float HpCritical     = 0.30f;
        private const float HpLow          = 0.55f;
        private const float HpLight        = 0.75f;

        // Mana thresholds
        private const float MpLow = 0.25f;

        // Combat thresholds
        private const float FinishHp = 0.35f;

        // Alchemist mana and essence costs
        private const int MinManaToSlow    = 15;
        private const int EssenceCostTier1 = 2;
        private const int EssenceCostTier2 = 3;
        private const int EssenceCostTier3 = 4;

        // Healers and crafters die first, tanks die last
        private static readonly HeroJobClass[] KillOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Wizard,
            HeroJobClass.Rogue,
            HeroJobClass.Monk,
            HeroJobClass.Fighter
        };

        // Wizard is the win condition, revive it first, Monk last
        private static readonly HeroJobClass[] ReviveOrder =
        {
            HeroJobClass.Wizard,
            HeroJobClass.Alchemist,
            HeroJobClass.Monk
        };

        // Wizard is most important to cleanse, PoisonNova is the win condition
        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Wizard,
            HeroJobClass.Alchemist,
            HeroJobClass.Monk
        };

        // Used to check if the team is safe enough to apply buffs
        private static readonly StatusEffect[] DangerousDebuffs =
        {
            StatusEffect.Doom,
            StatusEffect.Petrified,
            StatusEffect.Petrifying,
            StatusEffect.Poison
        };

        public static void ProcessAI()
        {
            Hero actor = TeamHeroCoder.BattleState.heroWithInitiative;

            if (actor == null || actor.health <= 0)
                return;

            Console.WriteLine($"Actor: {actor.jobClass} HP:{actor.health}/{actor.maxHealth} MP:{actor.mana}/{actor.maxMana}");

            switch (actor.jobClass)
            {
                case HeroJobClass.Monk:
                    ControlMonk(actor);
                    return;

                case HeroJobClass.Wizard:
                    ControlWizard(actor);
                    return;

                case HeroJobClass.Alchemist:
                    ControlAlchemist(actor);
                    return;

                default:
                    Wait(actor);
                    return;
            }
        }

        // ============================================================
        // MONK
        // ============================================================

        private static void ControlMonk(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;

            if (HealSelf(actor))   return;
            if (ApplyBrave(actor)) return;

            // Item Crafter: skip debuffing, focus the Alchemist directly
            if (IsItemCrafterLike())
            {
                Hero alchemist = FindLivingFoe(HeroJobClass.Alchemist);

                if (alchemist != null)
                {
                    if (AllEnemiesPoisoned() &&
                        Act(actor, Ability.FlurryOfBlows, alchemist)) return;
                    if (Act(actor, Ability.Attack, alchemist))         return;
                }

                if (FinishPhysicalTarget(actor))                    return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                if (Act(actor, Ability.Attack, FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            // FlurryOfBlows with Bitter Bloom is the win condition
            // prioritize it over debuffing once enemies are poisoned
            if (AllEnemiesPoisoned() &&
                Act(actor, Ability.FlurryOfBlows, BestAttackTarget())) return;

            // Apply debuffs once each then get out of the way
            if (DebraveThreats(actor))  return;
            if (DefaithThreats(actor))  return;

            // If enemies aren't all poisoned yet, still attack while waiting
            if (FinishPhysicalTarget(actor))                    return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;
            if (Act(actor, Ability.Attack, FirstLivingFoe()))   return;

            Wait(actor);
        }

        private static bool ApplyBrave(Hero actor)
        {
            if (HasStatus(actor, StatusEffect.Brave)) return false;

            return Act(actor, Ability.Brave, actor);
        }

        private static bool DebraveThreats(Hero actor)
        {
            if (Act(actor, Ability.Debrave, FindWithoutDebrave(HeroJobClass.Fighter))) return true;
            if (Act(actor, Ability.Debrave, FindWithoutDebrave(HeroJobClass.Monk)))    return true;
            if (Act(actor, Ability.Debrave, FindWithoutDebrave(HeroJobClass.Rogue)))   return true;

            return false;
        }

        private static bool DefaithThreats(Hero actor)
        {
            if (Act(actor, Ability.Defaith, FindWithoutDefaith(HeroJobClass.Wizard)))    return true;
            if (Act(actor, Ability.Defaith, FindWithoutDefaith(HeroJobClass.Cleric)))    return true;
            if (Act(actor, Ability.Defaith, FindWithoutDefaith(HeroJobClass.Alchemist))) return true;

            return false;
        }

        private static bool HealSelf(Hero actor)
        {
            if (HpRatio(actor) > HpLight) return false;

            if (Act(actor, Ability.QuickHeal,   actor)) return true;
            if (Act(actor, Ability.CureSerious, actor)) return true;

            return false;
        }

        // ============================================================
        // WIZARD
        // ============================================================

        private static void ControlWizard(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (DispelEnemyAutoLife(actor, Ability.QuickDispel)) return;

            // Brave Advance: 3 Fighters with Brave, skip PoisonNova, Doom and burst
            if (IsBraveAdvanceLike() && BurstFighters(actor)) return;

            // Item Crafter: spam Doom to drain enemy Essence, Full Remedies are expensive
            if (IsItemCrafterLike())
            {
                if (DoomAllTargets(actor))                                return;
                if (FinishMagicTarget(actor))                             return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
                if (Act(actor, Ability.Attack,       BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,       FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            // Lmt Brk Crafter: Alchemist crafts infinite sustain, Doom it immediately
            if (IsLmtBrkCrafterLike())
            {
                if (DoomAllTargets(actor))                                return;
                if (CountUnpoisonedFoes() > 0 &&
                    Act(actor, Ability.PoisonNova, BestMagicTarget()))    return;
                if (PetrifyThreats(actor))                                return;
                if (FinishMagicTarget(actor))                             return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
                if (Act(actor, Ability.Attack,       BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,       FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            // Petrification: 2+ Wizards clear poison with Full Remedies every turn
            if (IsMultiWizardLike())
            {
                if (DoomAllTargets(actor))                                 return;
                if (PetrifyThreats(actor))                                 return;
                if (SlowAllTargets(actor))                                 return;
                if (FinishMagicTarget(actor))                              return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget()))  return;
                if (Act(actor, Ability.Attack,        BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,        FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            // Poison Tribal: no Full Remedies, Doom is guaranteed, just survive the burst
            if (IsPoisonTribalLike())
            {
                if (UseEmergencyItem(actor))                               return;
                if (UseEther(actor, MpLow))                                return;
                if (DoomAllTargets(actor))                                 return;
                if (FinishMagicTarget(actor))                              return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget()))  return;
                if (Act(actor, Ability.Attack,        BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,        FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            // PoisonNova first, sets up Bitter Bloom amplification for the Monk
            if (CountUnpoisonedFoes() > 0 &&
                Act(actor, Ability.PoisonNova, BestMagicTarget())) return;

            if (DoomAllTargets(actor))   return;
            if (PetrifyThreats(actor))   return;
            if (SlowAllTargets(actor))   return;

            if (FinishMagicTarget(actor))                              return;
            if (Act(actor, Ability.MagicMissile, BestMagicTarget()))  return;
            if (Act(actor, Ability.Attack,        BestAttackTarget())) return;
            if (Act(actor, Ability.Attack,        FirstLivingFoe()))   return;

            Wait(actor);
        }

        // Brave Advance: burst Fighters with magic before Brave stacks get out of hand
        private static bool BurstFighters(Hero actor)
        {
            if (Act(actor, Ability.Meteor,       BestMagicTarget()))  return true;
            if (Act(actor, Ability.Fireball,     BestMagicTarget()))  return true;
            if (Act(actor, Ability.MagicMissile, BestMagicTarget()))  return true;
            if (Act(actor, Ability.Attack,        BestAttackTarget())) return true;

            return false;
        }

        private static bool DoomAllTargets(Hero actor)
        {
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Cleric)))    return true;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Alchemist))) return true;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Fighter)))   return true;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Monk)))      return true;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Rogue)))     return true;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Wizard)))    return true;

            return false;
        }

        private static bool PetrifyThreats(Hero actor)
        {
            if (Act(actor, Ability.Petrify, FindUnpetrified(HeroJobClass.Monk)))    return true;
            if (Act(actor, Ability.Petrify, FindUnpetrified(HeroJobClass.Fighter))) return true;
            if (Act(actor, Ability.Petrify, FindUnpetrified(HeroJobClass.Rogue)))   return true;

            return false;
        }

        // ============================================================
        // ALCHEMIST
        // ============================================================

        private static void ControlAlchemist(Hero actor)
        {
            // Self-preservation first, a dead Alchemist can't craft or revive
            if (HpRatio(actor) <= HpLow && HealCriticalAlly(actor)) return;

            // Poison Tribal: skip everything except survival and Doom support
            if (IsPoisonTribalLike())
            {
                if (CraftVsPoisonTribal(actor))      return;
                if (HealWizardUrgently(actor))       return;
                if (UseRemedyOnPoisonedAlly(actor))  return;
                if (UseRemedyOnDoomedAlly(actor))    return;
                if (UseEmergencyItem(actor))         return;
                if (UseEther(actor, MpLow))          return;
                if (CraftSupportItems(actor))        return;
                if (ReviveOrCraftRevive(actor))      return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;

                Wait(actor);
                return;
            }

            // Brave Advance: Fighters hit incredibly hard, keep Wizard alive above all else
            if (IsBraveAdvanceLike())
            {
                if (HealCriticalAlly(actor))                        return;
                if (HealWizardUrgently(actor))                      return;
                if (ReviveOrCraftRevive(actor))                     return;
                if (UseEmergencyItem(actor))                        return;
                if (UseEther(actor, MpLow))                         return;
                if (CraftSupportItems(actor))                       return;
                if (SlowAllTargets(actor))                          return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;

                Wait(actor);
                return;
            }

            // Item Crafter: keep Monk alive so it can keep attacking the enemy Alchemist
            if (IsItemCrafterLike())
            {
                if (HealCriticalAlly(actor))                        return;
                if (ReviveOrCraftRevive(actor))                     return;
                if (HealWizardUrgently(actor))                      return;
                if (UseEmergencyItem(actor))                        return;
                if (UseEther(actor, MpLow))                         return;
                if (CraftNeededRemedy(actor))                       return;

                // Craft Ether first, then MegaElixir, MegaElixir restores mana too
                if (Essence() >= EssenceCostTier1 &&
                    !AnyAllyHasItem(Ability.Ether) &&
                    SelfCast(actor, Ability.CraftEther))            return;

                if (Essence() >= EssenceCostTier3 &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftMegaElixir))       return;

                // Ether the Wizard so it can keep casting
                Hero wizard = FindLivingAlly(HeroJobClass.Wizard);
                if (wizard != null && MpRatio(wizard) <= MpLow &&
                    Act(actor, Ability.Ether, wizard))              return;

                if (Act(actor, Ability.Attack, BestAttackTarget())) return;

                Wait(actor);
                return;
            }

            // Multiple enemy Wizards means Petrify spam, craft remedies before anything else
            if (CraftVsMultipleWizards(actor)) return;

            // Wizard is the win condition, heal it before anything else if low
            if (HealWizardUrgently(actor)) return;

            if (SlowEnemyWizard(actor))          return;
            if (CleansePetrifyIfNoRemedy(actor)) return;
            if (CleanseDoomIfNoRemedy(actor))    return;

            // Actively use remedies on doomed or petrified allies, don't just craft and wait
            if (UseRemedyOnDoomedAlly(actor))    return;
            if (UseRemedyOnPetrifiedAlly(actor)) return;
            if (UseRemedyOnPoisonedAlly(actor))  return;

            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (HealCriticalAlly(actor)) return;

            if (CraftNeededRemedy(actor))   return;
            if (CraftSupportItems(actor))   return;
            if (ReviveOrCraftRevive(actor)) return;

            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            if (HasteMonk(actor)) return;

            if (actor.mana >= MinManaToSlow && SlowAllTargets(actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        // MegaElixir fully heals the team, use it reactively when anyone is critical
        private static bool HealCriticalAlly(Hero actor)
        {
            // MegaElixir fully heals everyone, use it whenever anyone is critical
            if (CountBelow(HpCritical) > 0 &&
                SelfCast(actor, Ability.MegaElixir)) return true;

            if (HpRatio(actor) <= HpLow)
            {
                if (Act(actor, Ability.Elixir, actor)) return true;
                if (Act(actor, Ability.Potion, actor)) return true;
            }

            Hero lowest = LowestAlly();
            if (lowest != null && HpRatio(lowest) <= HpCritical)
            {
                if (Act(actor, Ability.Elixir, lowest)) return true;
                if (Act(actor, Ability.Potion, lowest)) return true;
            }

            return false;
        }

        private static bool SlowEnemyWizard(Hero actor)
        {
            if (actor.mana < MinManaToSlow)             return false;
            if (FindLivingFoe(HeroJobClass.Wizard) == null) return false;

            return Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Wizard));
        }

        // Haste the Monk to maximize FlurryOfBlows turns
        private static bool HasteMonk(Hero actor)
        {
            if (!TeamIsStable()) return false;

            Hero monk = FindLivingAlly(HeroJobClass.Monk);

            if (monk == null)                           return false;
            if (HasStatus(monk, StatusEffect.Haste))    return false;

            return Act(actor, Ability.Haste, monk);
        }

        // Multiple enemy Wizards means Petrify spam, craft Petrify and Full Remedies proactively
        private static bool CraftVsMultipleWizards(Hero actor)
        {
            if (CountEnemyClass(HeroJobClass.Wizard) < 2) return false;
            if (Essence() < EssenceCostTier1)             return false;

            if (!AnyAllyHasItem(Ability.PetrifyRemedy) &&
                SelfCast(actor, Ability.CraftPetrifyRemedy)) return true;

            if (!AnyAllyHasItem(Ability.FullRemedy) &&
                SelfCast(actor, Ability.CraftFullRemedy)) return true;

            return false;
        }

        private static bool HealWizardUrgently(Hero actor)
        {
            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);

            if (wizard == null)           return false;
            if (HpRatio(wizard) > HpLow)  return false;

            if (Act(actor, Ability.Elixir, wizard)) return true;
            if (Act(actor, Ability.Potion, wizard)) return true;

            return false;
        }

        private static bool UseRemedyOnPoisonedAlly(Hero actor)
        {
            if (!IsPoisonTribalLike()) return false;

            Hero poisoned = FindAllyWithStatus(StatusEffect.Poison);
            if (poisoned == null) return false;

            return Act(actor, Ability.PoisonRemedy, poisoned);
        }

        private static bool UseRemedyOnDoomedAlly(Hero actor)
        {
            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            if (doomed == null) return false;

            if (Act(actor, Ability.FullRemedy, doomed)) return true;

            return false;
        }

        private static bool UseRemedyOnPetrifiedAlly(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
            if (petrified == null) return false;

            if (Act(actor, Ability.PetrifyRemedy, petrified)) return true;
            if (Act(actor, Ability.FullRemedy,    petrified)) return true;

            return false;
        }

        // Poison Tribal: 3 Monks with Bitter Bloom, our poison hurts us too
        // craft Poison Remedies proactively to survive
        private static bool CraftVsPoisonTribal(Hero actor)
        {
            if (!IsPoisonTribalLike())         return false;
            if (Essence() < EssenceCostTier1)  return false;

            if (!AnyAllyHasItem(Ability.PoisonRemedy) &&
                SelfCast(actor, Ability.CraftPoisonRemedy)) return true;

            // Craft Elixir immediately so we have something to heal the Wizard with
            if (Essence() >= EssenceCostTier2 &&
                !AnyAllyHasItem(Ability.Elixir) &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                SelfCast(actor, Ability.CraftElixir)) return true;

            return false;
        }

        private static bool CleansePetrifyIfNoRemedy(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);

            if (petrified == null)                     return false;
            if (AnyAllyHasItem(Ability.PetrifyRemedy)) return false;
            if (AnyAllyHasItem(Ability.FullRemedy))    return false;

            return Act(actor, Ability.Cleanse, petrified);
        }

        private static bool CleanseDoomIfNoRemedy(Hero actor)
        {
            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);

            if (doomed == null)                     return false;
            if (AnyAllyHasItem(Ability.FullRemedy)) return false;

            return Act(actor, Ability.Cleanse, doomed);
        }

        private static bool CraftNeededRemedy(Hero actor)
        {
            if (Essence() < EssenceCostTier1) return false;

            if (FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying) != null &&
                !AnyAllyHasItem(Ability.PetrifyRemedy) &&
                SelfCast(actor, Ability.CraftPetrifyRemedy)) return true;

            if (FindAllyWithStatus(StatusEffect.Doom) != null &&
                !AnyAllyHasItem(Ability.FullRemedy) &&
                SelfCast(actor, Ability.CraftFullRemedy)) return true;

            if (FindAllyWithStatus(StatusEffect.Silence) != null &&
                !AnyAllyHasItem(Ability.SilenceRemedy) &&
                SelfCast(actor, Ability.CraftSilenceRemedy)) return true;

            return false;
        }

        private static bool CraftSupportItems(Hero actor)
        {
            if (Essence() >= EssenceCostTier1 &&
                !AnyAllyHasItem(Ability.Ether) &&
                SelfCast(actor, Ability.CraftEther)) return true;

            if (Essence() >= EssenceCostTier2 &&
                !AnyAllyHasItem(Ability.Elixir) &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                SelfCast(actor, Ability.CraftElixir)) return true;

            if (Essence() >= EssenceCostTier3 &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                AnyAllyHasItem(Ability.Elixir) &&
                SelfCast(actor, Ability.CraftMegaElixir)) return true;

            return false;
        }

        private static bool ReviveOrCraftRevive(Hero actor)
        {
            Hero dead = BestDeadAlly();

            if (dead == null) return false;

            if (Essence() >= EssenceCostTier1 &&
                !AnyAllyHasItem(Ability.Revive) &&
                SelfCast(actor, Ability.CraftRevive)) return true;

            return Act(actor, Ability.Revive, dead);
        }

        // ============================================================
        // ITEMS
        // ============================================================

        private static bool UseEmergencyItem(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
            if (petrified != null && Act(actor, Ability.PetrifyRemedy, petrified)) return true;
            if (petrified != null && Act(actor, Ability.FullRemedy,    petrified)) return true;

            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            if (doomed != null && Act(actor, Ability.FullRemedy, doomed)) return true;

            // A silenced Wizard cannot cast PoisonNova, worth spending a remedy immediately
            if (RemoveSilenceFromImportantCaster(actor)) return true;

            if (CountBelow(HpLow) >= 2 &&
                SelfCast(actor, Ability.MegaElixir)) return true;

            Hero dead = BestDeadAlly();
            if (dead != null && Act(actor, Ability.Revive, dead)) return true;

            Hero lowest = LowestAlly();
            if (lowest != null && HpRatio(lowest) <= HpCritical)
            {
                if (Act(actor, Ability.Elixir, lowest)) return true;
                if (Act(actor, Ability.Potion, lowest)) return true;
            }

            return false;
        }

        private static bool RemoveSilenceFromImportantCaster(Hero actor)
        {
            foreach (HeroJobClass jobClass in CleanseOrder)
            {
                Hero ally = FindLivingAlly(jobClass);

                if (ally == null || !HasStatus(ally, StatusEffect.Silence))
                    continue;

                if (Act(actor, Ability.SilenceRemedy, ally)) return true;
                if (Act(actor, Ability.FullRemedy,    ally)) return true;
            }

            return false;
        }

        private static bool UseEther(Hero actor, float threshold)
        {
            Hero target    = null;
            float lowestMp = threshold + 0.001f;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float mp = MpRatio(ally);

                if (mp >= lowestMp) continue;

                lowestMp = mp;
                target   = ally;
            }

            return target != null && Act(actor, Ability.Ether, target);
        }

        // ============================================================
        // ATTACK
        // ============================================================

        private static bool SlowAllTargets(Hero actor)
        {
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Cleric)))    return true;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Alchemist))) return true;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Monk)))      return true;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Fighter)))   return true;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Wizard)))    return true;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Rogue)))     return true;

            return false;
        }

        private static bool FinishPhysicalTarget(Hero actor)
        {
            Hero target = BestAttackTarget();

            return target != null &&
                   HpRatio(target) <= FinishHp &&
                   Act(actor, Ability.Attack, target);
        }

        private static bool FinishMagicTarget(Hero actor)
        {
            Hero target = BestMagicTarget();

            return target != null &&
                   HpRatio(target) <= FinishHp &&
                   Act(actor, Ability.MagicMissile, target);
        }

        private static bool DispelEnemyAutoLife(Hero actor, Ability dispelAbility)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (HasStatus(foe, StatusEffect.AutoLife) &&
                    Act(actor, dispelAbility, foe)) return true;
            }

            return false;
        }

        private static Hero BestAttackTarget()
        {
            return BestTarget(Ability.Attack, ignoreCover: true);
        }

        private static Hero BestMagicTarget()
        {
            return BestTarget(Ability.MagicMissile, ignoreCover: false);
        }

        // Finds the highest priority living foe that can be targeted with the given ability
        private static Hero BestTarget(Ability ability, bool ignoreCover)
        {
            foreach (HeroJobClass jobClass in KillOrder)
            {
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                {
                    if (foe.jobClass == jobClass && Legal(ability, foe))
                        return foe;
                }
            }

            if (!ignoreCover) return null;

            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (LegalIgnoreCover(ability, foe)) return foe;

            return null;
        }

        // ============================================================
        // TARGET FINDERS
        // ============================================================

        private static Hero FindUndoomed(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Doom, Ability.Doom, ignoreCover: false);
        }

        private static Hero FindUnpetrified(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass != jobClass)                continue;
                if (HasStatus(foe, StatusEffect.Petrified))  continue;
                if (HasStatus(foe, StatusEffect.Petrifying)) continue;
                if (Legal(Ability.Petrify, foe))             return foe;
            }

            return null;
        }

        private static Hero FindUnslowed(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Slow, Ability.Slow, ignoreCover: false);
        }

        private static Hero FindWithoutDebrave(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Debrave, Ability.Debrave, ignoreCover: false);
        }

        private static Hero FindWithoutDefaith(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Defaith, Ability.Defaith, ignoreCover: false);
        }

        // Shared logic for finding a foe of a given class that does not have a given status
        private static Hero FindFoeWithout(
            HeroJobClass jobClass,
            StatusEffect status,
            Ability ability,
            bool ignoreCover)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass != jobClass) continue;
                if (HasStatus(foe, status))   continue;

                bool canTarget = ignoreCover
                    ? LegalIgnoreCover(ability, foe)
                    : Legal(ability, foe);

                if (canTarget) return foe;
            }

            return null;
        }

        private static Hero FindAllyWithStatus(params StatusEffect[] statuses)
        {
            foreach (HeroJobClass jobClass in CleanseOrder)
            {
                Hero ally = FindLivingAlly(jobClass);

                if (ally != null && HasAnyStatus(ally, statuses))
                    return ally;
            }

            return null;
        }

        private static Hero LowestAlly()
        {
            Hero lowestAlly = null;
            float lowestHp  = float.MaxValue;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float hp = HpRatio(ally);

                if (hp >= lowestHp) continue;

                lowestHp   = hp;
                lowestAlly = ally;
            }

            return lowestAlly;
        }

        private static Hero BestDeadAlly()
        {
            foreach (HeroJobClass jobClass in ReviveOrder)
            {
                Hero ally = FindDeadAlly(jobClass);
                if (ally != null) return ally;
            }

            return null;
        }

        private static Hero FindDeadAlly(HeroJobClass jobClass)
        {
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.jobClass == jobClass && ally.health <= 0)
                    return ally;
            }

            return null;
        }

        private static Hero FindLivingAlly(HeroJobClass jobClass)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (ally.jobClass == jobClass) return ally;
            }

            return null;
        }

        private static Hero FindLivingFoe(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass == jobClass) return foe;
            }

            return null;
        }

        // ============================================================
        // SITUATION DETECTION
        // ============================================================

        private static bool AllEnemiesPoisoned()
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (!HasStatus(foe, StatusEffect.Poison)) return false;
            }

            return true;
        }

        // Alchemist + 2 Rogues, no Monks, no Fighters, no Wizard, Rogues steal items
        private static bool IsItemCrafterLike()
        {
            return CountEnemyClass(HeroJobClass.Rogue)   >= 1 &&
                   CountEnemyClass(HeroJobClass.Monk)    == 0 &&
                   CountEnemyClass(HeroJobClass.Fighter) == 0 &&
                   CountEnemyClass(HeroJobClass.Wizard)  == 0;
        }

        // 2+ Monks + Alchemist, Alchemist crafts infinite revives and heals
        private static bool IsLmtBrkCrafterLike()
        {
            return CountEnemyClass(HeroJobClass.Monk)    >= 2 &&
                   FindLivingFoe(HeroJobClass.Alchemist) != null;
        }

        // 3 Fighters with Brave, burst them before stacks get out of hand
        private static bool IsBraveAdvanceLike()
        {
            return CountEnemyClass(HeroJobClass.Fighter) >= 3;
        }

        // 3+ Monks + Wizard, Monks have Bitter Bloom which amplifies damage on poisoned targets
        private static bool IsPoisonTribalLike()
        {
            return CountEnemyClass(HeroJobClass.Monk)   >= 3 &&
                   CountEnemyClass(HeroJobClass.Wizard) >= 1;
        }

        // 2+ enemy Wizards, they clear poison with Full Remedies faster than we can apply it
        private static bool IsMultiWizardLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard) >= 2;
        }

        private static bool TeamIsStable()
        {
            if (CountBelow(HpLow) > 0) return false;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (HasAnyStatus(ally, DangerousDebuffs)) return false;
            }

            return true;
        }

        private static bool AnyAllyHasItem(Ability ability)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (Utility.AreAbilityAndTargetLegal(ability, ally, false)) return true;
            }

            return false;
        }

        private static int CountBelow(float hpThreshold)
        {
            int count = 0;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (HpRatio(ally) <= hpThreshold) count++;
            }

            return count;
        }

        private static int CountUnpoisonedFoes()
        {
            int count = 0;

            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (!HasStatus(foe, StatusEffect.Poison)) count++;
            }

            return count;
        }

        private static int CountEnemyClass(HeroJobClass jobClass)
        {
            int count = 0;

            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass == jobClass) count++;
            }

            return count;
        }

        private static int Essence()
        {
            return TeamHeroCoder.BattleState.allyEssenceCount;
        }

        // ============================================================
        // CORE HELPERS
        // ============================================================

        private static bool Act(Hero actor, Ability ability, Hero target)
        {
            if (actor == null || target == null)                           return false;
            if (!Utility.AreAbilityAndTargetLegal(ability, target, false)) return false;

            Console.WriteLine($"{actor.jobClass} uses {ability} on {target.jobClass}");
            TeamHeroCoder.PerformHeroAbility(ability, target);
            return true;
        }

        private static bool SelfCast(Hero actor, Ability ability)
        {
            if (actor == null)                                             return false;
            if (!Utility.AreAbilityAndTargetLegal(ability, actor, false)) return false;

            Console.WriteLine($"{actor.jobClass} uses {ability}");
            TeamHeroCoder.PerformHeroAbility(ability, actor);
            return true;
        }

        private static bool Legal(Ability ability, Hero target)
        {
            return target != null &&
                   Utility.AreAbilityAndTargetLegal(ability, target, false);
        }

        private static bool LegalIgnoreCover(Ability ability, Hero target)
        {
            return target != null &&
                   Utility.AreAbilityAndTargetLegal(ability, target, true);
        }

        // Guaranteed fallback, returns any living foe regardless of cover or priority
        private static Hero FirstLivingFoe()
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                return foe;

            return null;
        }

        private static void Wait(Hero actor)
        {
            Console.WriteLine($"{actor.jobClass} waits");
            TeamHeroCoder.PerformHeroAbility(Ability.Wait, actor);
        }

        private static IEnumerable<Hero> Living(IEnumerable<Hero> heroes)
        {
            foreach (Hero hero in heroes)
            {
                if (hero.health > 0) yield return hero;
            }
        }

        private static float HpRatio(Hero hero)
        {
            if (hero == null || hero.maxHealth <= 0) return 1f;

            return (float)hero.health / hero.maxHealth;
        }

        private static float MpRatio(Hero hero)
        {
            if (hero == null || hero.maxMana <= 0) return 1f;

            return (float)hero.mana / hero.maxMana;
        }

        private static bool HasStatus(Hero hero, StatusEffect status)
        {
            if (hero == null) return false;

            foreach (StatusEffectAndDuration effect in hero.statusEffectsAndDurations)
            {
                if (effect.statusEffect == status) return true;
            }

            return false;
        }

        private static bool HasAnyStatus(Hero hero, params StatusEffect[] statuses)
        {
            foreach (StatusEffect status in statuses)
            {
                if (HasStatus(hero, status)) return true;
            }

            return false;
        }
    }
}