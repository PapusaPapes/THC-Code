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

    // Team: Fighter / Wizard / Alchemist
    // Items: 2 Ether, 4 Full Remedy, 37 Essence
    // Strategy:
    // Fighter stacks Brave and uses Resurrection to revive fallen allies.
    // Wizard PoisonNovas then Dooms all targets on a timer.
    // Alchemist crafts Ethers, MegaElixirs, and remedies to sustain the team.

    public static class MyAI
    {
        public static string FolderExchangePath =
            "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // Health thresholds
        private const float HpCritical = 0.30f;
        private const float HpLow      = 0.55f;
        private const float HpLight    = 0.75f;

        // Mana thresholds
        private const float MpLow = 0.25f;

        // Combat thresholds
        private const float FinishHp = 0.35f;

        // Alchemist essence costs
        private const int EssenceCostTier1 = 2;
        private const int EssenceCostTier2 = 3;
        private const int EssenceCostTier3 = 4;
        private const int MinManaToSlow    = 15;

        private static readonly HeroJobClass[] KillOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Wizard,
            HeroJobClass.Rogue,
            HeroJobClass.Monk,
            HeroJobClass.Fighter
        };

        private static readonly HeroJobClass[] ReviveOrder =
        {
            HeroJobClass.Alchemist,
            HeroJobClass.Wizard,
            HeroJobClass.Fighter
        };

        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Wizard,
            HeroJobClass.Alchemist,
            HeroJobClass.Fighter
        };

        // Cycle: craft 5 FullRemedies then 1 MegaElixir in Trinity Doom
        private static int trinityDoomCycleStep = 0;

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
        // FIGHTER
        // ============================================================

        private static void ControlFighter(Hero actor)
        {
            if (UseEmergencyItem(actor))  return;

            // Trinity Doom: cleanse urgent Doom, heal Alchemist, focus Cleric
            if (IsTrinityDoomLike())
            {
                if (ResurrectDeadAlly(actor))  return;
                // Cleanse urgent Doom if Alchemist can't get there in time
                Hero urgentD = MostUrgentDoomedAlly();
                if (urgentD != null && GetDoomDuration(urgentD) <= 1 &&
                    Act(actor, Ability.FullRemedy, urgentD)) return;
                Hero alch = FindLivingAlly(HeroJobClass.Alchemist);
                if (alch != null && HpRatio(alch) <= HpLow &&
                    Act(actor, Ability.CureSerious, alch)) return;
                if (FinishPhysicalTarget(actor))                                    return;
                if (Act(actor, Ability.Attack, FindLivingFoe(HeroJobClass.Cleric))) return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))                 return;
                Wait(actor);
                return;
            }

            if (ResurrectDeadAlly(actor)) return;

            // Meteor Rush: kill enemy Fighter to stop Resurrections
            if (IsMeteorRushLike())
            {
                if (ApplyBrave(actor)) return;
                Hero fighter = FindLivingFoe(HeroJobClass.Fighter);
                if (FinishPhysicalTarget(actor))                              return;
                if (fighter != null && Act(actor, Ability.QuickHit, fighter)) return;
                if (fighter != null && Act(actor, Ability.Attack,   fighter)) return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))           return;
                Wait(actor);
                return;
            }

            if (ApplyBrave(actor)) return;

            if (FinishPhysicalTarget(actor))                      return;

            Hero target = BestAttackTarget();
            if (target != null && HpRatio(target) <= 0.60f &&
                Act(actor, Ability.QuickHit, target))             return;

            if (Act(actor, Ability.Attack, BestAttackTarget()))   return;
            if (Act(actor, Ability.Attack, FirstLivingFoe()))     return;

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
        // WIZARD
        // ============================================================

        private static void ControlWizard(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (!IsTrinityDoomLike() && DispelEnemyAutoLife(actor, Ability.QuickDispel)) return;

            // Trinity Doom: cleanse urgent Doom, Meteor pressure, Doom Cleric and Fighter
            if (IsTrinityDoomLike())
            {
                // Help with urgent Doom cleanse before casting
                Hero urgentD = MostUrgentDoomedAlly();
                if (urgentD != null && GetDoomDuration(urgentD) <= 1 &&
                    Act(actor, Ability.FullRemedy, urgentD)) return;
                // PoisonNova for pressure instead of Doom, cheaper and poisons all
                if (CountUnpoisonedFoes() > 0 &&
                    Act(actor, Ability.PoisonNova, BestMagicTarget()))            return;
                if (Act(actor, Ability.Meteor, BestMagicTarget()))                return;
                if (FinishMagicTarget(actor))                                     return;
                if (Act(actor, Ability.MagicMissile, FindLivingFoe(HeroJobClass.Cleric))) return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget()))          return;
                if (Act(actor, Ability.Attack,        BestAttackTarget()))                return;
                Wait(actor);
                return;
            }

            // Meteor Rush: Doom enemy Fighter to stop Resurrections
            if (IsMeteorRushLike())
            {
                if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Fighter))) return;
                if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Wizard)))  return;
                if (FinishMagicTarget(actor))                                     return;
                if (Act(actor, Ability.MagicMissile, FindLivingFoe(HeroJobClass.Fighter))) return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget()))          return;
                if (Act(actor, Ability.Attack,        BestAttackTarget()))        return;
                Wait(actor);
                return;
            }

            // PoisonNova first, poisons all enemies
            if (CountUnpoisonedFoes() > 0 &&
                Act(actor, Ability.PoisonNova, BestMagicTarget())) return;

            if (DoomAllTargets(actor))  return;
            if (PetrifyThreats(actor))  return;
            if (SlowAllTargets(actor))  return;

            if (FinishMagicTarget(actor))                             return;
            if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
            if (Act(actor, Ability.Attack,        BestAttackTarget())) return;
            if (Act(actor, Ability.Attack,        FirstLivingFoe()))   return;

            Wait(actor);
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

        private static bool FinishMagicTarget(Hero actor)
        {
            Hero target = BestMagicTarget();
            return target != null &&
                   HpRatio(target) <= FinishHp &&
                   Act(actor, Ability.MagicMissile, target);
        }

        // ============================================================
        // ALCHEMIST
        // ============================================================

        private static void ControlAlchemist(Hero actor)
        {
            if (!IsTrinityDoomLike() && HpRatio(actor) <= HpLight && HealCriticalAlly(actor)) return;

            // Trinity Doom: reactive crafting, 1 FullRemedy always available, MegaElixir for swing
            if (IsTrinityDoomLike())
            {
                // Heal self if critical, use MegaElixir since we have no Elixirs
                if (HpRatio(actor) <= HpCritical)
                {
                    if (SelfCast(actor, Ability.MegaElixir))       return;
                    if (Act(actor, Ability.Elixir, actor))          return;
                    if (Act(actor, Ability.Potion, actor))          return;
                }
                // Ether Fighter so it can CureSerious and Resurrect
                Hero ourFighter = FindLivingAlly(HeroJobClass.Fighter);
                if (ourFighter != null && MpRatio(ourFighter) <= 0.35f &&
                    Act(actor, Ability.Ether, ourFighter)) return;
                // Cleanse own Doom immediately, Alchemist must stay alive
                if (HasStatus(actor, StatusEffect.Doom) &&
                    Act(actor, Ability.FullRemedy, actor)) return;
                // Cycle: 5 FullRemedies then 1 MegaElixir
                if (trinityDoomCycleStep < 4)
                {
                    if (Essence() >= EssenceCostTier1 &&
                        SelfCast(actor, Ability.CraftFullRemedy))
                    {
                        trinityDoomCycleStep++;
                        return;
                    }
                }
                else if (trinityDoomCycleStep == 4)
                {
                    if (Essence() >= EssenceCostTier3 &&
                        SelfCast(actor, Ability.CraftMegaElixir))
                    {
                        trinityDoomCycleStep++;
                        return;
                    }
                }
                else
                {
                    // Use MegaElixir then reset cycle
                    if (SelfCast(actor, Ability.MegaElixir))
                    {
                        trinityDoomCycleStep = 0;
                        return;
                    }
                }
                // Cleanse ally with lowest Doom duration
                Hero urgentDoomed = MostUrgentDoomedAlly();
                if (urgentDoomed != null &&
                    Act(actor, Ability.FullRemedy, urgentDoomed)) return;
                // Revive if needed
                if (ReviveOrCraftRevive(actor)) return;
                // Ether self if low mana
                if (UseEther(actor, MpLow)) return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;
            if (CleanseDoomIfNoRemedy(actor)) return;

            // Petrification: always have PetrifyRemedies stocked
            if (IsPetrificationLike())
            {
                if (Essence() >= EssenceCostTier1 &&
                    !AnyAllyHasItem(Ability.PetrifyRemedy) &&
                    SelfCast(actor, Ability.CraftPetrifyRemedy)) return;
            }

            if (CraftNeededRemedy(actor))   return;
            if (ReviveOrCraftRevive(actor)) return;
            if (CraftSupportItems(actor))   return;

            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            if (actor.mana >= MinManaToSlow && SlowAllTargets(actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
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

            return false;
        }

        private static bool CraftSupportItems(Hero actor)
        {
            // Craft MegaElixir first, heals whole team, not just one ally
            if (Essence() >= EssenceCostTier3 &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                SelfCast(actor, Ability.CraftMegaElixir)) return true;

            if (Essence() >= EssenceCostTier1 &&
                !AnyAllyHasItem(Ability.Ether) &&
                SelfCast(actor, Ability.CraftEther)) return true;

            if (Essence() >= EssenceCostTier2 &&
                !AnyAllyHasItem(Ability.Elixir) &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                SelfCast(actor, Ability.CraftElixir)) return true;

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

        private static bool HealCriticalAlly(Hero actor)
        {
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

        // ============================================================
        // ITEMS
        // ============================================================

        private static bool UseEmergencyItem(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
            if (petrified != null && Act(actor, Ability.PetrifyRemedy, petrified)) return true;
            if (petrified != null && Act(actor, Ability.FullRemedy,    petrified)) return true;

            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            // Always cleanse Alchemist Doom, it's our crafting engine
            if (doomed != null && doomed.jobClass == HeroJobClass.Alchemist &&
                Act(actor, Ability.FullRemedy, doomed)) return true;
            if (!IsTrinityDoomLike() && doomed != null && Act(actor, Ability.FullRemedy, doomed)) return true;

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
            foreach (HeroJobClass jobClass in KillOrder)
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                    if (foe.jobClass == jobClass && Legal(Ability.Attack, foe)) return foe;

            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (LegalIgnoreCover(Ability.Attack, foe)) return foe;

            return null;
        }

        private static Hero BestMagicTarget()
        {
            foreach (HeroJobClass jobClass in KillOrder)
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                    if (foe.jobClass == jobClass && Legal(Ability.MagicMissile, foe)) return foe;

            return FirstLivingFoe();
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
            return FindFoeWithout(jobClass, StatusEffect.Petrified, Ability.Petrify, ignoreCover: false);
        }

        private static Hero FindUnslowed(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Slow, Ability.Slow, ignoreCover: false);
        }

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
                if (ally.jobClass == jobClass && ally.health <= 0) return ally;
            return null;
        }

        private static Hero FindLivingAlly(HeroJobClass jobClass)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (ally.jobClass == jobClass) return ally;
            return null;
        }

        private static Hero FindLivingFoe(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jobClass) return foe;
            return null;
        }

        // ============================================================
        // SITUATION DETECTION
        // ============================================================

        // Fighter + Cleric + Wizard, Wizard Dooms, Cleric sustains
        private static bool IsTrinityDoomLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard)  >= 1 &&
                   CountEnemyClass(HeroJobClass.Cleric)  >= 1 &&
                   CountEnemyClass(HeroJobClass.Fighter) >= 1 &&
                   CountEnemyClass(HeroJobClass.Monk)    == 0 &&
                   CountEnemyClass(HeroJobClass.Rogue)   == 0;
        }

        // 2 Wizards + Rogue + Fighter, heavy Petrify spam
        private static bool IsPetrificationLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard) >= 2 &&
                   CountEnemyClass(HeroJobClass.Rogue)  >= 1;
        }

        // 2 Wizards + Fighter, kill enemy Fighter to stop Resurrections
        private static bool IsMeteorRushLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard)  >= 2 &&
                   CountEnemyClass(HeroJobClass.Fighter) >= 1 &&
                   CountEnemyClass(HeroJobClass.Rogue)   == 0;
        }

        private static int CountEnemyClass(HeroJobClass jobClass)
        {
            int count = 0;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jobClass) count++;
            return count;
        }

        private static bool TeamIsStable()
        {
            if (CountBelow(HpLow) > 0) return false;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (HasAnyStatus(ally, DangerousDebuffs)) return false;
            return true;
        }

        private static bool AnyAllyHasItem(Ability ability)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (Utility.AreAbilityAndTargetLegal(ability, ally, false)) return true;
            return false;
        }

        private static int CountBelow(float hpThreshold)
        {
            int count = 0;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (HpRatio(ally) <= hpThreshold) count++;
            return count;
        }

        private static int CountUnpoisonedFoes()
        {
            int count = 0;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (!HasStatus(foe, StatusEffect.Poison)) count++;
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
                if (hero.health > 0) yield return hero;
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

        private static Hero MostUrgentDoomedAlly()
        {
            Hero best = null;
            int lowestDuration = int.MaxValue;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                int dur = GetDoomDuration(ally);
                if (dur > 0 && dur < lowestDuration)
                {
                    lowestDuration = dur;
                    best = ally;
                }
            }
            return best;
        }

        private static int GetDoomDuration(Hero hero)
        {
            if (hero == null) return 0;
            foreach (StatusEffectAndDuration effect in hero.statusEffectsAndDurations)
                if (effect.statusEffect == StatusEffect.Doom)
                    return effect.duration;
            return 0;
        }

        private static bool HasStatus(Hero hero, StatusEffect status)
        {
            if (hero == null) return false;
            foreach (StatusEffectAndDuration effect in hero.statusEffectsAndDurations)
                if (effect.statusEffect == status) return true;
            return false;
        }

        private static bool HasAnyStatus(Hero hero, params StatusEffect[] statuses)
        {
            foreach (StatusEffect status in statuses)
                if (HasStatus(hero, status)) return true;
            return false;
        }
    }
}