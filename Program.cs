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
    // Strategy:
    // - Wizard uses Doom as the main win condition.
    // - Alchemist slows enemies, crafts items, and supports Wizard.
    // - Cleric keeps the team alive and protects key allies.

    public static class MyAI
    {
        public static string FolderExchangePath =
            "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        private const float HpCritical = 0.30f;
        private const float HpLow = 0.55f;
        private const float HpLight = 0.75f;
        private const float MpLow = 0.25f;
        private const float FinishHp = 0.35f;
        private const float StableClericHp = 0.95f;

        private static readonly HeroJobClass[] KillOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Wizard,
            HeroJobClass.Rogue,
            HeroJobClass.Monk,
            HeroJobClass.Fighter
        };

        private static readonly HeroJobClass[] DoomOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Fighter,
            HeroJobClass.Monk,
            HeroJobClass.Rogue,
            HeroJobClass.Wizard
        };

        private static readonly HeroJobClass[] PetrifyOrder =
        {
            HeroJobClass.Monk,
            HeroJobClass.Fighter,
            HeroJobClass.Rogue
        };

        private static readonly HeroJobClass[] SlowOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Monk,
            HeroJobClass.Fighter,
            HeroJobClass.Wizard,
            HeroJobClass.Rogue
        };

        private static readonly StatusEffect[] Debuffs =
        {
            StatusEffect.Debrave,
            StatusEffect.Defaith,
            StatusEffect.Doom,
            StatusEffect.Petrified,
            StatusEffect.Petrifying,
            StatusEffect.Slow,
            StatusEffect.Silence,
            StatusEffect.Poison
        };

        private static readonly StatusEffect[] Buffs =
        {
            StatusEffect.Brave,
            StatusEffect.Faith,
            StatusEffect.AutoLife,
            StatusEffect.Haste
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

        private static void ControlWizard(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow)) return;

            if (DispelAutoLife(actor, Ability.QuickDispel)) return;
            if (DoomTargets(actor)) return;

            if (CountUnpoisonedFoes() >= 2 &&
                Act(actor, Ability.PoisonNova, BestMagicTarget())) return;

            if (PetrifyThreats(actor)) return;
            if (SlowTargets(actor)) return;

            if (FinishMagicTarget(actor)) return;
            if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        private static void ControlAlchemist(Hero actor)
        {
            if (SlowEnemyWizard(actor)) return;
            if (CleansePetrifyNoRemedy(actor)) return;
            if (CleanseDoomNoRemedy(actor)) return;

            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow)) return;

            if (CraftVsWizard(actor)) return;
            if (CraftNeededRemedy(actor)) return;
            if (CraftSupportItems(actor)) return;
            if (ReviveOrCraftRevive(actor)) return;

            if (DispelAutoLife(actor, Ability.Dispel)) return;

            if (actor.mana >= 15 && SlowTargets(actor)) return;
            if (HasteStableTeam(actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;
            Wait(actor);
        }

        private static void ControlCleric(Hero actor)
        {
            if (ResurrectBestAlly(actor)) return;
            if (CleanseDanger(actor)) return;
            if (RemoveOwnSilence(actor)) return;
            if (UseEther(actor, MpLow)) return;
            if (HealTeam(actor)) return;
            if (DispelAutoLife(actor, Ability.Dispel)) return;

            if (TeamIsStable() && HpRatio(actor) >= StableClericHp)
            {
                if (ApplyAutoLife(actor)) return;
                if (ApplyFaith(actor)) return;
            }

            if (LightHealBeforeAttack(actor)) return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        private static bool DoomTargets(Hero actor)
        {
            foreach (HeroJobClass jobClass in DoomOrder)
            {
                Hero target = FindUndoomed(jobClass);

                if (Act(actor, Ability.Doom, target))
                    return true;
            }

            return false;
        }

        private static bool PetrifyThreats(Hero actor)
        {
            foreach (HeroJobClass jobClass in PetrifyOrder)
            {
                Hero target = FindUnpetrified(jobClass);

                if (Act(actor, Ability.Petrify, target))
                    return true;
            }

            return false;
        }

        private static bool SlowTargets(Hero actor)
        {
            foreach (HeroJobClass jobClass in SlowOrder)
            {
                Hero target = FindUnslowed(jobClass);

                if (Act(actor, Ability.Slow, target))
                    return true;
            }

            return false;
        }

        private static bool SlowEnemyWizard(Hero actor)
        {
            if (actor.mana < 15)
                return false;

            if (FindLivingFoe(HeroJobClass.Wizard) == null)
                return false;

            return Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Wizard));
        }

        private static bool CleansePetrifyNoRemedy(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(
                StatusEffect.Petrified,
                StatusEffect.Petrifying
            );

            if (petrified == null)
                return false;

            if (AnyAllyHasItem(Ability.PetrifyRemedy))
                return false;

            if (AnyAllyHasItem(Ability.FullRemedy))
                return false;

            return Act(actor, Ability.Cleanse, petrified);
        }

        private static bool CleanseDoomNoRemedy(Hero actor)
        {
            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);

            if (doomed == null)
                return false;

            if (AnyAllyHasItem(Ability.FullRemedy))
                return false;

            return Act(actor, Ability.Cleanse, doomed);
        }

        private static bool CraftVsWizard(Hero actor)
        {
            if (!EnemyHasWizard())
                return false;

            if (Essence() < 2)
                return false;

            if (!AnyAllyHasItem(Ability.PetrifyRemedy) &&
                SelfCast(actor, Ability.CraftPetrifyRemedy)) return true;

            if (!AnyAllyHasItem(Ability.FullRemedy) &&
                SelfCast(actor, Ability.CraftFullRemedy)) return true;

            return false;
        }

        private static bool CraftNeededRemedy(Hero actor)
        {
            if (Essence() < 2)
                return false;

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
            if (Essence() >= 2 &&
                !AnyAllyHasItem(Ability.Ether) &&
                SelfCast(actor, Ability.CraftEther)) return true;

            if (Essence() >= 3 &&
                !AnyAllyHasItem(Ability.Elixir) &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                SelfCast(actor, Ability.CraftElixir)) return true;

            if (Essence() >= 4 &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                AnyAllyHasItem(Ability.Elixir) &&
                SelfCast(actor, Ability.CraftMegaElixir)) return true;

            return false;
        }

        private static bool ReviveOrCraftRevive(Hero actor)
        {
            Hero dead = BestDeadAlly();

            if (dead == null)
                return false;

            if (Essence() >= 2 &&
                !AnyAllyHasItem(Ability.Revive) &&
                SelfCast(actor, Ability.CraftRevive)) return true;

            return Act(actor, Ability.Revive, dead);
        }

        private static bool HasteStableTeam(Hero actor)
        {
            if (!TeamIsStable())
                return false;

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

        private static bool ResurrectBestAlly(Hero actor)
        {
            Hero dead = BestDeadAlly();
            return dead != null && Act(actor, Ability.Resurrection, dead);
        }

        private static bool CleanseDanger(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(
                StatusEffect.Petrified,
                StatusEffect.Petrifying
            );

            if (petrified != null &&
                Act(actor, Ability.QuickCleanse, petrified)) return true;

            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            if (doomed != null &&
                Act(actor, Ability.QuickCleanse, doomed)) return true;

            return false;
        }

        private static bool RemoveOwnSilence(Hero actor)
        {
            if (!HasStatus(actor, StatusEffect.Silence))
                return false;

            if (Act(actor, Ability.SilenceRemedy, actor)) return true;
            if (Act(actor, Ability.FullRemedy, actor)) return true;

            return false;
        }

        private static bool HealTeam(Hero actor)
        {
            if (CountBelow(HpLow) >= 2 &&
                Act(actor, Ability.MassHeal, actor)) return true;

            if (HealSelf(actor)) return true;
            if (HealWizard(actor)) return true;
            if (HealLowest(actor)) return true;
            if (CleansePoisonedAlly(actor)) return true;

            return false;
        }

        private static bool HealSelf(Hero actor)
        {
            if (HpRatio(actor) > HpLight)
                return false;

            if (Act(actor, Ability.QuickHeal, actor)) return true;
            if (Act(actor, Ability.CureSerious, actor)) return true;

            return false;
        }

        private static bool HealWizard(Hero actor)
        {
            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);

            if (wizard == null)
                return false;

            if (HpRatio(wizard) > HpLow)
                return false;

            if (Act(actor, Ability.QuickHeal, wizard)) return true;
            if (Act(actor, Ability.CureSerious, wizard)) return true;

            return false;
        }

        private static bool HealLowest(Hero actor)
        {
            Hero lowest = LowestAlly();

            if (lowest == null)
                return false;

            if (HpRatio(lowest) <= HpCritical)
            {
                if (Act(actor, Ability.QuickHeal, lowest)) return true;
                if (Act(actor, Ability.CureSerious, lowest)) return true;
            }

            if (HpRatio(lowest) <= HpLow &&
                Act(actor, Ability.CureSerious, lowest)) return true;

            return false;
        }

        private static bool CleansePoisonedAlly(Hero actor)
        {
            Hero poisoned = FindAllyWithStatus(StatusEffect.Poison);

            if (poisoned == null)
                return false;

            if (HpRatio(poisoned) > HpLight)
                return false;

            return Act(actor, Ability.QuickCleanse, poisoned);
        }

        private static bool ApplyAutoLife(Hero actor)
        {
            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);
            if (wizard != null &&
                !HasStatus(wizard, StatusEffect.AutoLife) &&
                Act(actor, Ability.AutoLife, wizard)) return true;

            Hero alchemist = FindLivingAlly(HeroJobClass.Alchemist);
            if (alchemist != null &&
                !HasStatus(alchemist, StatusEffect.AutoLife) &&
                Act(actor, Ability.AutoLife, alchemist)) return true;

            if (!HasStatus(actor, StatusEffect.AutoLife) &&
                Act(actor, Ability.AutoLife, actor)) return true;

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

            if (lowest == null)
                return false;

            if (HpRatio(lowest) > HpLight)
                return false;

            return Act(actor, Ability.CureLight, lowest);
        }

        private static bool UseEmergencyItem(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(
                StatusEffect.Petrified,
                StatusEffect.Petrifying
            );

            if (petrified != null &&
                Act(actor, Ability.PetrifyRemedy, petrified)) return true;

            if (petrified != null &&
                Act(actor, Ability.FullRemedy, petrified)) return true;

            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            if (doomed != null &&
                Act(actor, Ability.FullRemedy, doomed)) return true;

            Hero silenced = FindAllyWithStatus(StatusEffect.Silence);
            if (IsImportantSilencedAlly(silenced))
            {
                if (Act(actor, Ability.SilenceRemedy, silenced)) return true;
                if (Act(actor, Ability.FullRemedy, silenced)) return true;
            }

            if (CountBelow(HpLow) >= 2 &&
                SelfCast(actor, Ability.MegaElixir)) return true;

            Hero dead = BestDeadAlly();
            if (dead != null &&
                Act(actor, Ability.Revive, dead)) return true;

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
            Hero target = null;
            float lowestMp = threshold + 0.001f;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float mp = MpRatio(ally);

                if (mp >= lowestMp)
                    continue;

                lowestMp = mp;
                target = ally;
            }

            return target != null && Act(actor, Ability.Ether, target);
        }

        private static bool IsImportantSilencedAlly(Hero ally)
        {
            if (ally == null)
                return false;

            return ally.jobClass == HeroJobClass.Wizard ||
                   ally.jobClass == HeroJobClass.Cleric;
        }

        private static bool FinishMagicTarget(Hero actor)
        {
            Hero target = BestMagicTarget();

            return target != null &&
                   HpRatio(target) <= FinishHp &&
                   Act(actor, Ability.MagicMissile, target);
        }

        private static Hero BestAttackTarget()
        {
            foreach (HeroJobClass jobClass in KillOrder)
            {
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                {
                    if (foe.jobClass == jobClass && Legal(Ability.Attack, foe))
                        return foe;
                }
            }

            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (LegalIgnoreCover(Ability.Attack, foe))
                    return foe;
            }

            return null;
        }

        private static Hero BestMagicTarget()
        {
            foreach (HeroJobClass jobClass in KillOrder)
            {
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                {
                    if (foe.jobClass == jobClass &&
                        Legal(Ability.MagicMissile, foe))
                    {
                        return foe;
                    }
                }
            }

            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (Legal(Ability.MagicMissile, foe))
                    return foe;
            }

            return null;
        }

        private static Hero FindUndoomed(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass != jobClass)
                    continue;

                if (HasStatus(foe, StatusEffect.Doom))
                    continue;

                if (Legal(Ability.Doom, foe))
                    return foe;
            }

            return null;
        }

        private static Hero FindUnpetrified(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass != jobClass)
                    continue;

                if (HasStatus(foe, StatusEffect.Petrified))
                    continue;

                if (HasStatus(foe, StatusEffect.Petrifying))
                    continue;

                if (Legal(Ability.Petrify, foe))
                    return foe;
            }

            return null;
        }

        private static Hero FindUnslowed(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass != jobClass)
                    continue;

                if (HasStatus(foe, StatusEffect.Slow))
                    continue;

                if (Legal(Ability.Slow, foe))
                    return foe;
            }

            return null;
        }

        private static Hero FindAllyWithStatus(params StatusEffect[] statuses)
        {
            Hero actor = TeamHeroCoder.BattleState.heroWithInitiative;
            Hero bestAlly = null;
            float bestScore = float.MinValue;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (ally == actor)
                    continue;

                if (!HasAnyStatus(ally, statuses))
                    continue;

                float score = AllyValue(ally) + (1f - HpRatio(ally)) * 100f;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestAlly = ally;
            }

            if (actor != null &&
                HasAnyStatus(actor, statuses) &&
                bestAlly == null)
            {
                return actor;
            }

            return bestAlly;
        }

        private static Hero LowestAlly()
        {
            Hero lowestAlly = null;
            float lowestHp = float.MaxValue;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float hp = HpRatio(ally);

                if (hp >= lowestHp)
                    continue;

                lowestHp = hp;
                lowestAlly = ally;
            }

            return lowestAlly;
        }

        private static Hero BestDeadAlly()
        {
            Hero bestAlly = null;
            float bestScore = float.MinValue;

            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health > 0)
                    continue;

                float score = AllyValue(ally);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestAlly = ally;
            }

            return bestAlly;
        }

        private static Hero FindLivingAlly(HeroJobClass jobClass)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (ally.jobClass == jobClass)
                    return ally;
            }

            return null;
        }

        private static Hero FindLivingFoe(HeroJobClass jobClass)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (foe.jobClass == jobClass)
                    return foe;
            }

            return null;
        }

        private static bool EnemyHasWizard()
        {
            return FindLivingFoe(HeroJobClass.Wizard) != null;
        }

        private static bool TeamIsStable()
        {
            if (CountBelow(HpLow) > 0)
                return false;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (HasAnyDebuff(ally))
                    return false;
            }

            return true;
        }

        private static bool AnyAllyHasItem(Ability ability)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (Utility.AreAbilityAndTargetLegal(ability, ally, false))
                    return true;
            }

            return false;
        }

        private static int CountBelow(float hpThreshold)
        {
            int count = 0;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (HpRatio(ally) <= hpThreshold)
                    count++;
            }

            return count;
        }

        private static int CountUnpoisonedFoes()
        {
            int count = 0;

            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (!HasStatus(foe, StatusEffect.Poison))
                    count++;
            }

            return count;
        }

        private static int Essence()
        {
            return TeamHeroCoder.BattleState.allyEssenceCount;
        }

        private static bool DispelAutoLife(Hero actor, Ability dispelAbility)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
            {
                if (HasStatus(foe, StatusEffect.AutoLife) &&
                    Act(actor, dispelAbility, foe))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Act(Hero actor, Ability ability, Hero target)
        {
            if (actor == null || target == null)
                return false;

            if (!Utility.AreAbilityAndTargetLegal(ability, target, false))
                return false;

            Console.WriteLine($"{actor.jobClass} uses {ability} on {target.jobClass}");
            TeamHeroCoder.PerformHeroAbility(ability, target);
            return true;
        }

        private static bool SelfCast(Hero actor, Ability ability)
        {
            if (actor == null)
                return false;

            if (!Utility.AreAbilityAndTargetLegal(ability, actor, false))
                return false;

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
                if (hero.health > 0)
                    yield return hero;
            }
        }

        private static float HpRatio(Hero hero)
        {
            if (hero == null || hero.maxHealth <= 0)
                return 1f;

            return (float)hero.health / hero.maxHealth;
        }

        private static float MpRatio(Hero hero)
        {
            if (hero == null || hero.maxMana <= 0)
                return 1f;

            return (float)hero.mana / hero.maxMana;
        }

        private static float AllyValue(Hero hero)
        {
            float score = 0f;

            if (hero.jobClass == HeroJobClass.Cleric)
                score += 350f;

            if (hero.jobClass == HeroJobClass.Wizard)
                score += 320f;

            if (hero.jobClass == HeroJobClass.Alchemist)
                score += 280f;

            return score +
                   hero.speed * 2f +
                   hero.physicalAttack * 1.8f +
                   hero.special * 1.5f;
        }

        private static bool HasStatus(Hero hero, StatusEffect status)
        {
            if (hero == null)
                return false;

            foreach (StatusEffectAndDuration effect in hero.statusEffectsAndDurations)
            {
                if (effect.statusEffect == status)
                    return true;
            }

            return false;
        }

        private static bool HasAnyStatus(Hero hero, params StatusEffect[] statuses)
        {
            foreach (StatusEffect status in statuses)
            {
                if (HasStatus(hero, status))
                    return true;
            }

            return false;
        }

        private static bool HasBuff(Hero hero, StatusEffect status)
        {
            foreach (StatusEffect buff in Buffs)
            {
                if (buff == status && HasStatus(hero, status))
                    return true;
            }

            return false;
        }

        private static bool HasDebuff(Hero hero, StatusEffect status)
        {
            foreach (StatusEffect debuff in Debuffs)
            {
                if (debuff == status && HasStatus(hero, status))
                    return true;
            }

            return false;
        }

        private static bool HasAnyBuff(Hero hero)
        {
            return HasAnyStatus(hero, Buffs);
        }

        private static bool HasAnyDebuff(Hero hero)
        {
            return HasAnyStatus(hero, Debuffs);
        }
    }
}