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

    // Team: Rogue (3 perks) / Wizard (3 perks) / Cleric (3 perks)
    // Items: 3 Ether, 3 Silence Remedy, 3 Petrify Remedy, 3 Full Remedy
    // Core: Wizard Doom bypasses all healing. QuickDispel strips AutoLife.

    public static class MyAI
    {
        public static string FolderExchangePath =
            "C:/Users/rmatt/AppData/LocalLow/Ludus Ventus/Team Hero Coder";

        private const float HP_CRITICAL = 0.30f;
        private const float HP_LOW      = 0.55f;
        private const float HP_LIGHT    = 0.75f;
        private const float MP_LOW      = 0.25f;
        private const float MP_ROGUE    = 0.20f;
        private const float FINISH_HP   = 0.35f;

        private static readonly HeroJobClass[] KillOrder =
        {
            HeroJobClass.Cleric, HeroJobClass.Alchemist, HeroJobClass.Wizard,
            HeroJobClass.Rogue,  HeroJobClass.Monk,      HeroJobClass.Fighter
        };

        public static void ProcessAI()
        {
            Hero actor = TeamHeroCoder.BattleState.heroWithInitiative;
            if (actor == null || actor.health <= 0) return;
            Console.WriteLine($"Actor: {actor.jobClass} HP:{actor.health}/{actor.maxHealth} MP:{actor.mana}/{actor.maxMana}");

            switch (actor.jobClass)
            {
                case HeroJobClass.Rogue:  ControlRogue(actor);  return;
                case HeroJobClass.Wizard: ControlWizard(actor); return;
                case HeroJobClass.Cleric: ControlCleric(actor); return;
                default:                  Wait(actor);           return;
            }
        }

        // ============================================================
        // ROGUE
        // ============================================================
        private static void ControlRogue(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MP_ROGUE)) return;

            // Lmt Brk Crafter kill Alchemist before it crafts
            if (IsLmtBrkCrafterLike())
            {
                if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Alchemist))) return;
                Hero alchemist = FindLivingEnemy(HeroJobClass.Alchemist);
                if (alchemist != null)
                {
                    if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Alchemist))) return;
                    if (Act(actor, Ability.StunStrike, alchemist)) return;
                    if (Act(actor, Ability.Attack, alchemist)) return;
                }
                if (FinishTarget(actor)) return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            // Tough Stuff focus Fighter to stop Resurrection
            if (IsToughStuffLike())
            {
                Hero fighter = FindLivingEnemy(HeroJobClass.Fighter);
                if (fighter != null)
                {
                    if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Fighter))) return;
                    if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Fighter))) return;
                    if (Act(actor, Ability.StunStrike, fighter)) return;
                    if (Act(actor, Ability.Attack, fighter)) return;
                }
                if (FinishTarget(actor)) return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            // silence casters, poison, attack
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Alchemist))) return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Wizard)))    return;
            if (Act(actor, Ability.SilenceStrike, FindUnsilenced(HeroJobClass.Fighter)))   return;

            if (FinishTarget(actor)) return;

            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Alchemist))) return;
            if (Act(actor, Ability.PoisonStrike, FindUnpoisoned(HeroJobClass.Wizard)))    return;

            if (Act(actor, Ability.StunStrike, BestAttackTarget())) return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;
            Wait(actor);
        }

        // ============================================================
        // WIZARD
        // ============================================================
        private static void ControlWizard(Hero actor)
        {
            if (UseEmergencyItem(actor)) return;
            if (UseEther(actor, MP_ROGUE)) return;

            // QuickDispel enemy AutoLife before it triggers
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (HasStatus(foe, StatusEffect.AutoLife) && Act(actor, Ability.QuickDispel, foe)) return;

            // Lmt Brk Crafter burst Alchemist down with magic Rogue silences
            if (IsLmtBrkCrafterLike())
            {
                Hero alchemist = FindLivingEnemy(HeroJobClass.Alchemist);
                if (alchemist != null)
                {
                    if (Act(actor, Ability.FlameStrike, alchemist)) return;
                    if (Act(actor, Ability.MagicMissile, alchemist)) return;
                }
                if (Act(actor, Ability.Fireball, BestMagicTarget())) return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            // Tough Stuff Meteor AOE Rogue handles Fighter
            if (IsToughStuffLike())
            {
                if (Act(actor, Ability.Meteor, BestMagicTarget())) return;
                if (Act(actor, Ability.Fireball, BestMagicTarget())) return;
                if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
                if (Act(actor, Ability.Attack, BestAttackTarget())) return;
                Wait(actor);
                return;
            }

            //Doom everything, Petrify physical, Slow, magic damage
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Alchemist))) return;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Fighter)))   return;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Monk)))      return;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Rogue)))     return;
            if (Act(actor, Ability.Doom, FindUndoomed(HeroJobClass.Wizard)))    return;

            if (Act(actor, Ability.Petrify, FindUnpetrified(HeroJobClass.Monk)))    return;
            if (Act(actor, Ability.Petrify, FindUnpetrified(HeroJobClass.Fighter))) return;
            if (Act(actor, Ability.Petrify, FindUnpetrified(HeroJobClass.Rogue)))   return;

            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Monk)))      return;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Fighter)))   return;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Cleric)))    return;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Alchemist))) return;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Wizard)))    return;
            if (Act(actor, Ability.Slow, FindUnslowed(HeroJobClass.Rogue)))     return;

            if (FinishTarget(actor)) return;
            if (Act(actor, Ability.MagicMissile, BestMagicTarget())) return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;
            Wait(actor);
        }

        // ============================================================
        // CLERIC
        // ============================================================
        private static void ControlCleric(Hero actor)
        {
            Hero dead = BestDeadAlly();
            if (dead != null && Act(actor, Ability.Resurrection, dead)) return;

            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
            if (petrified != null && Act(actor, Ability.QuickCleanse, petrified)) return;

            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            if (doomed != null && Act(actor, Ability.QuickCleanse, doomed)) return;

            if (HasStatus(actor, StatusEffect.Silence))
            {
                if (Act(actor, Ability.SilenceRemedy, actor)) return;
                if (Act(actor, Ability.FullRemedy, actor)) return;
            }

            if (UseEther(actor, MP_LOW)) return;

            if (CountBelow(HP_LOW) >= 2 && Act(actor, Ability.MassHeal, actor)) return;

            // self-heal at 75%
            if (HealthRatio(actor) <= HP_LIGHT)
            {
                if (Act(actor, Ability.QuickHeal, actor)) return;
                if (Act(actor, Ability.CureSerious, actor)) return;
            }

            // Rogue priority
            Hero rogueAlly = FindLivingAlly(HeroJobClass.Rogue);
            if (rogueAlly != null && HealthRatio(rogueAlly) <= HP_CRITICAL)
            {
                if (Act(actor, Ability.QuickHeal, rogueAlly)) return;
                if (Act(actor, Ability.CureSerious, rogueAlly)) return;
            }
            if (rogueAlly != null && HealthRatio(rogueAlly) <= HP_LOW)
                if (Act(actor, Ability.CureSerious, rogueAlly)) return;
            if (rogueAlly != null && HealthRatio(rogueAlly) <= HP_LIGHT)
                if (Act(actor, Ability.CureLight, rogueAlly)) return;

            // Wizard priority heal aggressively
            Hero wizardAlly = FindLivingAlly(HeroJobClass.Wizard);
            float wizardHealThreshold = IsToughStuffLike() ? 0.80f : HP_LOW;
            if (wizardAlly != null && HealthRatio(wizardAlly) <= wizardHealThreshold)
            {
                if (Act(actor, Ability.QuickHeal, wizardAlly)) return;
                if (Act(actor, Ability.CureSerious, wizardAlly)) return;
            }

            // lowest ally
            Hero lowest = LowestAlly();
            if (lowest != null && HealthRatio(lowest) <= HP_CRITICAL)
            {
                if (Act(actor, Ability.QuickHeal, lowest)) return;
                if (Act(actor, Ability.CureSerious, lowest)) return;
            }
            if (lowest != null && HealthRatio(lowest) <= HP_LOW)
                if (Act(actor, Ability.CureSerious, lowest)) return;

            // cleanse poison
            Hero poisonedAlly = FindAllyWithStatus(StatusEffect.Poison);
            if (poisonedAlly != null && HealthRatio(poisonedAlly) <= HP_LIGHT)
                if (Act(actor, Ability.QuickCleanse, poisonedAlly)) return;

            //Tough Stuff AutoLife on Wizard ASAP 
            if (IsToughStuffLike() && wizardAlly != null && !HasStatus(wizardAlly, StatusEffect.AutoLife)
                && HealthRatio(actor) >= 0.95f && CountBelow(HP_LOW) == 0)
                if (Act(actor, Ability.AutoLife, wizardAlly)) return;

            if (!TeamIsStable() || HealthRatio(actor) < 0.95f) goto attack;

            // Wizard AutoLife first Tough Stuff 
            if (IsToughStuffLike())
            {
                if (wizardAlly != null && !HasStatus(wizardAlly, StatusEffect.AutoLife)
                    && Act(actor, Ability.AutoLife, wizardAlly)) return;
            }

            if (rogueAlly != null && !HasStatus(rogueAlly, StatusEffect.AutoLife)
                && Act(actor, Ability.AutoLife, rogueAlly)) return;
            if (wizardAlly != null && !HasStatus(wizardAlly, StatusEffect.AutoLife)
                && Act(actor, Ability.AutoLife, wizardAlly)) return;
            if (!HasStatus(actor, StatusEffect.AutoLife)
                && Act(actor, Ability.AutoLife, actor)) return;

            if (rogueAlly != null && !HasStatus(rogueAlly, StatusEffect.Haste)
                && Act(actor, Ability.Haste, rogueAlly)) return;
            if (wizardAlly != null && !HasStatus(wizardAlly, StatusEffect.Faith)
                && Act(actor, Ability.Faith, wizardAlly)) return;
            if (!HasStatus(actor, StatusEffect.Faith)
                && Act(actor, Ability.Faith, actor)) return;

            attack:
            if (lowest != null && HealthRatio(lowest) <= HP_LIGHT)
                if (Act(actor, Ability.CureLight, lowest)) return;
            if (Act(actor, Ability.Attack, BestAttackTarget())) return;
            Wait(actor);
        }

        // ============================================================
        // SHARED ITEM USAGE
        // ============================================================
        private static bool UseEmergencyItem(Hero actor)
        {
            Hero petrified = FindAllyWithStatus(StatusEffect.Petrified, StatusEffect.Petrifying);
            if (petrified != null && Act(actor, Ability.PetrifyRemedy, petrified)) return true;
            if (petrified != null && Act(actor, Ability.FullRemedy,    petrified)) return true;

            Hero doomed = FindAllyWithStatus(StatusEffect.Doom);
            if (doomed != null && Act(actor, Ability.FullRemedy, doomed)) return true;

            Hero cleric = FindLivingAlly(HeroJobClass.Cleric);
            if (cleric != null && HasStatus(cleric, StatusEffect.Silence))
            {
                if (Act(actor, Ability.SilenceRemedy, cleric)) return true;
                if (Act(actor, Ability.FullRemedy,    cleric)) return true;
            }

            Hero wizard = FindLivingAlly(HeroJobClass.Wizard);
            if (wizard != null && HasStatus(wizard, StatusEffect.Silence))
            {
                if (Act(actor, Ability.SilenceRemedy, wizard)) return true;
                if (Act(actor, Ability.FullRemedy,    wizard)) return true;
            }

            return false;
        }

        private static bool UseEther(Hero actor, float threshold)
        {
            Hero target = null; float lowestMana = threshold + 0.001f;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float r = ManaRatio(ally);
                if (r < lowestMana) { lowestMana = r; target = ally; }
            }
            return target != null && Act(actor, Ability.Ether, target);
        }

        // ============================================================
        // ATTACK HELPERS
        // ============================================================
        private static bool FinishTarget(Hero actor)
        {
            Hero t = BestAttackTarget();
            return t != null && HealthRatio(t) <= FINISH_HP && Act(actor, Ability.Attack, t);
        }

        private static Hero BestAttackTarget()
        {
            foreach (HeroJobClass jc in KillOrder)
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                    if (foe.jobClass == jc && Legal(Ability.Attack, foe)) return foe;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (LegalIgnoreCover(Ability.Attack, foe)) return foe;
            return null;
        }

        private static Hero BestMagicTarget()
        {
            foreach (HeroJobClass jc in KillOrder)
                foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                    if (foe.jobClass == jc && Legal(Ability.MagicMissile, foe)) return foe;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (Legal(Ability.MagicMissile, foe)) return foe;
            return null;
        }

        // ============================================================
        // TARGET FINDERS
        // ============================================================
        private static Hero FindUnsilenced(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Silence)
                    && Legal(Ability.SilenceStrike, foe)) return foe;
            return null;
        }

        private static Hero FindUnpoisoned(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Poison)
                    && Legal(Ability.PoisonStrike, foe)) return foe;
            return null;
        }

        private static Hero FindUndoomed(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Doom)
                    && Legal(Ability.Doom, foe)) return foe;
            return null;
        }

        private static Hero FindUnpetrified(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc
                    && !HasStatus(foe, StatusEffect.Petrified)
                    && !HasStatus(foe, StatusEffect.Petrifying)
                    && Legal(Ability.Petrify, foe)) return foe;
            return null;
        }

        private static Hero FindUnslowed(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc && !HasStatus(foe, StatusEffect.Slow)
                    && Legal(Ability.Slow, foe)) return foe;
            return null;
        }

        private static Hero FindAllyWithStatus(params StatusEffect[] statuses)
        {
            Hero actor = TeamHeroCoder.BattleState.heroWithInitiative;
            Hero best = null; float bestScore = float.MinValue;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                if (ally == actor) continue;
                if (!HasAnyStatus(ally, statuses)) continue;
                float score = AllyValue(ally) + (1f - HealthRatio(ally)) * 100f;
                if (score > bestScore) { bestScore = score; best = ally; }
            }
            if (actor != null && HasAnyStatus(actor, statuses) && best == null) best = actor;
            return best;
        }

        private static Hero LowestAlly()
        {
            Hero best = null; float lowest = float.MaxValue;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
            {
                float r = HealthRatio(ally);
                if (r < lowest) { lowest = r; best = ally; }
            }
            return best;
        }

        private static Hero BestDeadAlly()
        {
            Hero best = null; float bestScore = float.MinValue;
            foreach (Hero ally in TeamHeroCoder.BattleState.allyHeroes)
            {
                if (ally.health > 0) continue;
                float score = AllyValue(ally);
                if (score > bestScore) { bestScore = score; best = ally; }
            }
            return best;
        }

        private static Hero FindLivingAlly(HeroJobClass jc)
        {
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (ally.jobClass == jc) return ally;
            return null;
        }

        private static Hero FindLivingEnemy(HeroJobClass jc)
        {
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc) return foe;
            return null;
        }

        // ============================================================
        // SITUATION DETECTION
        // ============================================================
        private static bool IsToughStuffLike() =>
            CountEnemyClass(HeroJobClass.Monk) >= 2 &&
            CountEnemyClass(HeroJobClass.Fighter) >= 1 &&
            FindLivingEnemy(HeroJobClass.Cleric)    == null &&
            FindLivingEnemy(HeroJobClass.Alchemist) == null &&
            FindLivingEnemy(HeroJobClass.Wizard)    == null;

        private static bool IsLmtBrkCrafterLike() =>
            CountEnemyClass(HeroJobClass.Monk) >= 2 &&
            FindLivingEnemy(HeroJobClass.Alchemist) != null;

        private static bool EnemyHasCaster() =>
            FindLivingEnemy(HeroJobClass.Cleric)    != null ||
            FindLivingEnemy(HeroJobClass.Alchemist) != null ||
            FindLivingEnemy(HeroJobClass.Wizard)    != null;

        private static bool TeamIsStable()
        {
            if (CountBelow(HP_LOW) > 0) return false;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (HasAnyStatus(ally, StatusEffect.Doom, StatusEffect.Petrified,
                    StatusEffect.Petrifying, StatusEffect.Poison)) return false;
            return true;
        }

        private static int CountBelow(float threshold)
        {
            int c = 0;
            foreach (Hero ally in Living(TeamHeroCoder.BattleState.allyHeroes))
                if (HealthRatio(ally) <= threshold) c++;
            return c;
        }

        private static int CountEnemyClass(HeroJobClass jc)
        {
            int c = 0;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (foe.jobClass == jc) c++;
            return c;
        }

        private static int CountUnpoisonedEnemies()
        {
            int c = 0;
            foreach (Hero foe in Living(TeamHeroCoder.BattleState.foeHeroes))
                if (!HasStatus(foe, StatusEffect.Poison)) c++;
            return c;
        }

        // ============================================================
        // CORE HELPERS
        // ============================================================
        private static bool Act(Hero actor, Ability ability, Hero target)
        {
            if (actor == null || target == null) return false;
            if (!Utility.AreAbilityAndTargetLegal(ability, target, false)) return false;
            Console.WriteLine($"{actor.jobClass} uses {ability} on {target.jobClass}");
            TeamHeroCoder.PerformHeroAbility(ability, target);
            return true;
        }

        private static bool Legal(Ability ability, Hero target) =>
            target != null && Utility.AreAbilityAndTargetLegal(ability, target, false);

        private static bool LegalIgnoreCover(Ability ability, Hero target) =>
            target != null && Utility.AreAbilityAndTargetLegal(ability, target, true);

        private static void Wait(Hero actor)
        {
            Console.WriteLine($"{actor.jobClass} waits");
            TeamHeroCoder.PerformHeroAbility(Ability.Wait, actor);
        }

        private static IEnumerable<Hero> Living(IEnumerable<Hero> heroes)
        {
            foreach (Hero h in heroes)
                if (h.health > 0) yield return h;
        }

        private static float HealthRatio(Hero h) =>
            h == null || h.maxHealth <= 0 ? 1f : (float)h.health / h.maxHealth;

        private static float ManaRatio(Hero h) =>
            h == null || h.maxMana <= 0 ? 1f : (float)h.mana / h.maxMana;

        private static float AllyValue(Hero h)
        {
            float s = 0f;
            if (h.jobClass == HeroJobClass.Cleric) s += 350f;
            if (h.jobClass == HeroJobClass.Wizard) s += 320f;
            if (h.jobClass == HeroJobClass.Rogue)  s += 280f;
            return s + h.speed * 2f + h.physicalAttack * 1.8f + h.special * 1.5f;
        }

        private static bool HasStatus(Hero h, StatusEffect s)
        {
            if (h == null) return false;
            foreach (var e in h.statusEffectsAndDurations)
                if (e.statusEffect == s) return true;
            return false;
        }

        private static bool HasAnyStatus(Hero h, params StatusEffect[] statuses)
        {
            foreach (var s in statuses)
                if (HasStatus(h, s)) return true;
            return false;
        }
    }
}