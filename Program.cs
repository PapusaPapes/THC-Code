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

    // Team: Fighter / Monk / Alchemist
    // Items: 2 Ether, 42 Essence
    // Strategy:
    // Fighter stacks Brave and uses Resurrection to support the team.
    // Monk stacks Brave and uses FlurryOfBlows as the primary damage dealer.
    // Adrenaline passive gives Monk 50% more damage below 51% HP.
    // Alchemist Slows enemies, Hastes Monk, and crafts sustain reactively.

    public static class MyAI
    {
        public static string FolderExchangePath =
            "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // Health thresholds
        private const float HpCritical   = 0.20f;  // Monk dies below this must heal
        private const float HpLow        = 0.55f;
        private const float HpLight      = 0.75f;

        // Mana thresholds
        private const float MpLow = 0.25f;

        // Combat thresholds
        private const float FinishHp = 0.35f;

        // Alchemist essence costs
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

        // Revive order, Alchemist first since it's the sustain engine
        private static readonly HeroJobClass[] ReviveOrder =
        {
            HeroJobClass.Alchemist,
            HeroJobClass.Monk,
            HeroJobClass.Fighter
        };

        // Cleanse order
        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Alchemist,
            HeroJobClass.Monk,
            HeroJobClass.Fighter
        };

        // Only truly lethal debuffs block buffing
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
                case HeroJobClass.Fighter:
                    ControlFighter(actor);
                    return;

                case HeroJobClass.Monk:
                    ControlMonk(actor);
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
        // FIGHTER
        // ============================================================

        private static void ControlFighter(Hero actor)
        {
            // Trinity Doom: use FullRemedy on self if doomed, otherwise just attack
            if (IsTrinityDoomLike())
            {
                if (ResurrectDeadAlly(actor)) return;

                if (HasStatus(actor, StatusEffect.Doom) &&
                    Act(actor, Ability.FullRemedy, actor)) return;

                if (FinishPhysicalTarget(actor))                    return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                if (Act(actor, Ability.Attack, FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            if (UseEmergencyItem(actor)) return;
            if (ResurrectDeadAlly(actor)) return;

            // Lmt Brk Crafter: focus Alchemist every turn, drain their 6 Revives
            if (IsLmtBrkCrafterLike())
            {
                if (ApplyBrave(actor)) return;
                Hero alchemist = FindLivingFoe(HeroJobClass.Alchemist);
                if (alchemist != null)
                {
                    if (FinishPhysicalTarget(actor))             return;
                    if (Act(actor, Ability.Attack, alchemist))   return;
                }
            }

            // Meteor Rush: kill Fighter first
            if (IsMeteorRushLike())
            {
                if (ApplyBrave(actor)) return;
                Hero fighter = FindLivingFoe(HeroJobClass.Fighter);
                if (fighter != null)
                {
                    if (FinishPhysicalTarget(actor))             return;
                    if (Act(actor, Ability.Attack, fighter))     return;
                }
            }

            // Ctrl & Sustain: focus Alchemist, skip Brave stacking, attack every turn
            if (IsCtrlAndSustainLike())
            {
                Hero alchemist = FindLivingFoe(HeroJobClass.Alchemist);
                if (alchemist != null)
                {
                    if (FinishPhysicalTarget(actor))               return;
                    if (Act(actor, Ability.Attack, alchemist))     return;
                }

                if (FinishPhysicalTarget(actor))                      return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))   return;
                if (Act(actor, Ability.Attack, FirstLivingFoe()))     return;

                Wait(actor);
                return;
            }

            if (ApplyBrave(actor)) return;

            if (FinishPhysicalTarget(actor))                      return;

            Hero finishing = BestAttackTarget();
            if (finishing != null && HpRatio(finishing) <= 0.60f &&
                Act(actor, Ability.QuickHit, finishing))          return;

            if (Act(actor, Ability.Attack,   BestAttackTarget())) return;
            if (Act(actor, Ability.Attack,   FirstLivingFoe()))   return;

            Wait(actor);
        }

        private static bool ApplyBrave(Hero actor)
        {
            if (HasStatus(actor, StatusEffect.Brave)) return false;

            return Act(actor, Ability.Brave, actor);
        }

        private static bool ResurrectDeadAlly(Hero actor)
        {
            foreach (HeroJobClass jobClass in ReviveOrder)
            {
                Hero dead = FindDeadAlly(jobClass);
                if (dead != null && Act(actor, Ability.Resurrection, dead)) return true;
            }

            return false;
        }

        // ============================================================
        // MONK
        // ============================================================

        private static void ControlMonk(Hero actor)
        {
            // Trinity Doom: clear own Doom, then attack, Fighter handles its own Doom
            if (IsTrinityDoomLike())
            {
                if (HasStatus(actor, StatusEffect.Doom))
                {
                    if (Act(actor, Ability.FullRemedy, actor)) return;
                    if (Act(actor, Ability.Cleanse,    actor)) return;
                }

                if (Act(actor, Ability.FlurryOfBlows, BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,         BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,         FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            if (UseEmergencyItem(actor)) return;

            // Only Brave when stable, don't waste tempo on setup turns under pressure
            if (TeamIsStable() && ApplyBrave(actor)) return;

            // Lmt Brk Crafter: focus Alchemist every turn, drain their 6 Revives
            if (IsLmtBrkCrafterLike())
            {
                Hero alchemist = FindLivingFoe(HeroJobClass.Alchemist);
                if (alchemist != null)
                {
                    if (Act(actor, Ability.FlurryOfBlows, alchemist)) return;
                    if (Act(actor, Ability.Attack,        alchemist)) return;
                }
            }

            // Meteor Rush: kill Fighter first, it keeps resurrecting Wizards
            if (IsMeteorRushLike())
            {
                Hero fighter = FindLivingFoe(HeroJobClass.Fighter);
                if (fighter != null)
                {
                    if (Act(actor, Ability.FlurryOfBlows, fighter)) return;
                    if (Act(actor, Ability.Attack,        fighter)) return;
                }
            }

            // Ctrl & Sustain: focus Alchemist, drain revives, Monk is just damage
            if (IsCtrlAndSustainLike())
            {
                if (UseEmergencyItem(actor)) return;

                // Debrave the enemy Monk to reduce kill pressure
                if (Act(actor, Ability.Debrave, FindWithoutDebrave(HeroJobClass.Monk))) return;

                Hero alchemist = FindLivingFoe(HeroJobClass.Alchemist);
                if (alchemist != null)
                {
                    if (Act(actor, Ability.FlurryOfBlows, alchemist)) return;
                    if (Act(actor, Ability.Attack,        alchemist)) return;
                }

                if (Act(actor, Ability.FlurryOfBlows, BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,         BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,         FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            // FlurryOfBlows is the primary win condition, always prioritize it
            if (Act(actor, Ability.FlurryOfBlows, BestAttackTarget())) return;

            if (DebraveThreats(actor)) return;
            if (DefaithThreats(actor)) return;

            if (FinishPhysicalTarget(actor))                    return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;
            if (Act(actor, Ability.Attack, FirstLivingFoe()))   return;

            Wait(actor);
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

        // ============================================================
        // ALCHEMIST
        // ============================================================

        private static void ControlAlchemist(Hero actor)
        {
            // first, dead Alchemist means no more crafting
            if (HpRatio(actor) <= HpLight && HealCriticalAlly(actor)) return;

            // Ctrl & Sustain: 2 Alchemists + Monk, craft MegaElixir and use it to survive
            if (IsCtrlAndSustainLike())
            {
                // Use MegaElixir when Alchemist is below HpLight, don't wait until critical
                if (HpRatio(actor) <= HpLight &&
                    SelfCast(actor, Ability.MegaElixir))                     return;
                if (HealCriticalAlly(actor))                                  return;
                if (UseEmergencyItem(actor))                                  return;
                if (UseEther(actor, MpLow))                                   return;

                // Cleanse Debrave off allies to keep damage up
                Hero monk = FindLivingAlly(HeroJobClass.Monk);
                if (monk != null && HasStatus(monk, StatusEffect.Debrave) &&
                    Act(actor, Ability.Cleanse, monk))                        return;

                Hero fighter = FindLivingAlly(HeroJobClass.Fighter);
                if (fighter != null && HasStatus(fighter, StatusEffect.Debrave) &&
                    Act(actor, Ability.Cleanse, fighter))                     return;

                // Craft MegaElixir, use Essence instead of sitting on it
                if (Essence() >= EssenceCostTier3 &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftMegaElixir))                return;

                // Craft Elixir as backup
                if (Essence() >= EssenceCostTier2 &&
                    !AnyAllyHasItem(Ability.Elixir) &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftElixir))                    return;

                // Slow enemy Monk once to reduce kill pressure
                if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Monk))) return;

                if (ReviveOrCraftRevive(actor))                               return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))           return;

                Wait(actor);
                return;
            }

            // Trinity Doom: survive until Wizard runs out of mana, must come before cleanse calls
            if (IsTrinityDoomLike())
            {
                if (UseEther(actor, MpLow)) return;

                // Haste self only after we have a FullRemedy stocked
                if (!HasStatus(actor, StatusEffect.Haste) &&
                    AnyAllyHasItem(Ability.FullRemedy) &&
                    Act(actor, Ability.Haste, actor)) return;

                // If anyone is critical, use MegaElixir immediately
                if (CountBelow(HpCritical) >= 1 &&
                    SelfCast(actor, Ability.MegaElixir)) return;

                // If no MegaElixir and team is getting low, craft one
                if (CountBelow(HpLow) >= 1 &&
                    Essence() >= EssenceCostTier3 &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftMegaElixir)) return;

                // Keep crafting FullRemedies, Doom kills without them
                if (Essence() >= EssenceCostTier1 &&
                    !AnyAllyHasItem(Ability.FullRemedy) &&
                    SelfCast(actor, Ability.CraftFullRemedy)) return;

                // No FullRemedy available, Cleanse doomed allies only if healthy enough
                if (!AnyAllyHasItem(Ability.FullRemedy) && HpRatio(actor) >= 0.80f)
                {
                    Hero doomedFighter = FindLivingAlly(HeroJobClass.Fighter);
                    if (doomedFighter != null && HasStatus(doomedFighter, StatusEffect.Doom) &&
                        Act(actor, Ability.Cleanse, doomedFighter)) return;

                    Hero doomedMonk = FindLivingAlly(HeroJobClass.Monk);
                    if (doomedMonk != null && HasStatus(doomedMonk, StatusEffect.Doom) &&
                        Act(actor, Ability.Cleanse, doomedMonk)) return;

                    if (HasStatus(actor, StatusEffect.Doom) &&
                        Act(actor, Ability.Cleanse, actor)) return;
                }

                // Craft MegaElixir proactively when healthy
                if (Essence() >= EssenceCostTier3 &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftMegaElixir)) return;

                if (ReviveOrCraftRevive(actor)) return;

                Wait(actor);
                return;
            }

            // Meteor Rush / Poison Tribal: outlast enemy mana with MegaElixir cycling
            if (IsMeteorRushLike() || IsPoisonTribalLike())
            {
                // Only use MegaElixir when someone is actually critical
                if (CountBelow(HpCritical) >= 1 &&
                    SelfCast(actor, Ability.MegaElixir))              return;
                if (UseEther(actor, MpLow))                           return;
                if (Essence() >= EssenceCostTier3 &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftMegaElixir))         return;
                if (Essence() >= EssenceCostTier2 &&
                    !AnyAllyHasItem(Ability.Elixir) &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftElixir))             return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))   return;

                Wait(actor);
                return;
            }

            if (CleansePetrifyIfNoRemedy(actor)) return;
            if (CleanseDoomIfNoRemedy(actor))    return;

            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (CraftNeededRemedy(actor))   return;
            if (ReviveOrCraftRevive(actor)) return;
            if (CraftSupportItems(actor))   return;

            // 2+ Wizards means Petrify spam, craft remedies proactively once we have healing
            if (CraftVsMultipleWizards(actor)) return;

            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            // Haste the Monk to maximize FlurryOfBlows turns
            if (HasteMonk(actor)) return;

            if (actor.mana >= MinManaToSlow && SlowAllTargets(actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        // Haste the Monk to give it more FlurryOfBlows turns
        private static bool HasteMonk(Hero actor)
        {
            if (!TeamIsStable()) return false;

            Hero monk = FindLivingAlly(HeroJobClass.Monk);

            if (monk == null)                         return false;
            if (HasStatus(monk, StatusEffect.Haste))  return false;

            return Act(actor, Ability.Haste, monk);
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

        // 2 Alchemists + Monk, both Alchemists revive each other endlessly
        private static bool IsCtrlAndSustainLike()
        {
            return CountEnemyClass(HeroJobClass.Alchemist) >= 2;
        }

        // Fighter + Cleric + Wizard, Wizard Dooms, Cleric sustains, Fighter tanks
        private static bool IsTrinityDoomLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard)  >= 1 &&
                   CountEnemyClass(HeroJobClass.Cleric)  >= 1 &&
                   CountEnemyClass(HeroJobClass.Fighter) >= 1 &&
                   CountEnemyClass(HeroJobClass.Monk)    == 0;
        }

        // 2+ enemy Wizards, Petrify spam, craft Petrify and Full Remedies proactively
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
            // When team is taking heavy damage craft MegaElixir immediately
            if (CountBelow(HpLow) >= 2 &&
                Essence() >= EssenceCostTier3 &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                SelfCast(actor, Ability.CraftMegaElixir)) return true;

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

        // MegaElixir fully heals everyone, use when anyone is critical
        private static bool HealCriticalAlly(Hero actor)
        {
            if (CountBelow(HpCritical) > 0 &&
                SelfCast(actor, Ability.MegaElixir)) return true;

            // Self-heal with Elixir/Potion when low
            if (HpRatio(actor) <= HpLow)
            {
                if (Act(actor, Ability.Elixir, actor)) return true;
                if (Act(actor, Ability.Potion, actor)) return true;
            }

            // Heal lowest ally when critical
            Hero lowest = LowestAlly();
            if (lowest != null && HpRatio(lowest) <= HpCritical &&
                lowest.jobClass != HeroJobClass.Monk)
            {
                if (Act(actor, Ability.Elixir, lowest)) return true;
                if (Act(actor, Ability.Potion, lowest)) return true;
            }

            return false;
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

            // Alchemist is the sustain engine, save it with MegaElixir if it's critical
            Hero alchemist = FindLivingAlly(HeroJobClass.Alchemist);
            if (alchemist != null && HpRatio(alchemist) <= HpCritical &&
                SelfCast(actor, Ability.MegaElixir)) return true;

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

        private static Hero FindWithoutDebrave(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Debrave, Ability.Debrave, ignoreCover: false);
        }

        private static Hero FindWithoutDefaith(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Defaith, Ability.Defaith, ignoreCover: false);
        }

        private static Hero FindUnslowed(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Slow, Ability.Slow, ignoreCover: false);
        }

        // for finding a foe of a given class that does not have a given status
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

        // 2 Monks + Alchemist, drain their Revive items by killing Alchemist repeatedly
        private static bool IsLmtBrkCrafterLike()
        {
            return CountEnemyClass(HeroJobClass.Monk)      >= 2 &&
                   CountEnemyClass(HeroJobClass.Alchemist) >= 1;
        }

        // 3 Monks + Wizard, Wizard Dooms us, Monks hit incredibly hard
        private static bool IsPoisonTribalLike()
        {
            return CountEnemyClass(HeroJobClass.Monk)   >= 3 &&
                   CountEnemyClass(HeroJobClass.Wizard) >= 1;
        }

        // 2 Wizards + Fighter, no Rogue, Fighter keeps resurrecting Wizards
        private static bool IsMeteorRushLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard)  >= 2 &&
                   CountEnemyClass(HeroJobClass.Fighter) >= 1 &&
                   CountEnemyClass(HeroJobClass.Rogue)   == 0;
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