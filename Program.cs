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

    // Team: Wizard / Monk / Cleric
    // Items: 3 Ether, 1 Silence Remedy, 5 Poison Remedy, 3 Petrify Remedy, 3 Full Remedy = 70g
    // Strategy:
    // Wizard opens with PoisonNova, then Dooms priority targets.
    // Monk follows with FlurryOfBlows, Bitter Bloom amplifies damage on poisoned targets.
    // Cleric sustains the team with heals, AutoLife, and cleanses debuffs.

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
            HeroJobClass.Cleric,
            HeroJobClass.Wizard,
            HeroJobClass.Monk
        };

        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Wizard,
            HeroJobClass.Monk,
            HeroJobClass.Cleric
        };

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
                case HeroJobClass.Wizard:
                    ControlWizard(actor);
                    return;

                case HeroJobClass.Monk:
                    ControlMonk(actor);
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

            // Ctrl & Sustain: PoisonNova for Bitterbloom, then blast
            if (IsCtrlAndSustainLike())
            {
                if (CountUnpoisonedFoes() > 0 &&
                    Act(actor, Ability.PoisonNova, BestMagicTarget()))    return;
                if (FinishMagicTarget(actor))                             return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
                if (Act(actor, Ability.Attack,        BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,        FirstLivingFoe()))   return;
                Wait(actor);
                return;
            }

            // Poison Tribal: Doom all Monks
            if (IsPoisonTribalLike())
            {
                if (DoomAllTargets(actor))                                return;
                if (FinishMagicTarget(actor))                             return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
                if (Act(actor, Ability.Attack,        BestAttackTarget())) return;
                if (Act(actor, Ability.Attack,        FirstLivingFoe()))   return;
                Wait(actor);
                return;
            }

            // PoisonNova first, sets up Bitter Bloom for the Monk
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
        // MONK
        // ============================================================

        private static void ControlMonk(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;

            // Ctrl & Sustain: wait for PoisonNova then FlurryOfBlows for Bitterbloom
            if (IsCtrlAndSustainLike())
            {
                Hero alchemist = FindLivingFoe(HeroJobClass.Alchemist);
                // Only FlurryOfBlows when enemies are poisoned, maximize Bitterbloom
                if (alchemist != null && CountUnpoisonedFoes() == 0 &&
                    Act(actor, Ability.FlurryOfBlows, alchemist)) return;
                if (FinishPhysicalTarget(actor))                   return;
                if (alchemist != null && Act(actor, Ability.Attack, alchemist)) return;
                if (Act(actor, Ability.Attack, BestAttackTarget()))             return;
                Wait(actor);
                return;
            }

            if (TeamIsStable() && ApplyBrave(actor)) return;

            if (Act(actor, Ability.FlurryOfBlows, BestAttackTarget())) return;

            if (DebraveThreats(actor)) return;
            if (DefaithThreats(actor)) return;

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

        // ============================================================
        // CLERIC
        // ============================================================

        private static void ControlCleric(Hero actor)
        {
            if (ResurrectDeadAlly(actor))    return;
            if (ApplyAutoLife(actor))        return;
            if (CleanseUrgentDebuffs(actor)) return;
            if (RemoveOwnSilence(actor))     return;
            if (UseEther(actor, MpLow))      return;
            if (HealTeam(actor))             return;
            if (CleansePoisonedAlly(actor))  return;
            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            if (!IsCtrlAndSustainLike() && !IsPoisonTribalLike() &&
                TeamIsStable() && HpRatio(actor) >= HpStableCleric)
            {
                if (ApplyBuffs(actor)) return;
            }

            if (LightHealBeforeAttack(actor))                   return;
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
            if (HealSelf(actor))   return true;
            if (HealLowest(actor)) return true;
            return false;
        }

        private static bool HealSelf(Hero actor)
        {
            if (HpRatio(actor) > HpLight) return false;
            if (Act(actor, Ability.QuickHeal,   actor)) return true;
            if (Act(actor, Ability.CureSerious, actor)) return true;
            return false;
        }

        private static bool HealLowest(Hero actor)
        {
            Hero lowest = LowestAlly();
            if (lowest == null) return false;

            // Don't over heal Monk, Adrenaline is active below 51%
            if (lowest.jobClass == HeroJobClass.Monk && HpRatio(lowest) > HpCritical)
                return false;

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

        private static bool ApplyBuffs(Hero actor)
        {
            Hero monk = FindLivingAlly(HeroJobClass.Monk);
            if (monk != null && !HasStatus(monk, StatusEffect.Haste) &&
                Act(actor, Ability.Haste, monk)) return true;

            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);
            if (wizard != null && !HasStatus(wizard, StatusEffect.Faith) &&
                Act(actor, Ability.Faith, wizard)) return true;

            if (!HasStatus(actor, StatusEffect.Faith) &&
                Act(actor, Ability.Faith, actor)) return true;

            return false;
        }

        private static bool LightHealBeforeAttack(Hero actor)
        {
            Hero lowest = LowestAlly();
            if (lowest == null)            return false;
            if (HpRatio(lowest) > HpLight) return false;
            if (lowest.jobClass == HeroJobClass.Monk) return false;
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
            // In Ctrl & Sustain only the Wizard needs mana, keep Doom going
            if (IsCtrlAndSustainLike())
            {
                Hero wizard = FindLivingAlly(HeroJobClass.Wizard);
                if (wizard != null && MpRatio(wizard) <= threshold)
                    return Act(actor, Ability.Ether, wizard);
                return false;
            }

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

        private static Hero FindWithoutDebrave(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Debrave, Ability.Debrave, ignoreCover: false);
        }

        private static Hero FindWithoutDefaith(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Defaith, Ability.Defaith, ignoreCover: false);
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

        private static Hero FindLivingFoe(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jobClass) return foe;
            return null;
        }

        // ============================================================
        // SITUATION DETECTION
        // ============================================================

        // 2 Alchemists + Monk
        private static bool IsCtrlAndSustainLike()
        {
            return CountEnemyClass(HeroJobClass.Alchemist) >= 2;
        }

        // 3+ Monks + Wizard
        private static bool IsPoisonTribalLike()
        {
            return CountEnemyClass(HeroJobClass.Monk)      >= 2 &&
                   CountEnemyClass(HeroJobClass.Alchemist) == 0 &&
                   CountEnemyClass(HeroJobClass.Fighter)   == 0 &&
                   CountEnemyClass(HeroJobClass.Cleric)    == 0;
        }

        private static bool TeamIsStable()
        {
            if (CountBelow(HpLow) > 0) return false;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (HasAnyStatus(ally, DangerousDebuffs)) return false;
            return true;
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

        private static int CountEnemyClass(HeroJobClass jobClass)
        {
            int count = 0;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jobClass) count++;
            return count;
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