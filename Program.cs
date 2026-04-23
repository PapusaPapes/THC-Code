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

    // Team: Wizard / Alchemist / Cleric
    // Items: 2 Ether, 42 Essence
    //
    // Strategy:
    // - Wizard uses Doom as the main win condition.
    // - Alchemist slows enemies, crafts items reactively, and supports the Wizard.
    // - Cleric keeps the team alive and protects key allies.

    public static class MyAI
    {
        public static string FolderExchangePath =
            "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        // Health thresholds
        private const float HpCritical     = 0.30f;
        private const float HpLow          = 0.55f;
        private const float HpLight        = 0.75f;
        private const float HpStableCleric = 0.95f;

        // Mana thresholds
        private const float MpLow = 0.25f;

        // Combat thresholds 
        private const float FinishHp = 0.35f;

        // Alchemist mana and essence costs 
        private const int MinManaToSlow      = 15;
        private const int EssenceCostTier1   = 2;  // Ether, Revive, Remedies
        private const int EssenceCostTier2   = 3;  // Elixir
        private const int EssenceCostTier3   = 4;  // Mega Elixir

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

        // Cleric keeps the team alive
        private static readonly HeroJobClass[] ReviveOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Wizard,
            HeroJobClass.Alchemist
        };

        // Cleric is most important to cleanse
        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Wizard,
            HeroJobClass.Alchemist
        };

        // Doom Cleric and Alchemist first since they sustain the enemy team
        private static readonly HeroJobClass[] DoomOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Fighter,
            HeroJobClass.Monk,
            HeroJobClass.Rogue,
            HeroJobClass.Wizard
        };

        // Petrify physical threats first
        private static readonly HeroJobClass[] PetrifyOrder =
        {
            HeroJobClass.Monk,
            HeroJobClass.Fighter,
            HeroJobClass.Rogue
        };

        // Slow healers and crafters first
        private static readonly HeroJobClass[] SlowOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Monk,
            HeroJobClass.Fighter,
            HeroJobClass.Wizard,
            HeroJobClass.Rogue
        };

        // Used to check if the team is safe enough to apply buffs
        private static readonly StatusEffect[] DangerousDebuffs =
        {
            StatusEffect.Doom,
            StatusEffect.Petrified,
            StatusEffect.Petrifying,
            StatusEffect.Slow,
            StatusEffect.Silence,
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
                case HeroJobClass.Wizard:
                    ControlWizard(actor);
                    return;

                case HeroJobClass.Alchemist:
                    ControlAlchemist(actor);
                    return;

                case HeroJobClass.Cleric:
                    ControlCleric(actor);
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
            if (DoomAllTargets(actor))                           return;

            if (CountUnpoisonedFoes() >= 2 &&
                Act(actor, Ability.PoisonNova, BestMagicTarget())) return;

            if (PetrifyThreats(actor)) return;
            if (SlowAllTargets(actor)) return;

            if (FinishMagicTarget(actor))                              return;
            if (Act(actor, Ability.MagicMissile, BestMagicTarget()))  return;
            if (Act(actor, Ability.Attack,        BestAttackTarget())) return;

            Wait(actor);
        }

        private static bool DoomAllTargets(Hero actor)
        {
            foreach (HeroJobClass jobClass in DoomOrder)
            {
                if (Act(actor, Ability.Doom, FindUndoomed(jobClass))) return true;
            }

            return false;
        }

        private static bool PetrifyThreats(Hero actor)
        {
            foreach (HeroJobClass jobClass in PetrifyOrder)
            {
                if (Act(actor, Ability.Petrify, FindUnpetrified(jobClass))) return true;
            }

            return false;
        }

        // ============================================================
        // ALCHEMIST
        // ============================================================

        private static void ControlAlchemist(Hero actor)
        {
            if (SlowEnemyWizard(actor))        return;
            if (CleansePetrifyIfNoRemedy(actor)) return;
            if (CleanseDoomIfNoRemedy(actor))   return;

            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (CraftVsWizard(actor))      return;
            if (CraftNeededRemedy(actor))  return;
            if (CraftSupportItems(actor))  return;
            if (ReviveOrCraftRevive(actor)) return;

            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            if (actor.mana >= MinManaToSlow && SlowAllTargets(actor)) return;
            if (HasteStableTeam(actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        // Slowing the enemy Wizard immediately delays its Doom and magic damage
        private static bool SlowEnemyWizard(Hero actor)
        {
            if (actor.mana < MinManaToSlow)              return false;
            if (FindLivingFoe(HeroJobClass.Wizard) == null) return false;

            return Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Wizard));
        }

        private static bool CleansePetrifyIfNoRemedy(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);

            if (petrified == null)                    return false;
            if (AnyAllyHasItem(Ability.PetrifyRemedy)) return false;
            if (AnyAllyHasItem(Ability.FullRemedy))   return false;

            return Act(actor, Ability.Cleanse, petrified);
        }

        private static bool CleanseDoomIfNoRemedy(Hero actor)
        {
            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);

            if (doomed == null)                     return false;
            if (AnyAllyHasItem(Ability.FullRemedy)) return false;

            return Act(actor, Ability.Cleanse, doomed);
        }

        // When the enemy has a Wizard, craft Petrify and Full Remedies
        private static bool CraftVsWizard(Hero actor)
        {
            if (!EnemyHasWizard())         return false;
            if (Essence() < EssenceCostTier1) return false;

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

        private static bool HasteStableTeam(Hero actor)
        {
            if (!TeamIsStable()) return false;

            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);
            if (wizard != null &&
                !HasStatus(wizard, StatusEffect.Haste) &&
                Act(actor, Ability.Haste, wizard)) return true;

            Hero cleric = FindLivingAlly(HeroJobClass.Cleric);
            if (cleric != null &&
                !HasStatus(cleric, StatusEffect.Haste) &&
                Act(actor, Ability.Haste, cleric)) return true;

            return false;
        }

        // ============================================================
        // CLERIC
        // ============================================================

        private static void ControlCleric(Hero actor)
        {
            if (ResurrectDeadAlly(actor))  return;
            if (CleanseUrgentDebuffs(actor)) return;
            if (RemoveOwnSilence(actor))   return;
            if (UseEther(actor, MpLow))    return;
            if (HealTeam(actor))           return;
            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            if (TeamIsStable() && HpRatio(actor) >= HpStableCleric)
            {
                if (ApplyAutoLife(actor)) return;
                if (ApplyFaith(actor))    return;
            }

            if (LightHealBeforeAttack(actor))               return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
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

        private static bool CleanseUrgentDebuffs(Hero actor)
        {
            foreach (HeroJobClass jobClass in CleanseOrder)
            {
                Hero ally = FindLivingAlly(jobClass);

                if (ally == null) continue;

                if (HasStatus(ally, StatusEffect.Petrified) ||
                    HasStatus(ally, StatusEffect.Petrifying))
                    if (Act(actor, Ability.QuickCleanse, ally)) return true;

                if (HasStatus(ally, StatusEffect.Doom) &&
                    Act(actor, Ability.QuickCleanse, ally)) return true;
            }

            return false;
        }

        private static bool RemoveOwnSilence(Hero actor)
        {
            if (!HasStatus(actor, StatusEffect.Silence)) return false;

            if (Act(actor, Ability.SilenceRemedy, actor)) return true;
            if (Act(actor, Ability.FullRemedy,    actor)) return true;

            return false;
        }

        private static bool HealTeam(Hero actor)
        {
            if (CountBelow(HpLow) >= 2 && Act(actor, Ability.MassHeal, actor)) return true;

            if (HealSelf(actor))    return true;
            if (HealWizard(actor))  return true;
            if (HealLowest(actor))  return true;
            if (CleansePoisonedAlly(actor)) return true;

            return false;
        }

        private static bool HealSelf(Hero actor)
        {
            if (HpRatio(actor) > HpLight) return false;

            if (Act(actor, Ability.QuickHeal,   actor)) return true;
            if (Act(actor, Ability.CureSerious, actor)) return true;

            return false;
        }

        private static bool HealWizard(Hero actor)
        {
            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);

            if (wizard == null)           return false;
            if (HpRatio(wizard) > HpLow) return false;

            if (Act(actor, Ability.QuickHeal,   wizard)) return true;
            if (Act(actor, Ability.CureSerious, wizard)) return true;

            return false;
        }

        private static bool HealLowest(Hero actor)
        {
            Hero lowest = LowestAlly();

            if (lowest == null) return false;

            if (HpRatio(lowest) <= HpCritical)
            {
                if (Act(actor, Ability.QuickHeal,   lowest)) return true;
                if (Act(actor, Ability.CureSerious, lowest)) return true;
            }

            if (HpRatio(lowest) <= HpLow &&
                Act(actor, Ability.CureSerious, lowest)) return true;

            return false;
        }

        private static bool CleansePoisonedAlly(Hero actor)
        {
            Hero poisoned = FindAllyWithStatus(StatusEffect.Poison);

            if (poisoned == null)            return false;
            if (HpRatio(poisoned) > HpLight) return false;

            return Act(actor, Ability.QuickCleanse, poisoned);
        }

        private static bool ApplyAutoLife(Hero actor)
        {
            foreach (HeroJobClass jobClass in ReviveOrder)
            {
                Hero ally = jobClass == actor.jobClass
                    ? actor
                    : FindLivingAlly(jobClass);

                if (ally != null &&
                    !HasStatus(ally, StatusEffect.AutoLife) &&
                    Act(actor, Ability.AutoLife, ally)) return true;
            }

            return false;
        }

        private static bool ApplyFaith(Hero actor)
        {
            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);
            if (wizard != null &&
                !HasStatus(wizard, StatusEffect.Faith) &&
                Act(actor, Ability.Faith, wizard)) return true;

            if (!HasStatus(actor, StatusEffect.Faith) &&
                Act(actor, Ability.Faith, actor)) return true;

            return false;
        }

        private static bool LightHealBeforeAttack(Hero actor)
        {
            Hero lowest = LowestAlly();

            if (lowest == null)             return false;
            if (HpRatio(lowest) > HpLight) return false;

            return Act(actor, Ability.CureLight, lowest);
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

            // A silenced Wizard or Cleric cannot cast, spend a remedy
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
            foreach (HeroJobClass jobClass in SlowOrder)
            {
                if (Act(actor, Ability.Slow, FindUnslowed(jobClass))) return true;
            }

            return false;
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

        // Finds the highest priority living foe 
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

        // find a foe of a given class
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

        private static bool EnemyHasWizard()
        {
            return FindLivingFoe(HeroJobClass.Wizard) != null;
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