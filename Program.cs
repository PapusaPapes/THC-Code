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

    // Team: Wizard / Rogue / Alchemist
    // Items: 2 Ether, 42 Essence
    //
    // Strategy:
    // Wizard PoisonNovas then Dooms all targets on a timer.
    // Rogue silences casters, poisons, stuns, and steals items.
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
        private const float MpLow   = 0.25f;
        private const float MpRogue = 0.20f;

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
            HeroJobClass.Rogue
        };

        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Wizard,
            HeroJobClass.Alchemist,
            HeroJobClass.Rogue
        };

        public static void ProcessAI()
        {
            Hero actor = TeamHeroCoder.BattleState.heroWithInitiative;

            if (actor == null || actor.health <= 0)
                return;

            Console.WriteLine($"Actor: {actor.jobClass} HP:{actor.health}/{actor.maxHealth} MP:{actor.mana}/{actor.maxMana}");

            switch (actor.jobClass)
            {
                case HeroJobClass.Wizard:
                    ControlWizard(actor);
                    return;

                case HeroJobClass.Rogue:
                    ControlRogue(actor);
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
        // WIZARD
        // ============================================================

        private static void ControlWizard(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (DispelEnemyAutoLife(actor, Ability.QuickDispel)) return;

            // PoisonNova first, sets up Rogue poison synergy
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
        // ROGUE
        // ============================================================

        private static void ControlRogue(Hero actor)
        {
            if (UseEmergencyItem(actor))  return;
            if (UseEther(actor, MpRogue)) return;

            // Silence casters first
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Alchemist))) return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Wizard)))    return;

            if (FinishPhysicalTarget(actor)) return;

            // Poison high priority targets
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Alchemist))) return;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Wizard)))    return;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Fighter)))   return;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Monk)))      return;

            if (Act(actor, Ability.StunStrike, BestAttackTarget())) return;
            if (Act(actor, Ability.Attack,     BestAttackTarget())) return;
            if (Act(actor, Ability.Attack,     FirstLivingFoe()))   return;

            Wait(actor);
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
            if (CleanseDoomIfNoRemedy(actor))    return;

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

            if (FindAllyWithStatus(StatusEffect.Silence) != null &&
                !AnyAllyHasItem(Ability.SilenceRemedy) &&
                SelfCast(actor, Ability.CraftSilenceRemedy)) return true;

            return false;
        }

        private static bool CraftSupportItems(Hero actor)
        {
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
                // Rogue doesn't need mana, skip it
                if (ally.jobClass == HeroJobClass.Rogue) continue;

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

        private static Hero FindUnsilenced(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Silence, Ability.SilenceStrike, ignoreCover: false);
        }

        private static Hero FindUnpoisoned(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Poison, Ability.PoisonStrike, ignoreCover: false);
        }

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

        // ============================================================
        // SITUATION DETECTION
        // ============================================================

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