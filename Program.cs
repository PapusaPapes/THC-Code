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

    // Team: Rogue / Alchemist / Cleric
    // Items: 2 Ether, 42 Essence
    // Strategy:
    // Alchemist crafts Ethers and Elixirs to fuel the Rogue's Item Jockey passive.
    // Rogue uses items to chain turns with Item Jockey (40% initiative refund per item used).
    // Between item uses, Rogue Silences, Poisons, and Stunstrikes enemies.
    // Cleric keeps the team alive and stacks Faith and Haste on the Rogue.

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
        private const float MpLow   = 0.25f;
        private const float MpRogue = 0.40f;

        // Combat thresholds 
        private const float FinishHp = 0.35f;

        // Alchemist essence costs
        private const int MinManaToSlow    = 15;
        private const int EssenceCostTier1 = 2;  // Ether, Revive, Remedies
        private const int EssenceCostTier2 = 3;  // Elixir
        private const int EssenceCostTier3 = 4;  // Mega Elixir

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

        // Cleric keeps the team alive, revive it first, Rogue last
        private static readonly HeroJobClass[] ReviveOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Rogue
        };

        // Cleric is most important to cleanse
        private static readonly HeroJobClass[] CleanseOrder =
        {
            HeroJobClass.Cleric,
            HeroJobClass.Alchemist,
            HeroJobClass.Rogue
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
                case HeroJobClass.Rogue:
                    ControlRogue(actor);
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
        // ROGUE
        // ============================================================

        private static void ControlRogue(Hero actor)
        {
            // Emergency items first, can't chain turns if we're dead
            if (UseEmergencyItem(actor)) return;

            // Use an Ether to chain a turn with Item Jockey if mana is low
            if (UseEtherOnSelf(actor, MpRogue)) return;

            // Use a healing item to chain a turn if we're low
            if (UseHealingItemForTempo(actor)) return;

            // Ctrl & Sustain: 2 Alchemists + Monk, steal their items to cut off sustain
            if (IsCtrlAndSustainLike())
            {
                if (StealFromEnemy(actor))           return;
                if (SilenceCasters(actor))           return;
                if (PoisonThreats(actor))            return;
                if (StunThreats(actor))              return;
                if (FinishPhysicalTarget(actor))     return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                if (Act(actor, Ability.Attack, FirstLivingFoe()))   return;

                Wait(actor);
                return;
            }

            // Slow Bash: Silence Wizard, kill Fighter, kill Rogue in that order
            if (IsSlowBashLike())
            {
                if (SilenceWizardFirst(actor))                                   return;

                // Kill Fighter first, stops Resurrection
                Hero fighter = FindLivingFoe(HeroJobClass.Fighter);
                if (fighter != null)
                {
                    if (FinishPhysicalTarget(actor))                             return;
                    if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Fighter))) return;
                    if (Act(actor, Ability.StunStrike,   fighter))               return;
                    if (Act(actor, Ability.Attack,       fighter))               return;
                }

                // Kill Rogue second, stops item theft
                Hero rogue = FindLivingFoe(HeroJobClass.Rogue);
                if (rogue != null)
                {
                    if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Rogue))) return;
                    if (Act(actor, Ability.StunStrike,   rogue))                 return;
                    if (Act(actor, Ability.Attack,       rogue))                 return;
                }

                if (Act(actor, Ability.Attack, BestAttackTarget()))              return;
                if (Act(actor, Ability.Attack, FirstLivingFoe()))                return;

                Wait(actor);
                return;
            }

            // Silence enemy healers and crafters, shuts down their sustain
            if (SilenceCasters(actor))       return;
            if (FinishPhysicalTarget(actor)) return;
            if (PoisonThreats(actor))        return;
            if (StunThreats(actor))          return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;
            if (Act(actor, Ability.Attack, FirstLivingFoe()))   return;

            Wait(actor);
        }

        // Steal items from the enemy to cut off their sustain
        private static bool StealFromEnemy(Hero actor)
        {
            foreach (HeroJobClass jobClass in KillOrder)
            {
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                {
                    if (foe.jobClass == jobClass &&
                        Act(actor, Ability.Steal, foe)) return true;
                }
            }

            return false;
        }

        // Use a healing item when low to chain turns with Item Jockey
        private static bool UseHealingItemForTempo(Hero actor)
        {
            if (HpRatio(actor) > HpLow) return false;

            if (Act(actor, Ability.Elixir,   actor)) return true;
            if (Act(actor, Ability.Potion,   actor)) return true;

            return false;
        }

        private static bool SilenceCasters(Hero actor)
        {
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Cleric)))    return true;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Alchemist))) return true;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Wizard)))    return true;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Fighter)))   return true;

            return false;
        }

        private static bool PoisonThreats(Hero actor)
        {
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Cleric)))    return true;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Alchemist))) return true;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Wizard)))    return true;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Monk)))      return true;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Fighter)))   return true;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Rogue)))     return true;

            return false;
        }

        private static bool StunThreats(Hero actor)
        {
            if (Act(actor, Ability.StunStrike, BestAttackTarget())) return true;

            return false;
        }

        // ============================================================
        // ALCHEMIST
        // ============================================================

        private static void ControlAlchemist(Hero actor)
        {
            if (SlowEnemyWizard(actor))          return;
            if (CleansePetrifyIfNoRemedy(actor)) return;
            if (CleanseDoomIfNoRemedy(actor))    return;

            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MpLow))  return;

            if (CraftNeededRemedy(actor))   return;

            // Fight specific proactive crafting
            if (CraftVsMultipleWizards(actor)) return;
            if (CraftVsTrinityDoom(actor))     return;

            // Prioritize crafting Ethers to fuel Rogue's Item Jockey chains
            if (CraftTempoItems(actor))     return;
            if (CraftSupportItems(actor))   return;
            if (ReviveOrCraftRevive(actor)) return;

            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            // Haste the Rogue to maximise Item Jockey chains
            if (HasteRogue(actor)) return;

            if (actor.mana >= MinManaToSlow && SlowAllTargets(actor)) return;

            if (Act(actor, Ability.Attack, BestAttackTarget())) return;

            Wait(actor);
        }

        private static bool SlowEnemyWizard(Hero actor)
        {
            if (actor.mana < MinManaToSlow)             return false;
            if (FindLivingFoe(HeroJobClass.Wizard) == null) return false;

            return Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Wizard));
        }

        // Haste the Rogue to give it more turns for Item Jockey chains
        private static bool HasteRogue(Hero actor)
        {
            if (!TeamIsStable()) return false;

            Hero rogue = FindLivingAlly(HeroJobClass.Rogue);

            if (rogue == null)                          return false;
            if (HasStatus(rogue, StatusEffect.Haste))   return false;

            return Act(actor, Ability.Haste, rogue);
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

        // Ethers are the primary fuel for Item Jockey, craft them before anything else
        private static bool CraftTempoItems(Hero actor)
        {
            if (Essence() >= EssenceCostTier1 &&
                !AnyAllyHasItem(Ability.Ether) &&
                SelfCast(actor, Ability.CraftEther)) return true;

            if (Essence() >= EssenceCostTier2 &&
                !AnyAllyHasItem(Ability.Elixir) &&
                !AnyAllyHasItem(Ability.MegaElixir) &&
                SelfCast(actor, Ability.CraftElixir)) return true;

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
        // CLERIC
        // ============================================================

        private static void ControlCleric(Hero actor)
        {
            if (ResurrectDeadAlly(actor))   return;
            if (CleanseUrgentDebuffs(actor)) return;
            if (RemoveOwnSilence(actor))    return;
            if (UseEther(actor, MpLow))     return;
            if (HealTeam(actor))            return;
            if (CleansePoisonedAlly(actor)) return;
            if (DispelEnemyAutoLife(actor, Ability.Dispel)) return;

            if (TeamIsStable() && HpRatio(actor) >= HpStableCleric)
            {
                if (ApplyAutoLife(actor))      return;
                if (ApplyFaithAndHaste(actor)) return;
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
            if (HealRogue(actor))  return true;
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

        // Rogue needs to stay healthy to keep chaining Item Jockey turns
        private static bool HealRogue(Hero actor)
        {
            Hero rogue = FindLivingAlly(HeroJobClass.Rogue);

            if (rogue == null) return false;

            if (HpRatio(rogue) <= HpCritical)
            {
                if (Act(actor, Ability.QuickHeal,   rogue)) return true;
                if (Act(actor, Ability.CureSerious, rogue)) return true;
            }

            if (HpRatio(rogue) <= HpLow &&
                Act(actor, Ability.CureSerious, rogue)) return true;

            if (HpRatio(rogue) <= HpLight &&
                Act(actor, Ability.CureLight, rogue)) return true;

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

        // Faith boosts Cleric heals, Haste gives Rogue more turns for Item Jockey chains
        private static bool ApplyFaithAndHaste(Hero actor)
        {
            Hero rogue = FindLivingAlly(HeroJobClass.Rogue);
            if (rogue != null &&
                !HasStatus(rogue, StatusEffect.Haste) &&
                Act(actor, Ability.Haste, rogue)) return true;

            if (!HasStatus(actor, StatusEffect.Faith) &&
                Act(actor, Ability.Faith, actor)) return true;

            Hero alchemist = FindLivingAlly(HeroJobClass.Alchemist);
            if (alchemist != null &&
                !HasStatus(alchemist, StatusEffect.Haste) &&
                Act(actor, Ability.Haste, alchemist)) return true;

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

        private static bool UseEtherOnSelf(Hero actor, float threshold)
        {
            if (MpRatio(actor) >= threshold) return false;

            return Act(actor, Ability.Ether, actor);
        }

        private static bool UseEther(Hero actor, float threshold)
        {
            Hero target    = null;
            float lowestMp = threshold + 0.001f;

            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                // Rogue manages its own mana, save Ethers for casters
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

        private static Hero FindUnsilenced(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Silence, Ability.SilenceStrike, ignoreCover: true);
        }

        private static Hero FindUnpoisoned(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Poison, Ability.PoisonStrike, ignoreCover: true);
        }

        private static Hero FindUnslowed(HeroJobClass jobClass)
        {
            return FindFoeWithout(jobClass, StatusEffect.Slow, Ability.Slow, ignoreCover: false);
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

        // 2+ enemy Wizards, Petrify spam, craft Petrify and Full Remedies immediately
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

        // 3 Wizards, Trinity Doom spams Doom on all allies, craft Full Remedies immediately
        private static bool CraftVsTrinityDoom(Hero actor)
        {
            if (CountEnemyClass(HeroJobClass.Wizard) < 3) return false;
            if (Essence() < EssenceCostTier1)             return false;

            if (!AnyAllyHasItem(Ability.FullRemedy) &&
                SelfCast(actor, Ability.CraftFullRemedy)) return true;

            return false;
        }

        // 2 Alchemists + Monk, near infinite sustain, need to steal their items
        private static bool IsCtrlAndSustainLike()
        {
            return CountEnemyClass(HeroJobClass.Alchemist) >= 2 &&
                   CountEnemyClass(HeroJobClass.Monk)      >= 1;
        }

        // Wizard + Rogue + Fighter, enemy Rogue steals our items
        private static bool IsSlowBashLike()
        {
            return CountEnemyClass(HeroJobClass.Wizard)  >= 1 &&
                   CountEnemyClass(HeroJobClass.Rogue)   >= 1 &&
                   CountEnemyClass(HeroJobClass.Fighter) >= 1;
        }

        // Silence the Wizard immediately in Slow Bash to stop debuffs
        private static bool SilenceWizardFirst(Hero actor)
        {
            return Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Wizard));
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

        // Fallback, returns any living foe regardless of cover or priority
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