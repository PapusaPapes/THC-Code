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

    // Team: Fighter / Fighter / Alchemist
    // Items: 2 Ether, 42 Essence
    // Strategy:
    // Both Fighters stack Brave, use QuickHit to finish targets, and Resurrect each other.
    // Alchemist crafts MegaElixirs, Ethers, and remedies to sustain the team.

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
            HeroJobClass.Fighter
        };

        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Fighter,
            HeroJobClass.Alchemist
        };

        // Cycle: 4 FullRemedies then 1 MegaElixir in Trinity Doom
        private static int  trinityDoomCycleStep = 0;
        private static bool trinityDoomActive     = false;

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
            if (ResurrectDeadAlly(actor)) return;

            if (IsTrinityDoomLike())
            {
                if (ResurrectDeadAlly(actor)) return;
                Hero alch = FindLivingAlly(HeroJobClass.Alchemist);
                if (alch != null && HpRatio(alch) <= HpLow &&
                    Act(actor, Ability.CureSerious, alch)) return;
                if (alch != null && MpRatio(alch) <= 0.20f &&
                    Act(actor, Ability.Ether, alch)) return;
                if (ApplyBrave(actor)) return;
                // Kill enemy Fighter to remove Cover, then hit Wizard/Cleric
                Hero enemyFighter = FindLivingFoe(HeroJobClass.Fighter);
                Hero enemyWiz     = FindLivingFoe(HeroJobClass.Wizard);
                Hero enemyCleric  = FindLivingFoe(HeroJobClass.Cleric);
                if (enemyFighter != null && Act(actor, Ability.QuickHit, enemyFighter)) return;
                if (enemyWiz     != null && Act(actor, Ability.QuickHit, enemyWiz))     return;
                if (enemyCleric  != null && Act(actor, Ability.QuickHit, enemyCleric))  return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))                      return;
                Wait(actor);
                return;
            }

            // Lmt Brk Crafter: hammer enemy Alchemist before it crafts more Revives
            if (IsLmtBrkCrafterLike())
            {
                if (ResurrectDeadAlly(actor)) return;
                if (ApplyBrave(actor)) return;
                Hero enemyAlch = FindLivingFoe(HeroJobClass.Alchemist);
                if (FinishPhysicalTarget(actor))                                        return;
                if (enemyAlch != null && Act(actor, Ability.QuickHit, enemyAlch))      return;
                if (enemyAlch != null && Act(actor, Ability.Attack,   enemyAlch))      return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))                     return;
                Wait(actor);
                return;
            }

            if (ApplyBrave(actor))        return;

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
        // ALCHEMIST
        // ============================================================

        private static void ControlAlchemist(Hero actor)
        {
            if (HpRatio(actor) <= HpLight && HealCriticalAlly(actor)) return;

            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (CleansePetrifyIfNoRemedy(actor)) return;

            // Lmt Brk Crafter: Haste Fighters, craft MegaElixir, attack
            if (IsLmtBrkCrafterLike())
            {
                if (HasteFirstFighter(actor)) return;
                if (Essence() >= EssenceCostTier3 &&
                    !AnyAllyHasItem(Ability.MegaElixir) &&
                    SelfCast(actor, Ability.CraftMegaElixir)) return;
                if (ReviveOrCraftRevive(actor)) return;
                if (UseEther(actor, MpLow))     return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            // Petrification: just keep crafting PetrifyRemedies
            if (IsPetrificationLike())
            {
                if (Essence() >= EssenceCostTier1 &&
                    SelfCast(actor, Ability.CraftPetrifyRemedy)) return;
                Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
                if (petrified != null && Act(actor, Ability.PetrifyRemedy, petrified)) return;
                if (UseEther(actor, MpLow)) return;
                if (ReviveOrCraftRevive(actor)) return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            // Trinity Doom: cycle 7 Revives then MegaElixir
            if (IsTrinityDoomLike())
            {
                if (HpRatio(actor) <= HpCritical &&
                    SelfCast(actor, Ability.MegaElixir)) return;
                if (UseEther(actor, 0.15f)) return;
                if (trinityDoomCycleStep < 4)
                {
                    if (Essence() >= EssenceCostTier1 &&
                        SelfCast(actor, Ability.CraftRevive))
                    { trinityDoomCycleStep++; return; }
                }
                else if (trinityDoomCycleStep == 4)
                {
                    if (Essence() >= EssenceCostTier3 &&
                        SelfCast(actor, Ability.CraftMegaElixir))
                    { trinityDoomCycleStep++; return; }
                }
                else
                {
                    if (SelfCast(actor, Ability.MegaElixir))
                    { trinityDoomCycleStep = 0; return; }
                    // Reset and start over if stuck
                    trinityDoomCycleStep = 0;
                }
                // Use Revives reactively when dead ally needs it
                Hero dead = BestDeadAlly();
                if (dead != null && Act(actor, Ability.Revive, dead)) return;
                if (UseEther(actor, MpLow)) return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            if (CleanseDoomIfNoRemedy(actor))    return;

            if (CraftNeededRemedy(actor))   return;
            if (ReviveOrCraftRevive(actor)) return;
            if (CraftSupportItems(actor))   return;

            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            if (HasteFirstFighter(actor)) return;
            if (actor.mana >= MinManaToSlow && SlowAllTargets(actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        private static bool HasteFirstFighter(Hero actor)
        {
            if (!TeamIsStable()) return false;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (ally.jobClass == HeroJobClass.Fighter &&
                    !HasStatus(ally, StatusEffect.Haste) &&
                    Act(actor, Ability.Haste, ally)) return true;
            }
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
            if (doomed != null && Act(actor, Ability.FullRemedy, doomed)) return true;

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

        // ============================================================
        // TARGET FINDERS
        // ============================================================

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

        private static bool IsLmtBrkCrafterLike()
        {
            return CountEnemyClass(HeroJobClass.Monk)      >= 2 &&
                   CountEnemyClass(HeroJobClass.Alchemist) >= 1 &&
                   CountEnemyClass(HeroJobClass.Wizard)    == 0 &&
                   CountEnemyClass(HeroJobClass.Rogue)     == 0;
        }

        private static bool IsPetrificationLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard) >= 2 &&
                   CountEnemyClass(HeroJobClass.Rogue)  >= 1;
        }

        private static bool IsTrinityDoomLike()
        {
            // Set flag when we first detect Trinity Doom
            if (CountEnemyClass(HeroJobClass.Wizard)  >= 1 &&
                CountEnemyClass(HeroJobClass.Cleric)  >= 1 &&
                CountEnemyClass(HeroJobClass.Fighter) >= 1 &&
                CountEnemyClass(HeroJobClass.Monk)    == 0 &&
                CountEnemyClass(HeroJobClass.Rogue)   == 0)
            {
                trinityDoomActive = true;
                return true;
            }
            // Keep strategy active after Wizard dies as long as Cleric or Fighter remain
            return trinityDoomActive &&
                   (CountEnemyClass(HeroJobClass.Cleric) >= 1 ||
                    CountEnemyClass(HeroJobClass.Fighter) >= 1);
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